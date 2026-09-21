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

# Build first so the running widget stays available until replacement files are ready.
& (Join-Path $projectRoot "publish.ps1")

if (-not (Test-Path (Join-Path $publishDir "CodexUsageWidget.exe"))) {
    throw "Published CodexUsageWidget.exe was not found."
}

function Get-InstalledWidgetProcesses {
    $matches = @()

    $matches += Get-Process -Name "CodexUsageWidget" -ErrorAction SilentlyContinue

    try {
        $matches += Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object {
                ($_.ExecutablePath -and
                    $_.ExecutablePath.StartsWith(
                        $installDir,
                        [System.StringComparison]::OrdinalIgnoreCase)) -or
                ($_.CommandLine -and
                    $_.CommandLine.IndexOf(
                        $installDir,
                        [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
            }
    }
    catch {
        # CIM lookup is only a fallback for hosted/framework-dependent copies.
    }

    $ids = @{}
    foreach ($process in $matches) {
        $id = if ($process.PSObject.Properties["Id"]) {
            $process.Id
        }
        elseif ($process.PSObject.Properties["ProcessId"]) {
            $process.ProcessId
        }
        else {
            $null
        }

        if ($id -and $id -ne $PID) {
            $ids[[int]$id] = $true
        }
    }

    return @($ids.Keys)
}

# Stop every copy tied to the installed widget folder. This also catches a
# framework-dependent copy if Windows hosts it under another process name.
$processIds = Get-InstalledWidgetProcesses

foreach ($processId in $processIds) {
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

# taskkill also terminates any child process tree owned by the apphost.
& taskkill.exe /F /T /IM CodexUsageWidget.exe 2>$null | Out-Null

for ($attempt = 1; $attempt -le 20; $attempt++) {
    $remaining = Get-InstalledWidgetProcesses
    if ($remaining.Count -eq 0) {
        break
    }

    foreach ($processId in $remaining) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 250
}

# Give Windows Defender / shell bookkeeping a brief moment to release handles.
Start-Sleep -Milliseconds 500

if (Test-Path $installDir) {
    $removed = $false

    for ($attempt = 1; $attempt -le 12 -and -not $removed; $attempt++) {
        try {
            Remove-Item $installDir -Recurse -Force
            $removed = $true
        }
        catch {
            if ($attempt -eq 12) {
                $remaining = Get-InstalledWidgetProcesses
                if ($remaining.Count -gt 0) {
                    throw "Could not replace the installed widget because process ID(s) $($remaining -join ', ') are still using it."
                }

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
