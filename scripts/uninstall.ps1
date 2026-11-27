param(
    [switch]$KeepConfig
)

$ErrorActionPreference = "Stop"
$InstallDir = Join-Path $env:LOCALAPPDATA "AntigravityHUD"
$TargetExe = Join-Path $InstallDir "AntigravityHUD.exe"
$ConfigFile = Join-Path $InstallDir "config.json"

$running = Get-Process -Name "AntigravityHUD" -ErrorAction SilentlyContinue
foreach ($p in $running) {
    try {
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            [void]$p.CloseMainWindow()
            [void]$p.WaitForExit(2500)
        }
        if (-not $p.HasExited) {
            $p.Kill()
            [void]$p.WaitForExit(2000)
        }
    } catch {}
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
try {
    $prop = Get-ItemProperty -Path $runKey -Name "AntigravityHUD" -ErrorAction SilentlyContinue
    if ($null -ne $prop) {
        Remove-ItemProperty -Path $runKey -Name "AntigravityHUD"
        Write-Host "Removed Windows login autostart entry."
    }
} catch {}

$lnkPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Antigravity HUD.lnk"
if (Test-Path $lnkPath) {
    Remove-Item $lnkPath
    Write-Host "Removed Start Menu shortcut."
}

if (Test-Path $TargetExe) {
    Remove-Item $TargetExe
    Write-Host "Removed $TargetExe"
}

if (-not $KeepConfig -and (Test-Path $ConfigFile)) {
    Remove-Item $ConfigFile
    Write-Host "Removed $ConfigFile"
}

if ((Test-Path $InstallDir) -and ((Get-ChildItem $InstallDir | Measure-Object).Count -eq 0)) {
    Remove-Item $InstallDir
}

Write-Host "Antigravity HUD uninstalled cleanly."
exit 0
