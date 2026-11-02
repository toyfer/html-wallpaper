# HtmlWallpaper

Windows 11 desktop wallpaper from a local HTML file. Tray only: start, stop, open HTML, quit. No admin. One folder.

## What it does

- Renders a local HTML file with WebView2.
- Parents that window to Explorer's `WorkerW` so it sits under desktop icons.
- Detach restores the system still image.
- Runs `asInvoker`. No UAC. Not a Windows service.
- Keeps WebView2 user data next to the exe (`webview2-data/`). No `%AppData%`.
- Start re-reads `config.json` and reloads the HTML (cache ignored). Quit is not required.

## Requirements

- Windows 11 (24H2 / 25H2).
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed).
- .NET 8 Desktop Runtime if you use the framework-dependent publish. The CI artifact is that build.

## Run

Put these next to each other and start the exe:

```
HtmlWallpaper.exe
wallpaper.html
config.json
```

Tray menu:

| item | action |
|---|---|
| Start | re-read `config.json`, reload HTML + CSS/JS, attach to WorkerW |
| Stop | detach, restore the still wallpaper, keep the process |
| Open HTML... | pick a file, write it to `config.json`, load it |
| Quit | stop, then exit |

CLI:

```
HtmlWallpaper.exe          # start wallpaper, stay in tray
HtmlWallpaper.exe --quit   # tell a running instance to quit
```

A second launch while one is running does nothing except `--quit`.

## HTML, CSS, JS

WebView2 is a browser. Relative links next to the HTML already load:

```html
<link rel="stylesheet" href="style.css">
<script src="app.js"></script>
<img src="img/bg.png">
```

```
clock/
  index.html
  style.css
  app.js
  img/
```

`config.json`:

```json
{ "html": "clock/index.html" }
```

Same folder, a subfolder, or an absolute path. CSS/JS/images resolve from the HTML file, not from the exe. No extra app code is required for that.

`file://` pages cannot use some HTTPS-only APIs (camera, geolocation, notifications). `localStorage` is per file path.

## Layout

```
src/HtmlWallpaper/
  HtmlWallpaper.csproj
  Program.cs          Win32 + tray + WebView2
  app.manifest        asInvoker
  wallpaper.html      sample page
  config.json         { "html": "wallpaper.html" }
```

## Build

```
dotnet publish src/HtmlWallpaper/HtmlWallpaper.csproj -c Release -r win-x64 --self-contained false -o dist
```

## CI that this chat can read

GitHub MCP cannot list Actions runs. Every non-PR run therefore writes the outcome to issue **CI results** (`ci-report` label): the issue body is the latest status, each comment is one run. Open that issue instead of the Actions tab.

## Out of scope

Multi-monitor layout UI, playlist, store, installer, service, AppData, elevation.
