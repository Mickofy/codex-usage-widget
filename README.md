# Codex Usage Widget

A lightweight Windows utility for viewing your real Codex usage limits directly in the taskbar area.

## Current behavior

The app uses a compact **taskbar overlay** rather than a tray icon or floating desktop card.

It shows:

- `5H` — remaining percentage in the 5-hour window
- `W` — remaining percentage in the weekly window
- reset countdowns
- a small live / refreshing / error status dot

The widget:

- starts inside the taskbar area, just to the right of the Weather/Widgets button by default
- stays locked to the taskbar vertically
- can be dragged left/right along the taskbar
- remembers its horizontal position between launches
- refreshes Codex usage automatically every 2 minutes
- updates reset countdowns every 30 seconds
- has a right-click menu with Refresh, Place beside Weather, Open log, and Exit
- does not create a system-tray icon

## Important Windows limitation

Windows 11 does not expose a supported API for third-party apps to insert arbitrary custom controls directly into the built-in taskbar beside Weather.

This project therefore uses a separate borderless topmost window positioned over unused taskbar space. It visually behaves like a taskbar widget without injecting code into Windows Explorer. That approach is intentionally safer and less likely to break Windows shell behavior.

## Requirements

- Windows 10/11
- Codex CLI installed and signed in
- .NET 8 SDK for development

Check:

```powershell
codex --version
dotnet --version
```

## Run from source

```powershell
git pull
dotnet run
```

The widget should appear in the taskbar area near Weather.

Drag it horizontally if you want a different position. The app stores that position locally under `%LOCALAPPDATA%\CodexUsageWidget`.

## How it works

The app starts the local Codex App Server and reads rate-limit information through:

```text
account/rateLimits/read
```

It recognizes:

- `300` minutes as the 5-hour window
- `10080` minutes as the weekly window

Missing windows are displayed as unavailable rather than estimated.

## Publish an EXE

Run:

```powershell
.\publish.ps1
```

The framework-dependent published app is created under:

```text
bin\Release\net8.0-windows\publish\
```

Run:

```text
CodexUsageWidget.exe
```

Keep the files in the publish folder together.

## Logs

Diagnostic logs are written locally to:

```text
%LOCALAPPDATA%\CodexUsageWidget\app.log
```

## Planned improvements

- start automatically when Windows signs in
- low-usage notifications at 20%, 10%, and 5%
- optional compact mode with percentages only
- usage history and trend
