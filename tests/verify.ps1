$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("antigravity_hud_verify_" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    $exePath = Join-Path $TempDir "AntigravityHUD.exe"
    & (Join-Path $RepoRoot "scripts\build.ps1") -OutFile $exePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Running config persistence self-test..."
    $pCfg = Start-Process -FilePath $exePath -ArgumentList "--test-config" -Wait -PassThru -NoNewWindow
    if ($pCfg.ExitCode -ne 0) {
        Write-Error "Config self-test failed with code $($pCfg.ExitCode)"
        exit 1
    }

    $cases = @(
        @{ Name = "bar_en.png"; Args = @("bar", "--lang", "en", "--demo", "--theme", "light") },
        @{ Name = "bar_zh.png"; Args = @("bar", "--lang", "zh", "--demo", "--theme", "light") },
        @{ Name = "bar_dark_en.png"; Args = @("bar", "--lang", "en", "--demo", "--theme", "dark") },
        @{ Name = "hud_en.png"; Args = @("hud", "--lang", "en", "--demo", "--theme", "light") },
        @{ Name = "hud_zh.png"; Args = @("hud", "--lang", "zh", "--demo", "--theme", "light") },
        @{ Name = "hud_dark_en.png"; Args = @("hud", "--lang", "en", "--demo", "--theme", "dark") },
        @{ Name = "ledger_en.png"; Args = @("ledger", "--lang", "en", "--demo", "--theme", "light") },
        @{ Name = "ledger_zh.png"; Args = @("ledger", "--lang", "zh", "--demo", "--theme", "light") },
        @{ Name = "ledger_dark_zh.png"; Args = @("ledger", "--lang", "zh", "--demo", "--theme", "dark") },
        @{ Name = "drill_en.png"; Args = @("drill", "--lang", "en", "--demo", "--theme", "light") },
        @{ Name = "menu_en.png"; Args = @("menu", "--lang", "en", "--theme", "light") },
        @{ Name = "menu_zh.png"; Args = @("menu", "--lang", "zh", "--theme", "light") },
        @{ Name = "menu_dark_zh.png"; Args = @("menu", "--lang", "zh", "--theme", "dark") }
    )

    foreach ($c in $cases) {
        $png = Join-Path $TempDir $c.Name
        $argList = @("--render-preview", $png) + $c.Args
        $proc = Start-Process -FilePath $exePath -ArgumentList $argList -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -ne 0 -or -not (Test-Path $png)) {
            Write-Error "Offscreen preview failed for $($c.Name) (exit=$($proc.ExitCode))"
            exit 1
        }
        $sz = (Get-Item $png).Length
        if ($sz -lt 1500) {
            Write-Error "Rendered PNG $($c.Name) unexpectedly small ($sz bytes)"
            exit 1
        }
        Write-Host ("PASS {0} ({1:N0} bytes)" -f $c.Name, $sz)
    }

    Write-Host "All Antigravity HUD verification checks passed."
} finally {
    if (Test-Path $TempDir) {
        Remove-Item -Path $TempDir -Recurse -ErrorAction SilentlyContinue
    }
}
exit 0
