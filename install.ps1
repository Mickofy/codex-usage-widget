$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$publishDir = Join-Path $projectRoot "bin\Release\net8.0-windows\publish"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\CodexUsageWidget"
$installedExe = Join-Path $installDir "CodexUsageWidget.exe"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex Usage Widget.lnk"
$startupShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\Codex Usage Widget.lnk"

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = Split-Path -Parent $Target
    $shortcut.Description = "Codex usage floating widget"
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Save()
}

Write-Host "Installing Codex Usage Widget..." -ForegroundColor Cyan

# Stop an existing copy so its files can be replaced safely.
$runningWidget = Get-Process -Name "CodexUsageWidget" -ErrorAction SilentlyContinue
if ($runningWidget) {
    $runningWidget | Stop-Process -Force -ErrorAction SilentlyContinue

    # Windows may keep the executable/folder locked briefly after the process
    # exits. Wait for each process before replacing the installation directory.
    foreach ($process in $runningWidget) {
        try {
            Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
        }
        catch {
            # Continue to the folder-removal retry below.
        }
    }

    Start-Sleep -Milliseconds 300
}

# Build the normal Windows GUI executable using the project's existing publish flow.
& (Join-Path $projectRoot "publish.ps1")

if (-not (Test-Path (Join-Path $publishDir "CodexUsageWidget.exe"))) {
    throw "Published CodexUsageWidget.exe was not found."
}

if (Test-Path $installDir) {
    $removed = $false

    for ($attempt = 1; $attempt -le 5 -and -not $removed; $attempt++) {
        try {
            Remove-Item $installDir -Recurse -Force
            $removed = $true
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }

            Start-Sleep -Milliseconds 500
        }
    }
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "*") $installDir -Recurse -Force

New-AppShortcut -Path $startMenuShortcut -Target $installedExe
New-AppShortcut -Path $startupShortcut -Target $installedExe

Write-Host ""
Write-Host "Installed successfully." -ForegroundColor Green
Write-Host "App: $installedExe"
Write-Host "Start menu shortcut: Codex Usage Widget"
Write-Host "Start with Windows: enabled"
Write-Host ""
Write-Host "Launching widget..."

Start-Process -FilePath $installedExe -WorkingDirectory $installDir
