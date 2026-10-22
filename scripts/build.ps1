#requires -Version 7.0
<#
.SYNOPSIS
  세 실행 파일을 자체 포함(.NET 불필요)으로 dist\ 에 발행하고 스크립트·README 를 복사한다.
  관리자 권한이 필요 없다.
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo "dist"

Write-Host "== 테스트 실행 ==" -ForegroundColor Cyan
dotnet test (Join-Path $repo "tests\RobloxGuard.Core.Tests")
if ($LASTEXITCODE -ne 0) { throw "테스트 실패 — 빌드를 중단합니다." }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

$projects = @(
    "src\RobloxGuard.Service\RobloxGuard.Service.csproj",
    "src\RobloxGuard.Tray\RobloxGuard.Tray.csproj",
    "src\RobloxGuard.Lockout\RobloxGuard.Lockout.csproj"
)
foreach ($proj in $projects) {
    Write-Host "== 발행: $proj ==" -ForegroundColor Cyan
    dotnet publish (Join-Path $repo $proj) -c $Configuration -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $dist
    if ($LASTEXITCODE -ne 0) { throw "발행 실패: $proj" }
}

# 발행 부산물 중 exe 만 남기고 pdb 등 정리는 생략(디버그에 유용). 스크립트·문서 복사.
Copy-Item (Join-Path $PSScriptRoot "install.ps1")   $dist -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") $dist -Force
$readme = Join-Path $repo "README.md"
if (Test-Path $readme) { Copy-Item $readme $dist -Force }

Write-Host ""
Write-Host "완료: $dist" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
Write-Host "설치: 관리자 PowerShell 에서  cd `"$dist`"; .\install.ps1" -ForegroundColor Yellow
