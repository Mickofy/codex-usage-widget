$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "Programs\CodexUsageWidget"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex Usage Widget.lnk"
$startupShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\Codex Usage Widget.lnk"

Write-Host "Uninstalling Codex Usage Widget..." -ForegroundColor Cyan

Get-Process -Name "CodexUsageWidget" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($shortcut in @($startMenuShortcut, $startupShortcut)) {
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force
    }
}

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}

Write-Host ""
Write-Host "Uninstalled successfully." -ForegroundColor Green
Write-Host "Saved widget preferences/logs were left untouched."
