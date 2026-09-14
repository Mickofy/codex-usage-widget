$ErrorActionPreference = "Stop"

Write-Host "Publishing Codex Usage Widget..." -ForegroundColor Cyan

dotnet publish `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true

$exe = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\win-x64\publish\CodexUsageWidget.exe"

if (Test-Path $exe) {
    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    Write-Host "EXE:"
    Write-Host $exe
    Write-Host ""
    Write-Host "You can create a shortcut to this EXE and place it in Startup later."
} else {
    throw "Publish completed but EXE was not found."
}
