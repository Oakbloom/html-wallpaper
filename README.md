# HTML Wallpaper

Use any HTML file or web page as an animated desktop wallpaper on Windows. The page runs in the Edge engine (WebView2) behind your desktop icons, and a tray icon controls it.

## Set up

1. Download this repository (Code → Download ZIP) and unzip it somewhere permanent.
2. Double-click `build.cmd`. It downloads Microsoft's WebView2 SDK from nuget.org, compiles `HtmlWallpaper.cs` with the C# compiler built into Windows, and starts the app. Windows may warn about running a downloaded script.
3. Pick an HTML file when asked. Try `examples/oakbloom-markets-wallpaper.html` or `examples/silk-markets-wallpaper.html`.

Nothing else needs installing: the compiler ships with Windows, and the WebView2 runtime is part of Windows 11 and current Windows 10.

## Use

The app sits in the tray next to the clock. Right-click its icon for:

- **Choose HTML file** – use a local page
- **Open web address** – use a web page
- **Reload**
- **Start with Windows** – on by default; untick it to turn it off
- **Exit** – restores your normal wallpaper

After the first build, start it with `app\HtmlWallpaper.exe`. You can also pass a file or address as an argument.

## Examples

Both examples show the same live market panel (S&P 500, the Magnificent Seven, Bitcoin and gold) and can be previewed in the browser at [oakbloom.se/apps/html-wallpaper](http://www.oakbloom.se/apps/html-wallpaper/).

### Silk markets

`examples/silk-markets-wallpaper.html` is flowing blue silk under a violet night sky, drawn live with a WebGL shader and no libraries. The colours shift cooler on days the S&P 500 is up and more violet on down days. You can also point the app at the hosted copy with **Open web address**: `http://www.oakbloom.se/apps/html-wallpaper/silk-markets.html`.

### Oakbloom markets

`examples/oakbloom-markets-wallpaper.html` is the glowing oak scene from [oakbloom.se](http://www.oakbloom.se/) with a live market panel: the S&P 500, the Magnificent Seven, Bitcoin and gold.

- Stocks and gold come from Yahoo Finance through the r.jina.ai reader (Yahoo blocks direct browser requests) and refresh every minute.
- Bitcoin comes from Binance every 15 seconds.
- Add `?fps=60` to the address for smoother motion; the default cap is 30 fps.

## How it works

Windows has no official way to draw behind the desktop icons. The app sends the undocumented `0x052C` message to the `Progman` window so Explorer splits the desktop into an icon layer and a wallpaper layer, then parents its own window between them:

- **Windows 10 and Windows 11 up to 23H2:** inside the `WorkerW` window that follows the icons' host window.
- **Windows 11 24H2 and later:** as a layered child of `Progman`, directly below `SHELLDLL_DefView`.

It re-attaches itself when Explorer restarts.

## Limits

- Covers the primary monitor only.
- The page does not react to the mouse, because it sits behind the icons.
- It keeps animating while other windows cover it.

Settings and a `debug.log` are kept in `%AppData%\HtmlWallpaper`.

## License

[MIT](LICENSE)
