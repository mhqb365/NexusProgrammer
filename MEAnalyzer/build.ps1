param(
    [string]$Python = "py -3",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$toolRoot = $PSScriptRoot
$scriptPath = Join-Path $toolRoot "MEA.py"
$requirementsPath = Join-Path $toolRoot "requirements.txt"
$buildPath = Join-Path $toolRoot "build"
$specPath = Join-Path $toolRoot "MEA.spec"
$exePath = Join-Path $toolRoot "MEA.exe"

if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "MEA.py was not found in $toolRoot"
}

if (-not (Test-Path -LiteralPath $requirementsPath)) {
    throw "requirements.txt was not found in $toolRoot"
}

$pythonParts = $Python -split '\s+', 2
$pythonExe = $pythonParts[0]
$pythonArgs = @()
if ($pythonParts.Length -gt 1 -and $pythonParts[1].Length -gt 0) {
    $pythonArgs += $pythonParts[1]
}

if (-not $SkipInstall) {
    & $pythonExe @pythonArgs -m pip install -r $requirementsPath
    if ($LASTEXITCODE -ne 0) {
        throw "pip install failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path -LiteralPath $buildPath) {
    [System.IO.Directory]::Delete((Resolve-Path -LiteralPath $buildPath).Path, $true)
}

if (Test-Path -LiteralPath $specPath) {
    [System.IO.File]::Delete((Resolve-Path -LiteralPath $specPath).Path)
}

& $pythonExe @pythonArgs -m PyInstaller `
    --onefile `
    --clean `
    --name MEA `
    --distpath $toolRoot `
    --workpath $buildPath `
    --specpath $toolRoot `
    --add-data "MEA.dat;." `
    --add-data "Huffman.dat;." `
    --add-data "FileTable.dat;." `
    $scriptPath

if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $buildPath) {
    [System.IO.Directory]::Delete((Resolve-Path -LiteralPath $buildPath).Path, $true)
}

if (Test-Path -LiteralPath $specPath) {
    [System.IO.File]::Delete((Resolve-Path -LiteralPath $specPath).Path)
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "MEA.exe was not created."
}

Write-Host "Built $exePath"
