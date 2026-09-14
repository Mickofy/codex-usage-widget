# Codex Usage Widget — V3

This is the first daily-use version of a lightweight Windows system-tray widget for viewing Codex usage limits.

## Behavior

- Starts silently in the Windows notification area.
- Tray icon shows your **weekly remaining percentage**.
- Hover shows both:
  - 5-hour remaining %
  - weekly remaining %
- Left-click tray icon:
  - opens the popup near the bottom-right of your screen
  - left-click again hides it
- Clicking away hides the popup.
- Auto-refreshes every 2 minutes.
- Reset countdown updates every 30 seconds.
- Right-click menu:
  - Open
  - Refresh
  - Open log
  - Exit

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
dotnet run
```

There will be no console window and no popup at startup.

Look in the Windows notification area near the clock. Windows may initially put the icon behind the `^` overflow arrow.

Left-click the icon to open the detailed usage popup.

## How it works

The app starts the local Codex App Server and reads the account rate-limit windows through `account/rateLimits/read`.

It currently recognizes:

- `300` minutes as the 5-hour window
- `10080` minutes as the weekly window

Missing windows are shown as unavailable rather than estimated.

## Make a standalone EXE

Run:

```powershell
.\publish.ps1
```

Or manually:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Your standalone EXE will be created under:

```text
bin\Release\net8.0-windows\win-x64\publish\CodexUsageWidget.exe
```

You can then run `CodexUsageWidget.exe` directly without opening PowerShell.

## Logs

Diagnostic logs are written locally to:

```text
%LOCALAPPDATA%\CodexUsageWidget\app.log
```

You can also right-click the tray icon and choose **Open log**.

## Windows taskbar note

This is a **notification-area / system-tray app**, not a program that modifies the Windows 11 taskbar shell itself.

That is intentional because the notification area is the supported and maintainable Windows location for this kind of background utility.

## Planned improvements

- start automatically when Windows signs in
- warning notifications at 20%, 10%, and 5%
- choose whether the tray icon shows weekly or 5-hour remaining %
- compact dark popup
- usage history and trend
