$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$publishDir = Join-Path $projectRoot "bin\Release\net8.0-windows\publish"

# Use immutable/versioned release folders so a stale Windows file handle on an
# older installed copy can never block an update.
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexUsageWidget"
$releasesRoot = Join-Path $installRoot "releases"
$releaseId = Get-Date -Format "yyyyMMdd-HHmmssfff"
$installDir = Join-Path $releasesRoot $releaseId
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

function Get-InstalledWidgetProcesses {
    $matches = @()

    $matches += Get-Process -Name "CodexUsageWidget" -ErrorAction SilentlyContinue

    try {
        $matches += Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object {
                ($_.ExecutablePath -and
                    $_.ExecutablePath.StartsWith(
                        $installRoot,
                        [System.StringComparison]::OrdinalIgnoreCase)) -or
                ($_.CommandLine -and
                    $_.CommandLine.IndexOf(
                        $installRoot,
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

Write-Host "Installing Codex Usage Widget..." -ForegroundColor Cyan

# Build first so a currently-running widget remains available until the new
# release is ready.
& (Join-Path $projectRoot "publish.ps1")

if (-not (Test-Path (Join-Path $publishDir "CodexUsageWidget.exe"))) {
    throw "Published CodexUsageWidget.exe was not found."
}

# Best-effort stop of an existing widget. Even if some unrelated Windows
# process still holds a handle to the old install folder, the new version goes
# into a different folder and installation can continue safely.
$processIds = Get-InstalledWidgetProcesses

foreach ($processId in $processIds) {
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

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

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "*") $installDir -Recurse -Force

New-AppShortcut -Path $startMenuShortcut -Target $installedExe
New-AppShortcut -Path $startupShortcut -Target $installedExe

# Remove old versioned releases only when Windows allows it. A locked legacy or
# previous release is harmless because the shortcuts now point to this release.
if (Test-Path $releasesRoot) {
    Get-ChildItem $releasesRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $installDir } |
        ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
}

# Best-effort cleanup of files from the original non-versioned installer.
# Never fail an update if Windows still has one of these legacy files locked.
foreach ($legacyName in @(
    "CodexUsageWidget.exe",
    "CodexUsageWidget.dll",
    "CodexUsageWidget.deps.json",
    "CodexUsageWidget.runtimeconfig.json",
    "Assets"
)) {
    $legacyPath = Join-Path $installRoot $legacyName
    if (Test-Path $legacyPath) {
        Remove-Item $legacyPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Installed successfully." -ForegroundColor Green
Write-Host "App: $installedExe"
Write-Host "Start menu shortcut: Codex Usage Widget"
Write-Host "Start with Windows: enabled"
Write-Host ""
Write-Host "Launching widget..."

Start-Process -FilePath $installedExe -WorkingDirectory $installDir
