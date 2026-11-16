// HtmlWallpaper - shows an HTML file or web page as the Windows desktop wallpaper.
// A WebView2 (Edge) view is placed behind the desktop icons; a tray icon controls it.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x).
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

static class Native
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L;
    public const long WS_EX_LAYERED = 0x00080000L, WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    public const uint LWA_ALPHA = 0x2;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);
}

static class Log
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HtmlWallpaper");
    public static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string file = Path.Combine(Dir, "debug.log");
            if (File.Exists(file) && new FileInfo(file).Length > 200000) File.Delete(file);
            File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
        }
        catch { }
    }
}

// The window that sits behind the desktop icons and holds the web view.
class WallForm : Form
{
    public readonly WebView2 Web;

    public WallForm(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        Opacity = 0;   // invisible (and already a layered window) until it is in place behind the icons
        Web = new WebView2();
        Web.Dock = DockStyle.Fill;
        Web.DefaultBackgroundColor = Color.Black;
        Controls.Add(Web);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            return cp;
        }
    }
}

// Hidden top-level window: hears when Explorer restarts (the wallpaper must then be rebuilt).
class MessageWindow : NativeWindow
{
    readonly int taskbarCreated;
    public event EventHandler ExplorerRestarted;

    public MessageWindow()
    {
        taskbarCreated = (int)Native.RegisterWindowMessage("TaskbarCreated");
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == taskbarCreated && ExplorerRestarted != null) ExplorerRestarted(this, EventArgs.Empty);
        base.WndProc(ref m);
    }
}

class App : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "HtmlWallpaper";

    readonly NotifyIcon tray;
    readonly MessageWindow messages;
    readonly ToolStripMenuItem startItem;
    readonly System.Windows.Forms.Timer rebuildTimer;
    readonly string settingsFile = Path.Combine(Log.Dir, "settings.txt");
    WallForm wall;
    string source;

    public App(string[] args)
    {
        Log.Write("---- start, Windows " + Environment.OSVersion.Version);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Choose HTML file...", null, delegate { ChooseFile(); });
        menu.Items.Add("Open web address...", null, delegate { ChooseAddress(); });
        menu.Items.Add("Reload", null, delegate { Rebuild(); });
        menu.Items.Add(new ToolStripSeparator());
        startItem = new ToolStripMenuItem("Start with Windows");
        startItem.Checked = IsAutoStart();
        startItem.Click += delegate { SetAutoStart(!startItem.Checked); startItem.Checked = IsAutoStart(); };
        menu.Items.Add(startItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (restore normal wallpaper)", null, delegate { Quit(); });

        tray = new NotifyIcon();
        tray.Icon = SystemIcons.Application;
        tray.Text = "HTML Wallpaper";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate { ChooseFile(); };

        messages = new MessageWindow();
        rebuildTimer = new System.Windows.Forms.Timer();
        rebuildTimer.Interval = 2500;
        rebuildTimer.Tick += delegate { rebuildTimer.Stop(); Rebuild(); };
        messages.ExplorerRestarted += delegate { Log.Write("Explorer restarted"); rebuildTimer.Stop(); rebuildTimer.Start(); };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (args.Length > 0) { source = args[0]; Save(); }
        else { try { if (File.Exists(settingsFile)) source = File.ReadAllText(settingsFile).Trim(); } catch { } }

        if (string.IsNullOrEmpty(source))
        {
            tray.ShowBalloonTip(6000, "HTML Wallpaper", "Pick an HTML file to use as wallpaper.", ToolTipIcon.Info);
            ChooseFile();
        }
        else Rebuild();
    }

    // ---------- choosing what to show ----------
    void ChooseFile()
    {
        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "Choose an HTML file to use as wallpaper";
            dlg.Filter = "Web pages (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*";
            if (dlg.ShowDialog() == DialogResult.OK) { source = dlg.FileName; Save(); Rebuild(); }
        }
    }

    void ChooseAddress()
    {
        using (Form f = new Form())
        {
            f.Text = "Web address to use as wallpaper";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MinimizeBox = false; f.MaximizeBox = false;
            f.ClientSize = new Size(460, 84);
            TextBox box = new TextBox();
            box.SetBounds(12, 14, 436, 24);
            box.Text = (source != null && source.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ? source : "https://";
            Button ok = new Button();
            ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
            ok.SetBounds(368, 46, 80, 28);
            f.Controls.Add(box); f.Controls.Add(ok);
            f.AcceptButton = ok;
            if (f.ShowDialog() == DialogResult.OK && box.Text.Trim().Length > 8) { source = box.Text.Trim(); Save(); Rebuild(); }
        }
    }

    void Save()
    {
        try { Directory.CreateDirectory(Log.Dir); File.WriteAllText(settingsFile, source); } catch { }
    }

    static string ToUrl(string s)
    {
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return s;
        if (File.Exists(s)) return new Uri(Path.GetFullPath(s)).AbsoluteUri;
        return "https://" + s;
    }

    // ---------- the wallpaper window ----------
    void Rebuild()
    {
        if (string.IsNullOrEmpty(source)) return;
        CloseWall();
        try
        {
            wall = new WallForm(Screen.PrimaryScreen.Bounds);
            wall.Show();
            Attach(wall.Handle);
            StartWeb(wall, ToUrl(source));
        }
        catch (Exception ex)
        {
            Log.Write("Rebuild failed: " + ex);
            MessageBox.Show("Could not set the wallpaper:\n" + ex.Message, "HTML Wallpaper");
        }
    }

    void CloseWall()
    {
        if (wall == null) return;
        try { if (!wall.IsDisposed) wall.Dispose(); } catch { }
        wall = null;
    }

    async void StartWeb(WallForm form, string url)
    {
        try
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HtmlWallpaper", "WebView2");
            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
            options.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-features=CalculateNativeWinOcclusion";
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data, options);
            if (form.IsDisposed) return;
            await form.Web.EnsureCoreWebView2Async(env);
            if (form.IsDisposed) return;
            form.Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            form.Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            form.Web.CoreWebView2.Navigate(url);
            Log.Write("Showing " + url);
        }
        catch (Exception ex)
        {
            Log.Write("WebView2 failed: " + ex);
            MessageBox.Show("The web view could not start:\n" + ex.Message +
                "\n\nIf the Microsoft Edge WebView2 Runtime is missing, install it from Microsoft and try again.", "HTML Wallpaper");
        }
    }

    // Puts the window between the desktop icons and the normal wallpaper.
    void Attach(IntPtr hwnd)
    {
        IntPtr progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("The desktop window (Progman) was not found.");

        // Ask Explorer to split the desktop into an icon layer and a wallpaper layer.
        IntPtr result;
        Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out result);

        long progmanEx = Native.GetWindowLongPtr(progman, Native.GWL_EXSTYLE).ToInt64();
        IntPtr iconsInProgman = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        bool raised = (progmanEx & Native.WS_EX_NOREDIRECTIONBITMAP) != 0 && iconsInProgman != IntPtr.Zero;

        // Turn the window into a child window. It stays layered for now (alpha 255 = fully visible).
        long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
        style = (style & ~Native.WS_POPUP) | Native.WS_CHILD;
        Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_LAYERED;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);

        IntPtr parent, insertAfter;
        if (raised)
        {
            // Windows 11 24H2 and later: icons and wallpaper are both children of Progman.
            // The window goes directly under the icons, above Explorer's own wallpaper window.
            parent = progman;
            insertAfter = iconsInProgman;
        }
        else
        {
            // Windows 10 and older Windows 11: the wallpaper layer is the WorkerW window
            // that follows the top-level window holding the icons.
            IntPtr workerw = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr top, IntPtr l)
            {
                if (Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    workerw = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            if (workerw == IntPtr.Zero) workerw = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            parent = workerw != IntPtr.Zero ? workerw : progman;
            insertAfter = IntPtr.Zero;   // top of the wallpaper layer
        }

        Native.SetParent(hwnd, parent);
        if (!raised)
        {
            // a layered window is only needed on the raised desktop
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_LAYERED));
        }
        Place(hwnd, parent, insertAfter);
        Log.Write("Attached. raised=" + raised + " progman=" + progman.ToString("X") + " parent=" + parent.ToString("X") +
                  " icons=" + iconsInProgman.ToString("X") + " hwnd=" + hwnd.ToString("X"));
        attachedParent = parent; attachedAfter = insertAfter;
    }

    IntPtr attachedParent, attachedAfter;

    static void Place(IntPtr hwnd, IntPtr parent, IntPtr insertAfter)
    {
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        Native.RECT pr;
        Native.GetWindowRect(parent, out pr);
        Native.SetWindowPos(hwnd, insertAfter, screen.X - pr.Left, screen.Y - pr.Top, screen.Width, screen.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
    }

    void OnDisplayChanged(object sender, EventArgs e)
    {
        try
        {
            if (wall != null && !wall.IsDisposed && Native.IsWindow(attachedParent)) Place(wall.Handle, attachedParent, attachedAfter);
            else { rebuildTimer.Stop(); rebuildTimer.Start(); }
        }
        catch (Exception ex) { Log.Write("Display change: " + ex.Message); }
    }

    // ---------- start with Windows ----------
    static bool IsAutoStart()
    {
        try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) { return k != null && k.GetValue(AppName) != null; } }
        catch { return false; }
    }

    static void SetAutoStart(bool on)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex) { MessageBox.Show("Could not change the startup setting:\n" + ex.Message, "HTML Wallpaper"); }
    }

    void Quit()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        CloseWall();
        tray.Visible = false;
        tray.Dispose();
        Native.SystemParametersInfo(20, 0, null, 1);   // repaint the normal wallpaper
        Log.Write("exit");
        ExitThread();
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool first;
        using (Mutex mutex = new Mutex(true, "HtmlWallpaper.SingleInstance", out first))
        {
            if (!first)
            {
                MessageBox.Show("HTML Wallpaper is already running. Look for its icon in the tray, next to the clock.", "HTML Wallpaper");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("Error: " + e.Exception); };
            Application.Run(new App(args));
        }
    }
}
