# RobloxGuard 설계 문서

작성일: 2026-10-22
상태: 승인됨 (2026-10-22)

## 1. 의도

가정용 Windows PC에서 중학생 자녀가 부모 몰래 로블록스를 실행하는 것을
사람의 개입 없이 자동으로 막는다. 부모가 비밀번호를 넣고 풀어주기 전까지는
무조건 차단이며, 풀어준 시간이 끝나면 자동으로 다시 차단된다.

핵심은 "부모가 지켜보지 않아도 된다"는 것이다. 프로그램은 PC가 켜질 때부터
조용히 돌고, 아이가 끄거나 지울 수 없어야 하며, 부모는 트레이 아이콘 하나로
풀어주고 기록을 확인한다.

## 2. 사용자가 결정한 것

- 대상: Windows PC 한 대. 아이는 **일반 사용자 계정**(관리자 아님)을 쓴다.
  부모는 관리자 계정을 가진다. 이 전제가 깨지면 소프트웨어만으로는 막을 수 없다.
- 정책: 스케줄 없음. **부모가 풀어주기 전까지 무조건 차단.**
- 풀어주는 방식: **트레이 아이콘 → 비밀번호 입력 → 허용 시간 선택.**
  시간이 끝나면 자동으로 차단 모드 복귀.
- 아이 쪽 경험: 로블록스가 감지되면 **즉시 종료**하고, 그 세션의
  **키보드와 마우스를 15초 동안 막는다.** 화면 전체를 덮는 잠금 화면에
  안내 문구와 카운트다운을 보여준다. 이 잠금 화면이 안내 창 역할을 한다.
- Roblox Studio도 막는다 (설정으로 끌 수 없음).
- 브라우저 클라우드 게임(now.gg 등)은 알려진 도메인을 hosts 차단 목록에
  넣는 수준으로 막는다 (best effort).
- 기록: **시도 기록**을 남기고 부모가 볼 수 있다.
- 비밀번호는 아이 세션에서도 입력할 수 있게 둔다. 대신 README와 입력 창에
  "가급적 부모 계정으로 로그인해서 입력하세요"를 안내한다.

## 3. 목표 / 비목표 / 반목표

### 목표
1. 이 PC에서 로블록스 플레이어가 실행되면 1초 이내에 종료된다.
2. 아이는 일반 계정 권한으로 이 프로그램을 끄거나 지우거나 우회할 수 없다.
3. 부모는 트레이에서 30초 안에 풀어줄 수 있다.
4. 허용 시간이 끝나면 아무도 손대지 않아도 다시 차단된다.
5. 부모가 "언제 몇 번 시도했는지"를 볼 수 있다.
6. 로블록스가 감지된 세션은 15초 동안 키보드·마우스를 쓸 수 없고, 잠금 화면이
   이유를 알려준다. 15초가 지나면 반드시 풀린다.

### 비목표 (이번 버전에서 하지 않음)
- 요일/시간대별 자동 허용 스케줄.
- 폰이나 원격에서 풀어주기.
- 로블록스 외 다른 게임이나 사이트 차단.
- Mac, Linux, 모바일, 다른 PC.
- 브라우저 확장 프로그램.
- 가상 머신 안에서 실행되는 로블록스 차단.
- 사용 시간 통계, 주간 리포트 등 분석 기능.

### 반목표 (이렇게 되면 실패)
- **부모가 못 푸는 상황.** 서비스가 죽거나 버그가 나도 부모는 관리자 권한으로
  언제든 서비스를 멈추고 지울 수 있어야 한다. 비밀번호를 잊으면 관리자 권한으로
  재설치하면 풀린다. 어떤 경우에도 PC 자체를 잠그지 않는다.
- **엉뚱한 프로그램을 죽이는 것.** 이름에 "roblox"가 들어간 문서 뷰어 같은 것을
  죽이면 안 된다. 판정은 실행 파일의 버전 정보(회사명) 기준을 포함한다.
- **잠금이 안 풀리는 상황.** 15초 잠금이 버그로 영원히 이어지면 PC를 못 쓴다.
  안전장치를 세 겹 둔다: 잠금 프로그램 자체의 타이머, 프로세스가 죽으면
  Windows가 `BlockInput` 을 자동 해제하는 성질, 20초 뒤 서비스의 강제 종료.
  잠금은 로블록스가 돌던 세션에만 건다. 부모가 다른 세션에 로그인해 있으면
  영향 없다.
- **PC 네트워크를 망가뜨리는 것.** hosts 파일을 건드릴 때 우리가 쓴 구간만
  건드리고, 원자적으로 쓰고, 삭제 시 깨끗이 복원한다.
- **아이에게 모욕적인 UX.** 안내 창은 짧고 중립적이어야 한다.
  "지금은 로블록스를 사용할 수 없어요. 부모님께 말씀해 주세요." 수준.

## 4. 제약

- OS: Windows 10 21H2 이상 / Windows 11. x64만.
- 개발 PC에 .NET SDK가 없다. `winget install Microsoft.DotNet.SDK.8` 로 설치한다.
- 아이 계정은 일반 사용자. 부모 계정은 관리자.
- 아이가 쓸 수 있는 우회 수단 중 다음은 **막는다**: 서비스 프로세스 종료,
  프로그램 폴더 삭제, 로블록스 실행 파일 이름 변경, 비밀번호 무차별 추측,
  시스템 시계 조작(일반 계정은 불가).
- 아이가 트레이 앱을 종료하는 것은 **막지 않는다**. 트레이 앱은 아이 권한으로
  돌기 때문에 막을 수 없고, 막을 필요도 없다. 트레이가 죽어도 차단은 서비스가
  유지하고, 안내 창은 `WTSSendMessage` 로 대신 띄운다. 트레이를 다시 살리는
  감시 코드는 넣지 않는다. **(내 결정)**
- 차단은 **PC 전체**에 걸린다. 부모 계정에서 로블록스를 켜도 종료되고,
  차단 모드에서는 부모도 roblox.com 에 들어갈 수 없다. 계정별로 나누면
  판정 로직과 hosts 처리가 복잡해지는데, 부모가 필요하면 30분 허용을 걸면
  되므로 단순한 쪽을 택했다. **(내 결정)**
- 다음은 **막지 못하며 문서에 명시**한다: 관리자 비밀번호 획득, 다른 기기 사용,
  안전 모드 부팅(로블록스도 정상 동작하지 않음), 가상 머신, 아이 세션에서
  입력되는 비밀번호 가로채기(키로거), 잠금 중 Ctrl+Alt+Del (Windows 설계상
  막을 수 없음. 로그아웃·재부팅은 가능하지만 로블록스 실행에는 도움이 안 됨).

## 5. 구조

실행 파일 두 개와 설치 스크립트로 구성한다.

```
RobloxGuard.Service.exe       Windows 서비스. LocalSystem. 실제 차단을 담당.
RobloxGuard.Tray.exe          트레이 앱. 모든 사용자 세션에서 로그인 시 자동 실행.
RobloxGuard.Lockout.exe       잠금 화면. 서비스가 아이 세션에 SYSTEM 권한으로 띄운다.
install.ps1 / uninstall.ps1   관리자 권한 PowerShell 설치/제거 스크립트.
```

### 5.1 서비스 (RobloxGuard.Service)

**역할**: 차단 상태 유지, 로블록스 프로세스 감지·종료, 네트워크 차단 적용,
허용 시간 관리, 기록 저장, 트레이 앱과의 통신.

**프로세스 감지**
- 1초 주기로 전체 프로세스를 훑는다. (WMI `Win32_ProcessStartTrace` 이벤트를
  쓰면 더 빠르지만 필수는 아니다. 빌더 선택.)
- 로블록스 판정 기준 (하나라도 맞으면 로블록스):
  1. 프로세스 이름이 알려진 목록에 있음: `RobloxPlayerBeta`, `RobloxPlayerLauncher`,
     `RobloxPlayerInstaller`, `RobloxStudioBeta`, `RobloxStudioLauncher`,
     `RobloxStudioInstaller`.
  2. 실행 파일의 `FileVersionInfo.CompanyName` 에 "Roblox" 포함.
     (아이가 exe 이름을 바꿔도 잡힌다. 로블록스는 `%LOCALAPPDATA%\Roblox` 에
     사용자 권한으로 설치되므로 이름 변경이 가능하다.)
  3. 실행 파일 경로에 `\ROBLOXCORPORATION.ROBLOX_` 포함 (Microsoft Store 버전,
     프로세스 이름은 `Windows10Universal.exe`).
- Roblox Studio도 항상 차단한다 (Studio 안에서 게임을 플레이할 수 있다).
  끄는 설정은 두지 않는다.
- 차단 모드에서 감지되면 `Process.Kill(entireProcessTree: true)`.
- 종료 1회마다 기록 1건. 종료 직후 그 프로세스의 세션 ID를 확인해
  해당 세션에 잠금 화면을 띄운다 (아래 5.3절). 같은 세션에서 잠금이 진행 중이거나
  끝난 지 10초가 안 됐으면 (런처가 여러 프로세스를 띄우므로) 종료만 하고
  새 잠금은 걸지 않는다.
- 서비스 자신, 트레이 앱, 시스템 프로세스(PID 0, 4)는 판정 대상에서 뺀다.

**네트워크 차단** (보조 수단. 주 수단은 프로세스 종료.)
- hosts 파일에 마커 블록(`# RobloxGuard BEGIN` ~ `# RobloxGuard END`)을 넣어
  `roblox.com`, `www.roblox.com`, `web.roblox.com`, `apis.roblox.com`,
  `auth.roblox.com`, `games.roblox.com`, `setup.rbxcdn.com` 을 `0.0.0.0` 으로 보낸다.
  브라우저 클라우드 게임 우회를 줄이기 위해 `now.gg`, `www.now.gg`, `app.now.gg`
  도 함께 넣는다. 도메인 목록은 `config.json` 의 `extraBlockedHosts` 로 덧붙일 수 있다.
  허용 모드로 바뀌면 블록을 제거하고, 차단 모드로 돌아오면 다시 넣는다.
  쓰기는 임시 파일 작성 후 교체로 원자적으로 한다. 쓴 뒤 `ipconfig /flushdns`.
- Windows 방화벽: 발견된 로블록스 실행 파일 경로
  (`C:\Users\*\AppData\Local\Roblox\Versions\*\*.exe`)에 대해 아웃바운드 차단
  규칙을 만든다. 규칙 이름 접두사 `RobloxGuard-`. 1분마다 경로를 다시 스캔해
  규칙을 갱신한다. 허용 모드에서는 규칙을 비활성화한다. 실패해도 서비스는
  계속 돈다 (best effort). **(내 결정)**
- 한계: 브라우저의 DNS over HTTPS 설정에 따라 hosts 차단이 무시될 수 있다.
  하지만 로블록스 게임은 브라우저 안에서 돌지 않고 반드시 플레이어 exe를 띄우므로
  프로세스 종료가 최종 방어선이다. README에 적는다.

**상태 기계**
```
차단 중 (Blocked)  --Allow(분)-->  허용 중 (Allowed, until=T)
      ^                                   |
      +---- T 도달 또는 Block 명령 --------+
```
- `allowUntil` 은 `C:\ProgramData\RobloxGuard\state.json` 에 UTC로 저장한다.
  서비스가 재시작돼도 허용 시간은 유지된다.
- 허용 시간 선택지: 30분, 1시간, 2시간, 오늘 하루(오늘 자정까지). **(내 결정)**
- 만료 5분 전과 1분 전에 아이에게 "N분 뒤 로블록스가 종료됩니다" 알림을 보낸다.
  만료 시 실행 중인 로블록스를 종료한다. **(내 결정)**
- 허용 중에 다시 Allow가 오면 새 시간으로 **교체**한다 (누적 아님).

**비밀번호**
- 설치 시 부모가 정한다. PBKDF2-SHA256, 솔트 16바이트, 반복 100,000회 이상.
  `config.json` 에 저장. 평문은 어디에도 남기지 않는다.
- 틀린 시도 5회 연속 → 5분 잠금. 잠금 중에는 맞아도 거절. 틀린 시도와 잠금은
  기록에 남긴다 (아이가 추측하는지 부모가 알 수 있다). **(내 결정)**
- 복구 수단 없음. 잊으면 관리자 권한으로 `install.ps1` 을 다시 실행해 재설정한다.

**기록**
- `C:\ProgramData\RobloxGuard\attempts.log`, JSON Lines.
- 한 줄 필드: `ts`(UTC ISO8601), `type`, `session`(Windows 사용자 이름),
  `process`(이름), `path`, `detail`.
- `type` 값: `blocked_kill`, `blocked_lockout_start`, `blocked_lockout_end`,
  `allow_start`, `allow_end`, `block_manual`, `wrong_password`, `password_lockout`,
  `service_start`, `password_changed`.
- 90일 지난 줄은 서비스 시작 시와 하루 한 번 정리한다.

**통신 (IPC)**
- 명명된 파이프 `\\.\pipe\RobloxGuard`. 줄 단위 JSON.
- 파이프 ACL: `Users` 에 읽기/쓰기 허용 (아이 세션의 트레이 앱도 상태 조회와
  알림 수신은 해야 한다). 권한이 필요한 명령은 모두 비밀번호를 동반한다.
- 요청:
  - `GetStatus` → `{mode, allowUntil, lockedUntil}`
  - `Allow {password, minutes}` → `{ok}` 또는 `{error: "wrong_password"|"locked"}`
  - `Block {}` → `{ok}` (비밀번호 불필요. 아이가 스스로 차단해도 해롭지 않다.) **(내 결정)**
  - `GetLog {password, days}` → `{entries:[...]}`
  - `ChangePassword {old, new}` → `{ok}`
  - `Subscribe {}` → 연결을 유지하며 서비스가 이벤트를 밀어 보낸다:
    `{event: "blocked", process}`, `{event: "expiring", minutes}`,
    `{event: "mode_changed", mode}`.
- 차단 안내는 트레이 앱이 아니라 잠금 화면(5.3절)이 담당하므로, 트레이 앱이
  죽어 있어도 아이에게는 똑같이 보인다.

**우회 방지**
- 설치 위치 `C:\Program Files\RobloxGuard\` (일반 사용자는 쓰기 불가).
- 데이터 위치 `C:\ProgramData\RobloxGuard\`, ACL: SYSTEM·Administrators 전체,
  Users 접근 없음.
- 서비스 계정 LocalSystem, 시작 유형 자동, 복구 옵션: 실패 시 1초 후 재시작(3회 모두).
- 서비스 보안 디스크립터는 기본값 유지 (일반 사용자는 중지 불가).
- 트레이 앱 자동 실행은 `HKLM\...\Run` 에 등록 (일반 사용자는 삭제 불가).

### 5.2 트레이 앱 (RobloxGuard.Tray)

**역할**: 부모의 조작 패널 + 아이에게 보이는 안내 창. 모든 사용자 세션에서 돈다.
자기 권한으로는 아무것도 막지 못한다. 죽여도 서비스는 계속 돈다.

- WinForms `NotifyIcon`. 아이콘 두 종류: 차단 중(자물쇠), 허용 중(열린 자물쇠).
  허용 중에는 툴팁에 남은 시간.
- 우클릭 메뉴:
  - `상태: 차단 중` / `상태: 허용 중 (42분 남음)` (비활성 항목)
  - `허용하기…` → 비밀번호 입력 창 → 30분 / 1시간 / 2시간 / 오늘 하루 버튼
  - `지금 차단하기` (허용 중일 때만 표시)
  - `기록 보기…` → 비밀번호 → 최근 7일을 날짜별로 묶어 "10월 21일 (화) 시도 3회"
    형태로 보여주고 펼치면 시각과 프로세스 표시. 30일로 늘리는 토글.
  - `비밀번호 변경…`
  - `정보`
  - 종료 메뉴는 두지 않는다. 트레이 앱은 끌 이유가 없다. **(내 결정)**
- 비밀번호 입력 창에는 작은 글씨로 "가급적 부모 계정으로 로그인해서 입력하세요"를
  표시한다. 현재 세션이 Administrators 그룹이 아니면 이 문구를 강조한다.
- 만료 예고 알림 (`expiring` 이벤트): 풍선 알림 "5분 뒤 로블록스가 종료됩니다."
- 서비스에 연결이 안 되면 아이콘을 회색으로 하고 툴팁에 "서비스에 연결할 수 없음".
  3초마다 재연결.
- UI 언어: 한국어. 문자열은 리소스 파일 한 곳에 모은다.

### 5.3 잠금 화면 (RobloxGuard.Lockout)

**역할**: 로블록스가 감지된 세션을 15초 동안 쓰지 못하게 하고 이유를 보여준다.

- 서비스가 `WTSQueryUserToken` 으로 대상 세션을 찾은 뒤, **SYSTEM 토큰을 복제해
  세션 ID만 대상 세션으로 바꿔** `CreateProcessAsUser` 로 띄운다. 아이 권한이
  아니라 SYSTEM 권한으로 아이의 데스크톱에 뜨므로, 아이는 작업 관리자에서
  이 프로세스를 끝낼 수 없고 UIPI 때문에 이 창 위에 다른 창을 올리지도 못한다.
- 인자: `--seconds 15 --reason roblox`.
- 동작:
  1. 모든 모니터를 덮는 테두리 없는 최상위 창을 띄운다. 배경은 검정.
     가운데에 흰 글씨로 "로블록스 실행이 감지되었습니다", 그 아래 큰 숫자로
     남은 초, 그 아래 작게 "부모님의 허용이 필요합니다". 커서는 숨긴다.
  2. `BlockInput(true)` 로 키보드·마우스를 막는다. 보조로 `WH_KEYBOARD_LL`,
     `WH_MOUSE_LL` 훅을 걸어 Alt+Tab, Win 키, Alt+F4 를 삼킨다.
  3. 1초마다 숫자를 갱신한다.
  4. 15초 뒤 `BlockInput(false)` 를 호출하고 종료한다.
- 안전장치:
  - 프로세스가 어떤 이유로든 죽으면 Windows 가 `BlockInput` 을 자동 해제한다.
  - 서비스는 띄운 프로세스를 기억하고, 20초가 지나도 살아 있으면 강제 종료한다.
  - 세션당 동시에 하나만 띄운다.
  - 서비스 자신이 중지될 때 살아 있는 잠금 프로세스를 모두 종료한다.
- Ctrl+Alt+Del 은 막을 수 없다. 그 화면에서 돌아오면 잠금 창이 그대로 있다.
- 시작할 때 `blocked_lockout_start`, 끝날 때 `blocked_lockout_end` 를 기록한다
  (서비스가 기록한다. 잠금 프로그램은 파일을 건드리지 않는다).

### 5.4 설치 / 제거

`install.ps1` (관리자 권한 필수, 아니면 안내 후 종료):
1. 기존 설치가 있으면 서비스 중지.
2. `C:\Program Files\RobloxGuard\` 에 파일 복사.
3. `C:\ProgramData\RobloxGuard\` 생성, ACL 설정.
4. 비밀번호 입력(두 번, 마스킹) → 해시를 `config.json` 에 기록.
   기존 config가 있으면 "비밀번호를 재설정하시겠습니까?" 확인.
5. `sc.exe create` 로 서비스 등록, 복구 옵션 설정, 시작.
6. `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` 에 트레이 앱 등록,
   현재 세션에서 트레이 앱 즉시 실행.
7. 마지막에 "아이 계정이 일반 사용자인지 확인하세요" 안내와 확인 방법 출력.

`uninstall.ps1` (관리자 권한 필수):
서비스 중지·삭제, hosts 마커 블록 제거, `RobloxGuard-*` 방화벽 규칙 삭제,
Run 등록 삭제, 트레이 프로세스 종료, 파일 삭제. 기록 파일은 남길지 묻는다.

### 5.5 데이터 파일

```
C:\ProgramData\RobloxGuard\
  config.json     { passwordHash, salt, iterations, logRetentionDays, lockoutSeconds,
                    extraProcessNames, extraBlockedHosts }
  state.json      { mode, allowUntil, failedAttempts, lockedUntil }
  attempts.log    JSON Lines
```

## 6. 기술 선택 (내 결정)

- C# / .NET 8. 서비스는 Worker Service 템플릿 + `UseWindowsService()`.
  트레이와 잠금 화면은 WinForms. 두 프로젝트가 공유하는 `RobloxGuard.Core` 라이브러리에
  프로토콜 타입, 비밀번호 해시, hosts 편집, 로그 파싱, 프로세스 판정 함수를 둔다.
- 배포: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.
  대상 PC에 .NET이 없어도 돈다.
- 외부 NuGet 의존성은 최소화. 방화벽은 `netsh advfirewall` 호출로 충분하다.
- 테스트: xUnit. 상태 기계(허용/만료/교체), 비밀번호 해시·잠금, hosts 블록
  삽입·제거(임시 파일), 로그 기록·정리·파싱, 프로세스 판정 함수(가짜 정보 주입).
  프로세스 종료·서비스 등록·방화벽·잠금 화면은 수동 통합 테스트 체크리스트로
  README에 둔다. 잠금 화면은 특히 "15초 뒤 반드시 풀리는지"와 "서비스를 중지하면
  즉시 풀리는지"를 체크리스트 맨 위에 둔다.
- 저장소 구조:
  ```
  src/RobloxGuard.Core/
  src/RobloxGuard.Service/
  src/RobloxGuard.Tray/
  src/RobloxGuard.Lockout/
  tests/RobloxGuard.Core.Tests/
  scripts/install.ps1, uninstall.ps1, build.ps1
  README.md   (설치법, 아이 계정 일반 사용자로 바꾸는 법, 한계, 수동 테스트 체크리스트)
  ```

## 7. 빌더에게 맡기는 것

- 프로세스 감지에 WMI 이벤트를 쓸지 1초 폴링만 할지.
- 파이프 프로토콜의 정확한 JSON 스키마와 오류 코드 이름.
- 트레이 아이콘 그래픽 (단순한 자물쇠면 충분).
- 기록 보기 창의 정확한 레이아웃.
- 방화벽 규칙 갱신 주기와 구현 세부.
- 로그 파일이 커졌을 때의 회전 방식 (90일 정리만 지키면 됨).

## 8. 알려진 한계 (README에 그대로 적는다)

- 아이가 관리자 비밀번호를 알면 막을 수 없다.
- 다른 기기(폰, 태블릿, 친구 PC)는 범위 밖이다.
- 브라우저 DoH 설정에 따라 roblox.com 사이트 자체는 열릴 수 있다. 게임 실행은 막힌다.
- now.gg 같은 클라우드 게임은 hosts 차단에 의존하므로 DoH 환경에서는 뚫릴 수 있다.
  새로운 서비스가 나오면 `extraBlockedHosts` 에 추가해야 한다.
- 비밀번호를 아이 세션에서 입력하면 아이가 미리 깔아둔 프로그램이 가로챌 수 있다.
  부모 계정에서 입력하는 것이 안전하다.
- 잠금 중 Ctrl+Alt+Del 은 막히지 않는다. 로그아웃·재부팅은 가능하다.
- 로블록스가 실행 파일 이름이나 회사명 메타데이터를 크게 바꾸면 판정 목록을
  갱신해야 한다. 목록은 `config.json` 의 `extraProcessNames` 로 덧붙일 수 있다.
