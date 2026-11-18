# Codex Usage Widget

> **Unofficial community project.** Not affiliated with or endorsed by OpenAI. This software depends on locally installed Codex CLI interfaces that may change between Codex versions.

A lightweight Windows utility for viewing your real Codex usage limits in a small floating desktop widget.

## Current behavior

The app uses a compact **floating widget** instead of a system-tray icon or taskbar overlay.

It shows:

- `5H` — remaining percentage in the 5-hour window
- `WEEK` — remaining percentage in the weekly window
- reset countdowns
- a small status dot for live / refreshing / error state

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
- .NET 8 SDK for development

Check:

```powershell
codex --version
dotnet --version
```

## Download for Windows

**Portable build:** Download the **CodexUsageWidget-Windows-x64** artifact from a successful `main` run of [Windows build](https://github.com/Mickofy/codex-usage-widget/actions/workflows/windows-build.yml). Extract **the entire artifact folder**, not only its EXE, and launch `CodexUsageWidget.exe`.

- Requires **Windows 10/11 x64**, an installed and authenticated **Codex CLI**, and the **.NET 8 Desktop Runtime**. The CI build is framework-dependent; it does not bundle the .NET runtime.
- The portable download does not require the .NET SDK or running a PowerShell install script.
- The downloadable app is currently **unsigned** and has no automatic updater. Verify the GitHub source and avoid bypassing Windows security protections.
- `Install.cmd` and `install.ps1` are alternative **source-based** installers and create Start Menu **and auto-start** shortcuts. Read them before running. `uninstall.ps1` removes their installed copy and shortcuts.
- Build validation includes a Windows compile/publish and a deterministic, offline model smoke test; it does **not** confirm that every Codex CLI release supports the app-server endpoint.

## Privacy and portability

The app calls the Codex CLI found on the **current user's PATH**, uses that user's existing Codex login, and writes its own settings/logs under `%LOCALAPPDATA%\\CodexUsageWidget`. It does not use the author's personal Codex account, account credentials, or home folder.

Logs can contain error messages from the local Codex process; inspect and redact them before opening public issues. The app is intended for local use, not as a service to collect others' Codex authentication information.

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

## Open source

Licensed under the [MIT License](LICENSE). Contributions are welcome. Do not upload session transcripts, credentials, login files, or personal logs to public issues.
