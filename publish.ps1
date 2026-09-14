$ErrorActionPreference = "Stop"

Write-Host "Publishing Codex Usage Widget..." -ForegroundColor Cyan
Write-Host "Using framework-dependent publish (uses your installed .NET 8 runtime)." -ForegroundColor DarkGray

# Do not use --self-contained or PublishSingleFile here.
# Those modes require additional Windows runtime/ILLink packages from NuGet.
# A normal framework-dependent publish is enough for this PC and avoids that restore dependency.
dotnet publish `
  -c Release `
  --self-contained false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishDir = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\publish"
$exe = Join-Path $publishDir "CodexUsageWidget.exe"

if (Test-Path $exe) {
    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    Write-Host "Published folder:"
    Write-Host $publishDir
    Write-Host ""
    Write-Host "Launch:"
    Write-Host $exe
    Write-Host ""
    Write-Host "Keep the files in this publish folder together. The app uses the .NET 8 runtime already installed on this PC."
} else {
    throw "Publish completed but CodexUsageWidget.exe was not found."
}
