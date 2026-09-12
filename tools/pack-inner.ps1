# 打 W3 内测直装目录：Release 自包含，未签名。
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

taskkill /F /IM ChanJing.App.exe 2>$null | Out-Null

dotnet test "$root\tests\ChanJing.Tests\ChanJing.Tests.csproj" --nologo
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

$out = Join-Path $root "dist\ZenFocus-inner"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

dotnet publish "$root\src\ChanJing.App\ChanJing.App.csproj" `
    -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Copy-Item (Join-Path $PSScriptRoot "inner-readme.txt") (Join-Path $out "READ_ME.txt") -Force

$exe = Join-Path $out "ChanJing.App.exe"
if (-not (Test-Path $exe)) { throw "missing $exe" }

Write-Host "INNER_OK $exe"
