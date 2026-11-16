@echo off
setlocal
cd /d "%~dp0"
echo Building HTML Wallpaper...

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The .NET Framework compiler was not found at %CSC%
  goto fail
)

rem The WebView2 SDK (three small DLLs from Microsoft) is downloaded once from nuget.org.
if not exist sdk\lib (
  echo Downloading the Microsoft WebView2 SDK from nuget.org...
  curl -L -f -s -S -o sdk.zip https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2
  if errorlevel 1 goto fail
  if not exist sdk mkdir sdk
  tar -xf sdk.zip -C sdk
  if errorlevel 1 goto fail
  del sdk.zip
)

set "LIB="
for /d %%d in (sdk\lib\net4*) do if exist "%%d\Microsoft.Web.WebView2.WinForms.dll" set "LIB=%%d"
if "%LIB%"=="" (
  echo The WebView2 SDK files were not found in sdk\lib.
  goto fail
)

if not exist app mkdir app
copy /y "%LIB%\Microsoft.Web.WebView2.Core.dll" app\ >nul
copy /y "%LIB%\Microsoft.Web.WebView2.WinForms.dll" app\ >nul
copy /y sdk\runtimes\win-x64\native\WebView2Loader.dll app\ >nul

taskkill /im HtmlWallpaper.exe /f >nul 2>&1
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /win32manifest:app.manifest ^
  /out:app\HtmlWallpaper.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:app\Microsoft.Web.WebView2.Core.dll /r:app\Microsoft.Web.WebView2.WinForms.dll ^
  HtmlWallpaper.cs
if errorlevel 1 goto fail

echo.
echo Done. Starting app\HtmlWallpaper.exe - look for its icon in the tray, next to the clock.
start "" "app\HtmlWallpaper.exe"
timeout /t 5 >nul
exit /b 0

:fail
echo.
echo Build failed. Copy the text above and send it to Claude.
pause
exit /b 1
