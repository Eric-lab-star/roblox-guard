#requires -Version 7.0
<#
.SYNOPSIS
  RobloxGuard 를 설치한다. dist\ 안에서 관리자 권한으로 실행한다.
.DESCRIPTION
  서비스 등록(LocalSystem, 자동 시작, 실패 시 재시작), 데이터 폴더 ACL 설정,
  부모 비밀번호 설정, 트레이 자동 실행 등록까지 한다.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "관리자 권한이 필요합니다. PowerShell 을 관리자로 실행한 뒤 다시 시도하세요." -ForegroundColor Red
        exit 1
    }
}
Assert-Admin

$ServiceName = "RobloxGuard"
$src         = $PSScriptRoot
$installDir  = Join-Path $env:ProgramFiles "RobloxGuard"
$dataDir     = Join-Path $env:ProgramData "RobloxGuard"
$serviceExe  = Join-Path $installDir "RobloxGuard.Service.exe"
$trayExe     = Join-Path $installDir "RobloxGuard.Tray.exe"

foreach ($f in @("RobloxGuard.Service.exe","RobloxGuard.Tray.exe","RobloxGuard.Lockout.exe")) {
    if (-not (Test-Path (Join-Path $src $f))) { throw "$f 가 없습니다. 먼저 build.ps1 로 발행하고 dist 안에서 실행하세요." }
}

# 1. 기존 서비스 중지 + 트레이/잠금 프로세스 종료
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "기존 서비스를 중지합니다..." -ForegroundColor Cyan
    Stop-Service $ServiceName -ErrorAction SilentlyContinue
}
Get-Process RobloxGuard.Tray,RobloxGuard.Lockout -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 2. 파일 복사
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $src "*.exe") $installDir -Force
Copy-Item (Join-Path $src "*.dll") $installDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $src "*.json") $installDir -Force -ErrorAction SilentlyContinue

# 3. 데이터 폴더 + 제한 ACL (SYSTEM/Administrators 전체, 일반 사용자 접근 없음)
# 선점·정션 공격 방지: 일반 사용자는 ProgramData 에 하위 폴더를 만들 수 있다.
# 미리 만들어 둔 폴더가 정션(reparse point)이거나 신뢰할 수 없는 소유자이면,
# 재귀 삭제(정션을 따라가 엉뚱한 곳을 지울 수 있음) 대신 그 자리에서 옆으로 '이름만' 바꿔 치운다.
$trustedSids = @('S-1-5-18', 'S-1-5-32-544')  # SYSTEM, Administrators
if (Test-Path $dataDir) {
    $item = Get-Item -LiteralPath $dataDir -Force
    $isReparse = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    $ownerSid = $null
    try { $ownerSid = (Get-Acl -LiteralPath $dataDir).GetOwner([Security.Principal.SecurityIdentifier]).Value } catch {}
    if ($isReparse -or ($ownerSid -notin $trustedSids)) {
        $asideName = "RobloxGuard.untrusted-" + (Get-Date -Format "yyyyMMddHHmmss")
        Write-Host "기존 데이터 폴더를 신뢰할 수 없습니다(소유자=$ownerSid, 정션=$isReparse). 옆으로 치웁니다: $asideName" -ForegroundColor Yellow
        Rename-Item -LiteralPath $dataDir -NewName $asideName  # 이름만 바꾸므로 정션 대상을 건드리지 않음
    }
}
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }
# 이제 $dataDir 은 우리 소유의(또는 새로 만든 빈) 폴더다. 재귀(/T) 없이 ACL 을 설정한다.
# OI/CI 상속 플래그로 이후 서비스가 만드는 파일도 같은 권한을 물려받는다.
icacls $dataDir /setowner "*S-1-5-32-544" | Out-Null
icacls $dataDir /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
Write-Host "데이터 폴더 권한 설정: $dataDir" -ForegroundColor Cyan

# 4. 비밀번호 설정
$configPath = Join-Path $dataDir "config.json"
$needPassword = $true
if (Test-Path $configPath) {
    $answer = Read-Host "이미 설치되어 있습니다. 비밀번호를 재설정하시겠습니까? (y/N)"
    $needPassword = ($answer -eq "y" -or $answer -eq "Y")
}
if ($needPassword) {
    while ($true) {
        $p1 = Read-Host "부모 비밀번호 (4자 이상)" -AsSecureString
        $p2 = Read-Host "비밀번호 확인" -AsSecureString
        $s1 = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p1))
        $s2 = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p2))
        if ($s1 -ne $s2)      { Write-Host "일치하지 않습니다. 다시." -ForegroundColor Yellow; continue }
        if ($s1.Length -lt 4) { Write-Host "4자 이상이어야 합니다. 다시." -ForegroundColor Yellow; continue }
        $s1 | & $serviceExe set-password
        if ($LASTEXITCODE -ne 0) { Write-Host "비밀번호 저장 실패. 다시." -ForegroundColor Yellow; continue }
        break
    }
}

# 5. 서비스 등록 + 복구 옵션
if (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) {
    Write-Host "서비스를 등록합니다..." -ForegroundColor Cyan
    & sc.exe create $ServiceName binPath= "`"$serviceExe`"" start= auto obj= LocalSystem DisplayName= "RobloxGuard" | Out-Null
    & sc.exe description $ServiceName "로블록스 차단 서비스" | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/1000/restart/1000/restart/1000 | Out-Null
}
Start-Service $ServiceName
Write-Host "서비스 시작됨: $ServiceName" -ForegroundColor Green

# 6. 트레이 자동 실행(모든 사용자) + 현재 세션에서 실행
$runKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runKey -Name "RobloxGuard.Tray" -Value "`"$trayExe`""
Start-Process $trayExe -ErrorAction SilentlyContinue

# 7. 안내
Write-Host ""
Write-Host "설치 완료." -ForegroundColor Green
Write-Host "중요: 아이 계정이 '표준 사용자' 여야 합니다. 관리자면 이 프로그램을 지울 수 있습니다." -ForegroundColor Yellow
Write-Host "  설정 > 계정 > 가족 및 다른 사용자 > (아이 계정) > 계정 유형 변경 > 표준 사용자" -ForegroundColor Yellow
Write-Host ""
Write-Host "현재 이 PC 의 관리자 계정:" -ForegroundColor Cyan
Get-LocalGroupMember Administrators -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
