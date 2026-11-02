# HtmlWallpaper

Windows 11 desktop wallpaper from a local HTML file. Tray only: start, stop, quit. No admin. One folder.

## What it does

- Renders `wallpaper.html` with WebView2.
- Parents that window to Explorer's `WorkerW` so it sits under desktop icons.
- Does not call `SPI_SETDESKWALLPAPER`. The system still image comes back when the window is detached.
- Runs `asInvoker`. No UAC. Not a Windows service.
- Keeps WebView2 user data next to the exe (`webview2-data/`). No `%AppData%`.

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
| Start | attach HTML to WorkerW |
| Stop | detach, restore the still wallpaper, keep the process |
| Quit | stop, then exit |

CLI:

```
HtmlWallpaper.exe          # start wallpaper, stay in tray
HtmlWallpaper.exe --quit   # tell a running instance to quit
```

A second launch while one is running just focuses the existing tray instance. `--quit` is the only extra switch.

## Layout

```
src/HtmlWallpaper/
  HtmlWallpaper.csproj
  Program.cs          Win32 + tray + WebView2
  app.manifest        asInvoker
  wallpaper.html      sample page
  config.json         { "html": "wallpaper.html" }
```

Edit `wallpaper.html` or point `config.json` at another file in the same folder.

## Build

```
dotnet publish src/HtmlWallpaper/HtmlWallpaper.csproj -c Release -r win-x64 --self-contained false -o dist
```

## CI that this chat can read

GitHub MCP cannot list Actions runs. Every non-PR run therefore writes the outcome to issue **CI results** (`ci-report` label): the issue body is the latest status, each comment is one run. Open that issue instead of the Actions tab.

## Out of scope

Multi-monitor layout UI, playlist, store, installer, service, AppData, elevation.
