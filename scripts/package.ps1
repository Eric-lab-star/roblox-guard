#requires -Version 5.1
<#
.SYNOPSIS
  배포용 ZIP 하나를 만든다. 받는 사람은 이 ZIP 만 있으면 된다(.NET SDK 불필요).
.DESCRIPTION
  build.ps1 로 자체 포함 실행 파일을 발행하고, 디버그 심볼(pdb)을 제거한 뒤,
  더블클릭 런처(Install.cmd/Uninstall.cmd)와 안내문(설치방법.txt)을 넣어
  release\RobloxGuard-v<버전>.zip 으로 압축한다. SHA256 지문도 함께 출력한다.
.PARAMETER SkipBuild
  이미 dist\ 에 발행본이 있으면 다시 빌드하지 않고 그대로 패키징한다.
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild
)
$ErrorActionPreference = "Stop"
$repo    = Split-Path -Parent $PSScriptRoot
$dist    = Join-Path $repo "dist"
$release = Join-Path $repo "release"

# 1) 발행 (별도 요청이 없으면 build.ps1 로 새로 빌드)
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 실패" }
}
if (-not (Test-Path (Join-Path $dist "RobloxGuard.Service.exe"))) {
    throw "dist\ 에 발행본이 없습니다. -SkipBuild 없이 먼저 빌드하세요."
}

# 2) 디버그 심볼 제거(배포본에는 불필요)
Get-ChildItem $dist -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

# 3) 더블클릭 런처 + 안내문 포함
foreach ($f in @("Install.cmd", "Uninstall.cmd", "설치방법.txt")) {
    Copy-Item (Join-Path $PSScriptRoot $f) $dist -Force
}

# 4) 버전 읽기 (Directory.Build.props 의 <Version>)
$props   = Get-Content (Join-Path $repo "Directory.Build.props") -Raw
$version = if ($props -match "<Version>\s*([^<]+?)\s*</Version>") { $Matches[1] } else { "0.0.0" }

# 5) ZIP 생성 (파일 이름의 한글이 깨지지 않도록 UTF-8 플래그를 쓰는 .NET ZipFile 사용)
if (-not (Test-Path $release)) { New-Item -ItemType Directory -Path $release | Out-Null }
$zip = Join-Path $release ("RobloxGuard-v{0}.zip" -f $version)
if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
# 엔트리 이름을 UTF-8 로 기록(bit 11) → Windows 탐색기/7-Zip 모두에서 한글 이름 정상
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $dist, $zip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false,                                   # includeBaseDirectory: 폴더째가 아니라 내용만
    [System.Text.UTF8Encoding]::new($false))  # BOM 없는 UTF-8 로 엔트리 이름 기록

# 6) 결과 + 무결성 지문
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ""
Write-Host "배포 패키지 완성:" -ForegroundColor Green
Write-Host "  파일 : $zip"
Write-Host "  크기 : $size MB"
Write-Host "  SHA256: $hash"
Write-Host ""
Write-Host "받는 사람은 이 ZIP 만 있으면 됩니다. 압축을 풀고 Install.cmd 를 더블클릭하세요." -ForegroundColor Yellow
