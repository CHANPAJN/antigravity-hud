param(
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir = Join-Path $RepoRoot "src"
if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $DistDir = Join-Path $RepoRoot "dist"
    if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }
    $OutFile = Join-Path $DistDir "AntigravityHUD.exe"
} else {
    $OutDir = Split-Path -Parent $OutFile
    if (-not [string]::IsNullOrWhiteSpace($OutDir) -and -not (Test-Path $OutDir)) {
        New-Item -ItemType Directory -Path $OutDir | Out-Null
    }
}

$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    Write-Error "Built-in .NET Framework 4.x compiler (csc.exe) was not found. Windows 10/11 is required."
    exit 1
}

$srcFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $srcFiles -or $srcFiles.Count -eq 0) {
    Write-Error "No C# source files found in $SrcDir"
    exit 1
}

$compileArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/utf8output",
    "/out:$OutFile",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll"
) + $srcFiles

Write-Host "Compiling AntigravityHUD ($($srcFiles.Count) source files) using $csc ..."
& $csc @compileArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$fi = Get-Item $OutFile
Write-Host ("Built successfully: {0} ({1:N0} bytes)" -f $fi.FullName, $fi.Length)
exit 0
