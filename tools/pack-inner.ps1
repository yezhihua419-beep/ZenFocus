# Unpackaged Release folder. Needs license.secret at repo root.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$secretFile = Join-Path $root "license.secret"
if (-not (Test-Path $secretFile) -and -not $env:CHANJING_LICENSE_SECRET) {
    throw "Release needs license.secret at repo root (see license.secret.example)"
}

taskkill /F /IM ChanJing.App.exe 2>$null | Out-Null

dotnet test (Join-Path $root "tests\ChanJing.Tests\ChanJing.Tests.csproj") --nologo
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

$out = Join-Path $root "dist\ZenFocus-inner"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

dotnet publish (Join-Path $root "src\ChanJing.App\ChanJing.App.csproj") `
    -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Copy-Item (Join-Path $PSScriptRoot "inner-readme.txt") (Join-Path $out "READ_ME.txt") -Force

$exe = Join-Path $out "ChanJing.App.exe"
if (-not (Test-Path $exe)) { throw "missing $exe" }

Write-Host "INNER_OK $exe"
