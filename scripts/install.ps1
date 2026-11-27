param(
    [switch]$Autostart,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$InstallDir = Join-Path $env:LOCALAPPDATA "AntigravityHUD"
$TargetExe = Join-Path $InstallDir "AntigravityHUD.exe"

# Gracefully close existing instance if running (never force-kill unrelated processes)
$running = Get-Process -Name "AntigravityHUD" -ErrorAction SilentlyContinue
foreach ($p in $running) {
    try {
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            [void]$p.CloseMainWindow()
        }
        [void]$p.WaitForExit(2500)
    } catch {}
}

$buildScript = Join-Path $PSScriptRoot "build.ps1"
& $buildScript -OutFile $TargetExe
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Autostart) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $runKey -Name "AntigravityHUD" -Value "`"$TargetExe`""
    Write-Host "Enabled Windows login autostart (HKCU Run)."
}

# Create Start Menu shortcut
try {
    $startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $lnkPath = Join-Path $startMenu "Antigravity HUD.lnk"
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($lnkPath)
    $sc.TargetPath = $TargetExe
    $sc.WorkingDirectory = $InstallDir
    $sc.Description = "Antigravity HUD - Windows Taskbar Token Telemetry"
    $sc.Save()
    Write-Host "Created Start Menu shortcut: $lnkPath"
} catch {
    Write-Warning "Skipped Start Menu shortcut creation: $($_.Exception.Message)"
}

if (-not $NoLaunch) {
    Start-Process -FilePath $TargetExe -WorkingDirectory $InstallDir
    Write-Host "Launched AntigravityHUD.exe"
}
exit 0
