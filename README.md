# Codex Usage Widget

A lightweight Windows utility for viewing Codex and Gemini/Antigravity usage limits in a small floating desktop widget.

## Current behavior

The app uses a compact **floating widget** instead of a system-tray icon or taskbar overlay.

It shows:

- Codex: remaining percentage for the 5-hour and weekly windows
- Gemini: the Antigravity "Gemini Models" weekly remaining quota and reset countdown
- click the provider logo to switch between Codex and Gemini
- the selected provider is remembered between launches

The widget:

- stays on top by default
- can be dragged anywhere with the left mouse button
- remembers its position between launches
- restores itself to a visible screen if monitor/layout settings change
- refreshes Codex usage automatically every 2 minutes
- updates reset countdowns every 30 seconds
- prevents duplicate app instances
- has a right-click menu with:
  - Refresh
  - Always on top
  - Reset position
  - Open log
  - Exit
- does not create a system-tray icon
- does not inject into or modify Windows Explorer

## Requirements

- Windows 10/11
- Codex CLI installed and signed in
- Antigravity CLI (`agy`) installed and signed in for Gemini quota
- .NET 8 SDK for development

Check:

```powershell
codex --version
agy --version
dotnet --version
```

For personal Google accounts, Google retired consumer Google-login through the legacy Gemini CLI in June 2026. Gemini usage is therefore read from Antigravity CLI using its read-only structured usage command.

## Run from source

```powershell
git pull
dotnet run
```

The widget appears near the bottom-right of the active screen the first time it runs. Drag it wherever you prefer; the position is saved locally.

Settings are stored under:

```text
%LOCALAPPDATA%\CodexUsageWidget\settings.json
```

## How it works

For Codex, the app starts the local Codex App Server and reads rate-limit information through:

```text
account/rateLimits/read
```

It recognizes:

- `300` minutes as the 5-hour window
- `10080` minutes as the weekly window

For Gemini, the app runs Antigravity's read-only structured quota command:

```text
agy --print /usage --output-format json
```

It selects the `Gemini Models` weekly quota bucket and reads its `remaining_fraction` and `reset_time`. The command does not start a model turn.

Missing windows or quota buckets are displayed as unavailable rather than estimated.

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

## Design principles

The widget intentionally stays simple:

- no administrator privileges
- no Explorer/taskbar injection
- no browser scraping
- no API key storage
- no background database
- no hidden tray dependency
- persisted settings limited to UI preferences

## Possible future improvements

- start automatically when Windows signs in
- low-usage notifications at 20%, 10%, and 5%
- optional percentage-only compact mode
- optional light theme
- usage history and trend
