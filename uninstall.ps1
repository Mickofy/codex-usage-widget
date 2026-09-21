$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexUsageWidget"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex Usage Widget.lnk"
$startupShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\Codex Usage Widget.lnk"

Write-Host "Uninstalling Codex Usage Widget..." -ForegroundColor Cyan

# Stop both normal apphost copies and any framework-dependent process launched
# from the CodexUsageWidget installation tree.
Get-Process -Name "CodexUsageWidget" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

try {
    Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object {
            ($_.ExecutablePath -and
                $_.ExecutablePath.StartsWith(
                    $installRoot,
                    [System.StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and
                $_.CommandLine.IndexOf(
                    $installRoot,
                    [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
        } |
        ForEach-Object {
            if ($_.ProcessId -ne $PID) {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
}
catch {
    # Best-effort fallback only.
}

Start-Sleep -Milliseconds 500

foreach ($shortcut in @($startMenuShortcut, $startupShortcut)) {
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force
    }
}

if (Test-Path $installRoot) {
    try {
        Remove-Item $installRoot -Recurse -Force
    }
    catch {
        Write-Warning "Some old installed files are still locked by Windows and could not be removed. The app shortcuts and auto-start entry were removed, so the widget is uninstalled. You can delete '$installRoot' after the locking process releases it or after a restart."
    }
}

Write-Host ""
Write-Host "Uninstalled successfully." -ForegroundColor Green
Write-Host "Saved widget preferences/logs were left untouched."
