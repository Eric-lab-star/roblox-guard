#requires -Version 5.1
<#
.SYNOPSIS
  RobloxGuard 를 제거한다. 관리자 권한으로 실행한다.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = "Continue"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "관리자 권한이 필요합니다." -ForegroundColor Red
        exit 1
    }
}
Assert-Admin

$ServiceName = "RobloxGuard"
$installDir  = Join-Path $env:ProgramFiles "RobloxGuard"
$dataDir     = Join-Path $env:ProgramData "RobloxGuard"
$hostsPath   = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"

# 1. 서비스 중지·삭제
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service $ServiceName -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "서비스 삭제됨" -ForegroundColor Cyan
}

# 2. 트레이·잠금 프로세스 종료
Get-Process RobloxGuard.Tray,RobloxGuard.Lockout -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 3. hosts 마커 블록 제거
if (Test-Path $hostsPath) {
    $text = Get-Content $hostsPath -Raw
    $cleaned = [System.Text.RegularExpressions.Regex]::Replace(
        $text, "(\r?\n)?# RobloxGuard BEGIN.*?# RobloxGuard END(\r?\n)?", "",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($cleaned -ne $text) {
        Set-Content -Path $hostsPath -Value $cleaned -NoNewline -Encoding utf8
        & ipconfig /flushdns | Out-Null
        Write-Host "hosts 차단 블록 제거됨" -ForegroundColor Cyan
    }
}

# 4. 방화벽 규칙 제거
Get-NetFirewallRule -DisplayName "RobloxGuard-*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
# 규칙 이름이 DisplayName 과 같게 등록되므로 이름으로도 한 번 더
$rules = (& netsh advfirewall firewall show rule name=all) 2>$null | Select-String "RobloxGuard-" | ForEach-Object { ($_ -split ":",2)[1].Trim() } | Select-Object -Unique
foreach ($r in $rules) { & netsh advfirewall firewall delete rule name="$r" | Out-Null }

# 5. 자동 실행 등록 제거
Remove-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "RobloxGuard.Tray" -ErrorAction SilentlyContinue

# 6. 파일 삭제
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue }

# 7. 기록 삭제 여부
# 정션(재분석 지점) 공격 방지: 일반 사용자가 데이터 폴더나 그 하위를 정션으로
# 바꿔치기해 두면, 관리자 권한의 재귀 삭제(Remove-Item -Recurse)가 정션을 따라가
# 엉뚱한 곳을 지울 수 있다. 폴더 자신과 하위에 정션/링크가 없고 소유자가 신뢰
# 대상(SYSTEM/Administrators)일 때만 삭제한다. 아니면 보존하고 수동 확인을 권한다.
if (Test-Path $dataDir) {
    $trustedSids = @('S-1-5-18', 'S-1-5-32-544')  # SYSTEM, Administrators
    $safe = $true
    $it = Get-Item -LiteralPath $dataDir -Force
    if (($it.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "데이터 폴더가 정션/재분석 지점입니다. 안전을 위해 삭제하지 않습니다: $dataDir"
        $safe = $false
    }
    if ($safe) {
        $owner = $null
        try { $owner = (Get-Acl -LiteralPath $dataDir).GetOwner([Security.Principal.SecurityIdentifier]).Value } catch {}
        if ($owner -notin $trustedSids) {
            Write-Warning "데이터 폴더 소유자를 신뢰할 수 없습니다($owner). 안전을 위해 삭제하지 않습니다: $dataDir"
            $safe = $false
        }
    }
    if ($safe -and (Get-ChildItem -LiteralPath $dataDir -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue)) {
        Write-Warning "데이터 폴더 하위에 정션/링크가 있습니다. 안전을 위해 삭제하지 않습니다: $dataDir"
        $safe = $false
    }
    if (-not $safe) {
        Write-Host "데이터 보존: $dataDir (수동 확인 필요)" -ForegroundColor Yellow
    } elseif ((Read-Host "기록(attempts.log)과 설정을 삭제할까요? (y/N)") -in @("y","Y")) {
        Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "데이터 삭제됨" -ForegroundColor Cyan
    } else {
        Write-Host "데이터 보존: $dataDir" -ForegroundColor Cyan
    }
}

Write-Host "제거 완료." -ForegroundColor Green
