# Codex Usage Widget

A lightweight Windows widget for viewing your real Codex usage limits directly above the taskbar.

## Current behavior

The app now uses an **always-visible desktop/taskbar widget** instead of a system-tray icon.

It shows:

- `5H` — remaining percentage in the 5-hour window
- `WEEK` — remaining percentage in the weekly window
- reset countdown for each window
- live / refreshing / error status

The widget:

- stays on top
- starts near the bottom-right above the Windows taskbar
- refreshes Codex usage automatically every 2 minutes
- updates reset countdowns every 30 seconds
- can be dragged anywhere with the mouse
- can be double-clicked to snap back above the taskbar
- has a right-click menu with:
  - Refresh
  - Snap to taskbar
  - Open log
  - Exit

There is **no tray icon** in this version.

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

The widget should appear immediately above the taskbar.

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

You can also right-click the widget and choose **Open log**.

## Planned improvements

- start automatically when Windows signs in
- remember custom widget position
- compact / expanded widget modes
- low-usage notifications at 20%, 10%, and 5%
- optional light theme
- usage history and trend
