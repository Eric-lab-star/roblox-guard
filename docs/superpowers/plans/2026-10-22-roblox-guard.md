# RobloxGuard 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows PC에서 로블록스를 부모가 풀어주기 전까지 무조건 차단하고, 감지 시 즉시 종료 + 15초 입력 잠금 + 기록을 남기는 프로그램을 만든다.

**Architecture:** 차단 판단 로직은 전부 `RobloxGuard.Core` 의 `GuardEngine` 에 두고 단위 테스트로 검증한다. `RobloxGuard.Service` (Windows 서비스, LocalSystem)는 프로세스 열거·종료, hosts/방화벽, 잠금 프로세스 실행, 명명된 파이프 등 Windows 접착 코드만 담당한다. `RobloxGuard.Lockout` 은 서비스가 아이 세션에 SYSTEM 권한으로 띄우는 전체 화면 잠금, `RobloxGuard.Tray` 는 부모의 조작 패널이다.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), Worker Service + `UseWindowsService()`, WinForms, System.Text.Json, xUnit, PowerShell 설치 스크립트. 외부 NuGet 없음 (xUnit 제외).

**Spec:** `docs/superpowers/specs/2026-10-22-roblox-guard-design.md`

## Global Constraints

- OS: Windows 10 21H2 이상 / Windows 11, x64만.
- 모든 프로젝트 `net8.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`.
- 배포는 `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.
- 설치 위치 `C:\Program Files\RobloxGuard\`, 데이터 위치 `C:\ProgramData\RobloxGuard\` (config.json, state.json, attempts.log).
- 파이프 이름 `RobloxGuard`. 줄 단위 JSON. JSON은 camelCase, enum은 문자열.
- 비밀번호: PBKDF2-SHA256, 솔트 16바이트, 반복 100,000회, 해시 32바이트. 틀린 시도 5회 → 5분 잠금.
- 허용 선택지: 30분 / 1시간 / 2시간 / 오늘 하루(로컬 자정까지, 최소 1분). 허용은 교체, 누적 아님. 최대 1440분.
- 만료 5분 전·1분 전 알림. 만료 시 로블록스 종료.
- 잠금 15초 (`lockoutSeconds` 설정, 상한 60초). 세션당 하나. 같은 세션에서 잠금 종료 후 10초 안의 재감지는 종료만 하고 잠금 없음. 20초 초과 생존 시 서비스가 강제 종료.
- hosts 마커: `# RobloxGuard BEGIN` / `# RobloxGuard END`. 기본 차단 도메인: `roblox.com`, `www.roblox.com`, `web.roblox.com`, `apis.roblox.com`, `auth.roblox.com`, `games.roblox.com`, `setup.rbxcdn.com`, `now.gg`, `www.now.gg`, `app.now.gg`. 모두 `0.0.0.0`.
- 방화벽 규칙 이름 접두사 `RobloxGuard-`.
- 기록 보관 90일. "시도 횟수"는 `blocked_lockout_start` 건수로 센다.
- UI 문자열은 한국어, 트레이 앱의 `Strings.cs` 한 곳에 모은다. 아이에게 보이는 문구는 중립적.
- 우리 실행 파일 이름은 모두 `RobloxGuard.*` 로 시작하고, 어셈블리 Company 는 `RobloxGuard`. 탐지기는 `RobloxGuard` 로 시작하는 이름을 절대 로블록스로 판정하지 않는다.

## Review Focus

1. hosts 파일 끝에 개행이 없거나 CRLF/LF가 섞여 있어도 블록이 마지막 줄에 붙어 쓰이지 않고, 제거하면 원문이 그대로 돌아와야 한다. → Task 5 테스트.
2. 서비스 시작 시 `state.json` 의 `allowUntil` 이 이미 지난 시각이면 즉시 차단 모드로 올라오고 hosts 차단을 다시 적용해야 한다. → Task 9 테스트.
3. `Allow` 의 minutes 가 0 이하이거나 1440 초과, `GetLog` 의 days 가 1~90 밖이면 `bad_request` 로 거절하고 상태를 바꾸지 않아야 한다. → Task 9 테스트.
4. `config.json` 이 깨졌거나 비밀번호가 설정돼 있지 않아도 서비스는 죽지 않고 차단은 계속하며, `Allow` 는 `not_configured` 를 돌려줘야 한다 (fail closed). → Task 7, Task 9 테스트.
5. `attempts.log` 에 깨진 줄(부분 기록)이 있어도 읽기는 그 줄만 건너뛰고 나머지를 돌려줘야 한다. → Task 6 테스트.

---

## 파일 구조

```
RobloxGuard.sln
Directory.Build.props                  공통 빌드 설정 (TFM, Nullable, Company, Version)
.gitignore
src/RobloxGuard.Core/
  RobloxGuard.Core.csproj
  Clock.cs                             IClock, SystemClock
  GuardPaths.cs                        경로·파이프 이름 상수
  JsonFile.cs                          JSON 원자적 저장/로드 + 공통 JsonSerializerOptions
  Security/PasswordHasher.cs           PasswordRecord, Hash, Verify
  Security/LoginThrottle.cs            5회 실패 → 5분 잠금
  State/GuardStateMachine.cs           GuardMode, GuardEvent, Allow/Block/Tick, 경고 임계값
  State/AllowDurations.cs              프리셋, 오늘 하루 계산, 상한
  State/GuardStateSnapshot.cs          state.json 모양
  Config/GuardConfig.cs                config.json 모양
  Detection/RobloxDetector.cs          ProcessInfo, IsRoblox
  Network/HostsFileEditor.cs           마커 블록 삽입/제거/원자적 적용
  Logging/AttemptLog.cs                AttemptTypes, AttemptEntry, Append/ReadSince/Prune
  Logging/LogSummary.cs                날짜별 묶기
  Protocol/Messages.cs                 PipeRequest, PipeResponse, PipeEvent, 상수
  Protocol/ProtocolJson.cs             Serialize/Deserialize
  Engine/GuardEngine.cs                모든 판단 로직. INetworkEnforcer, ILockoutLauncher 인터페이스
src/RobloxGuard.Service/
  RobloxGuard.Service.csproj
  Program.cs                           set-password 모드 / 호스트 구성
  Native/Wts.cs                        세션 ID → 사용자 이름
  Native/SystemProcessLauncher.cs      SYSTEM 토큰으로 특정 세션에 프로세스 실행
  ProcessWatcher.cs                    1초 폴링, 판정, 종료, Tick
  WindowsNetworkEnforcer.cs            hosts + flushdns + netsh 방화벽
  WindowsLockoutLauncher.cs            잠금 프로세스 실행·추적·20초 감시
  PipeServer.cs                        명명된 파이프 서버, ACL, 구독
  MaintenanceService.cs                60초마다 방화벽 갱신·잠금 감시, 하루 한 번 로그 정리
src/RobloxGuard.Lockout/
  RobloxGuard.Lockout.csproj
  Program.cs                           인자 파싱, 상한, 비상 종료 타이머
  InputBlocker.cs                      BlockInput + 저수준 훅
  LockoutForm.cs                       모니터별 검은 전체 화면 + 카운트다운
  NativeMethods.cs
src/RobloxGuard.Tray/
  RobloxGuard.Tray.csproj
  Program.cs                           세션당 단일 인스턴스
  Strings.cs                           한국어 문자열 전부
  ServiceConnection.cs                 파이프 클라이언트, 상태 폴링, 이벤트 구독·재연결
  IconFactory.cs                       자물쇠 아이콘 3종을 GDI로 그림
  TrayContext.cs                       NotifyIcon, 메뉴, 이벤트 처리
  Forms/PasswordDialog.cs
  Forms/AllowDialog.cs
  Forms/ChangePasswordDialog.cs
  Forms/LogViewerForm.cs
tests/RobloxGuard.Core.Tests/
  RobloxGuard.Core.Tests.csproj
  FakeClock.cs
  (Task별 테스트 파일)
scripts/build.ps1, install.ps1, uninstall.ps1
README.md
```

---

### Task 1: 솔루션 뼈대와 테스트 파이프라인

**Files:**
- Create: `Directory.Build.props`, `.gitignore`, `RobloxGuard.sln`
- Create: `src/RobloxGuard.Core/RobloxGuard.Core.csproj`, `src/RobloxGuard.Core/Clock.cs`, `src/RobloxGuard.Core/GuardPaths.cs`
- Create: `tests/RobloxGuard.Core.Tests/RobloxGuard.Core.Tests.csproj`, `tests/RobloxGuard.Core.Tests/FakeClock.cs`, `tests/RobloxGuard.Core.Tests/ClockTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace RobloxGuard.Core;
  public interface IClock { DateTimeOffset UtcNow { get; } }
  public sealed class SystemClock : IClock
  public static class GuardPaths {
    public const string PipeName = "RobloxGuard";
    public static string InstallDir;   // C:\Program Files\RobloxGuard
    public static string DataDir;      // C:\ProgramData\RobloxGuard
    public static string ConfigPath, StatePath, LogPath;   // DataDir 아래 config.json, state.json, attempts.log
    public static string HostsPath;    // %SystemRoot%\System32\drivers\etc\hosts
  }
  // tests 프로젝트
  public sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } public void Advance(TimeSpan by); }
  ```

- [ ] **Step 1: .NET 8 SDK 설치 확인**

Run: `dotnet --version`
없으면: `winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements` 후 새 셸에서 다시 확인.
Expected: `8.x.x`

- [ ] **Step 2: `Directory.Build.props` 작성**

`TargetFramework=net8.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `Company=RobloxGuard`, `Product=RobloxGuard`, `Version=1.0.0`, `TreatWarningsAsErrors=false`, `SatelliteResourceLanguages=en`.

- [ ] **Step 3: 프로젝트 생성**

```powershell
dotnet new sln -n RobloxGuard
dotnet new classlib -o src/RobloxGuard.Core -n RobloxGuard.Core
dotnet new xunit -o tests/RobloxGuard.Core.Tests -n RobloxGuard.Core.Tests
dotnet sln add src/RobloxGuard.Core tests/RobloxGuard.Core.Tests
dotnet add tests/RobloxGuard.Core.Tests reference src/RobloxGuard.Core
```
생성된 `Class1.cs`, `UnitTest1.cs` 삭제. csproj 에서 Directory.Build.props 와 겹치는 속성 제거. `.gitignore` 는 `dotnet new gitignore` 결과에 `dist/` 추가.

- [ ] **Step 4: 실패하는 테스트 작성** (`ClockTests.cs`)

```csharp
[Fact] public void FakeClock_Advance_MovesTime() {
  var c = new FakeClock { UtcNow = new DateTimeOffset(2026,10,22,0,0,0,TimeSpan.Zero) };
  c.Advance(TimeSpan.FromMinutes(5));
  Assert.Equal(new DateTimeOffset(2026,10,22,0,5,0,TimeSpan.Zero), c.UtcNow);
}
[Fact] public void GuardPaths_AreUnderExpectedRoots() {
  Assert.EndsWith(@"\RobloxGuard\config.json", GuardPaths.ConfigPath);
  Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), GuardPaths.DataDir);
}
```

- [ ] **Step 5: 실패 확인**

Run: `dotnet test tests/RobloxGuard.Core.Tests`
Expected: 컴파일 오류 (FakeClock, GuardPaths 없음)

- [ ] **Step 6: `Clock.cs`, `GuardPaths.cs`, `FakeClock.cs` 구현**

`GuardPaths` 는 `Environment.SpecialFolder.ProgramFiles`, `CommonApplicationData`, `Environment.GetFolderPath(SpecialFolder.System)` 로 조합한다.

- [ ] **Step 7: 통과 확인**

Run: `dotnet test tests/RobloxGuard.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 8: 커밋**

```bash
git add -A && git commit -m "chore: 솔루션 뼈대, Core/테스트 프로젝트, Clock, GuardPaths"
```

---

### Task 2: 비밀번호 해시와 잠금 스로틀

**Files:**
- Create: `src/RobloxGuard.Core/Security/PasswordHasher.cs`, `src/RobloxGuard.Core/Security/LoginThrottle.cs`
- Test: `tests/RobloxGuard.Core.Tests/PasswordHasherTests.cs`, `tests/RobloxGuard.Core.Tests/LoginThrottleTests.cs`

**Interfaces:**
- Consumes: `IClock`, `FakeClock` (Task 1)
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Security;
  public sealed record PasswordRecord(string Salt, string Hash, int Iterations);   // base64, base64
  public static class PasswordHasher {
    public const int DefaultIterations = 100_000;
    public static PasswordRecord Hash(string password);
    public static bool Verify(string password, PasswordRecord record);
  }
  public sealed class LoginThrottle {
    public LoginThrottle(IClock clock, int maxFailures = 5, TimeSpan? lockDuration = null); // 기본 5분
    public int FailedAttempts { get; }
    public DateTimeOffset? LockedUntil { get; }
    public bool IsLocked { get; }          // 지났으면 false 로 돌아가고 카운터 초기화
    public bool RecordFailure();           // 이번 실패로 잠겼으면 true
    public void RecordSuccess();           // 카운터 초기화
    public void Restore(int failedAttempts, DateTimeOffset? lockedUntil);
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// PasswordHasherTests
[Fact] public void Hash_ThenVerify_Succeeds() => Assert.True(PasswordHasher.Verify("abc123", PasswordHasher.Hash("abc123")));
[Fact] public void Verify_WrongPassword_Fails() => Assert.False(PasswordHasher.Verify("abc124", PasswordHasher.Hash("abc123")));
[Fact] public void Hash_UsesFreshSaltEachTime() { var a = PasswordHasher.Hash("x"); var b = PasswordHasher.Hash("x"); Assert.NotEqual(a.Salt, b.Salt); Assert.NotEqual(a.Hash, b.Hash); }
[Fact] public void Hash_Parameters() { var r = PasswordHasher.Hash("x"); Assert.Equal(100_000, r.Iterations); Assert.Equal(16, Convert.FromBase64String(r.Salt).Length); Assert.Equal(32, Convert.FromBase64String(r.Hash).Length); }

// LoginThrottleTests (clock 은 FakeClock)
[Fact] public void FourFailures_NotLocked() { 4번 RecordFailure → IsLocked false, FailedAttempts 4 }
[Fact] public void FifthFailure_LocksForFiveMinutes() { 5번째 RecordFailure 가 true 반환, IsLocked true, LockedUntil == now+5m }
[Fact] public void Lock_ExpiresAndResetsCounter() { 잠근 뒤 Advance(5m) → IsLocked false, FailedAttempts 0 }
[Fact] public void Success_ResetsCounter() { 3번 실패, RecordSuccess → FailedAttempts 0 }
[Fact] public void Restore_ReappliesLock() { Restore(0, now+2m) → IsLocked true }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test tests/RobloxGuard.Core.Tests`
Expected: 컴파일 오류

- [ ] **Step 3: 구현**

`PasswordHasher.Hash` 는 `RandomNumberGenerator.GetBytes(16)` + `Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32)`. `Verify` 는 `CryptographicOperations.FixedTimeEquals`.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test tests/RobloxGuard.Core.Tests`
Expected: 전부 PASS

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): PBKDF2 비밀번호 해시와 5회 실패 잠금 스로틀"
```

---

### Task 3: 차단/허용 상태 기계와 허용 시간 계산

**Files:**
- Create: `src/RobloxGuard.Core/State/GuardStateMachine.cs`, `src/RobloxGuard.Core/State/AllowDurations.cs`, `src/RobloxGuard.Core/State/GuardStateSnapshot.cs`
- Test: `tests/RobloxGuard.Core.Tests/GuardStateMachineTests.cs`, `tests/RobloxGuard.Core.Tests/AllowDurationsTests.cs`

**Interfaces:**
- Consumes: `IClock`
- Produces:
  ```csharp
  namespace RobloxGuard.Core.State;
  public enum GuardMode { Blocked, Allowed }
  public enum GuardEventKind { ModeChanged, Expiring }
  public sealed record GuardEvent(GuardEventKind Kind, GuardMode Mode, DateTimeOffset? AllowUntil, int? MinutesLeft);
  public sealed class GuardStateMachine {
    public GuardStateMachine(IClock clock);
    public GuardMode Mode { get; }
    public DateTimeOffset? AllowUntil { get; }
    public GuardEvent Allow(TimeSpan duration);      // 항상 ModeChanged(Allowed) 반환 (교체)
    public GuardEvent? Block();                      // 이미 Blocked 면 null
    public IReadOnlyList<GuardEvent> Tick();         // 만료 → ModeChanged(Blocked); 5분/1분 임계 → Expiring
    public void Restore(GuardMode mode, DateTimeOffset? allowUntil);
  }
  public static class AllowDurations {
    public static readonly int[] PresetMinutes = { 30, 60, 120 };
    public const int MaxMinutes = 1440;
    public static readonly int[] WarningMinutes = { 5, 1 };
    public static TimeSpan UntilEndOfDay(DateTimeOffset nowUtc, TimeZoneInfo tz);   // 로컬 다음 자정까지, 최소 1분
  }
  public sealed record GuardStateSnapshot(GuardMode Mode, DateTimeOffset? AllowUntil, int FailedAttempts, DateTimeOffset? LockedUntil) {
    public static GuardStateSnapshot Default => new(GuardMode.Blocked, null, 0, null);
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// GuardStateMachineTests, now = 2026-10-22T10:00Z
[Fact] Initial_IsBlocked
[Fact] Allow30_SetsAllowedUntilNowPlus30 → Mode Allowed, AllowUntil now+30m, 반환 이벤트 Kind ModeChanged
[Fact] Allow_Replaces_NotAccumulates → Allow(30m) 후 Advance(10m), Allow(30m) → AllowUntil == now+40m (원래 기준), 즉 현재+30m
[Fact] Block_FromAllowed_ReturnsModeChanged; Block_FromBlocked_ReturnsNull
[Fact] Tick_BeforeExpiry_NoEvents → Allow(30m), Advance(10m), Tick() 빈 목록
[Fact] Tick_At5MinLeft_RaisesExpiring5_Once → Allow(30m), Advance(25m), Tick() == [Expiring 5]; 다시 Tick() 빈 목록
[Fact] Tick_At1MinLeft_RaisesExpiring1
[Fact] Tick_AtExpiry_ReturnsBlocked → Advance(30m), Tick() 에 ModeChanged(Blocked) 포함, Mode Blocked, AllowUntil null
[Fact] Allow3Min_Only1MinWarning → Allow(3m), Advance(2m), Tick()==[Expiring 1]; 5분 경고는 영영 없음
[Fact] Allow1Min_NoWarnings → Allow(1m), Advance(1m), Tick() 에 Expiring 없음, ModeChanged(Blocked) 만
[Fact] Restore_PastAllowUntil_FirstTickExpires → Restore(Allowed, now-1m); Tick() == [ModeChanged Blocked]
[Fact] Restore_FutureAllowUntil_KeepsAllowed

// AllowDurationsTests (tz = Asia/Seoul 또는 TimeZoneInfo.CreateCustomTimeZone(+9))
[Fact] UntilEndOfDay_At2300_Is60Min → 로컬 23:00 → 60분
[Fact] UntilEndOfDay_At235930_IsAtLeast1Min → 30초 남아도 1분
[Fact] UntilEndOfDay_AtMidnight_Is24h
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test` Expected: 컴파일 오류

- [ ] **Step 3: 구현**

경고 임계값은 `Allow` 시점에 "남은 시간 ≥ 임계" 인 것만 활성으로 표시하고(`_pendingWarnings`), `Tick` 에서 남은 시간이 임계 이하로 내려간 순간 한 번 발생시킨 뒤 제거한다. `Restore` 도 같은 규칙으로 임계를 계산한다.

- [ ] **Step 4: 통과 확인** — Run: `dotnet test` Expected: 전부 PASS

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): 차단/허용 상태 기계, 만료 경고, 오늘 하루 계산"
```

---

### Task 4: 로블록스 프로세스 판정기

**Files:**
- Create: `src/RobloxGuard.Core/Detection/RobloxDetector.cs`
- Test: `tests/RobloxGuard.Core.Tests/RobloxDetectorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Detection;
  public sealed record ProcessInfo(int Pid, string Name, string? Path, string? CompanyName, int SessionId);  // Name 은 .exe 없이
  public static class RobloxDetector {
    public static readonly string[] KnownProcessNames = { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxPlayerInstaller", "RobloxStudioBeta", "RobloxStudioLauncher", "RobloxStudioInstaller" };
    public const string StorePathMarker = @"\ROBLOXCORPORATION.ROBLOX_";
    public const string RobloxCompanyName = "Roblox Corporation";
    public const string SelfPrefix = "RobloxGuard";
    public static bool IsRoblox(ProcessInfo p, IReadOnlyCollection<string>? extraNames = null);
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Theory][InlineData("RobloxPlayerBeta")][InlineData("robloxplayerbeta")][InlineData("RobloxPlayerBeta.exe")][InlineData("RobloxStudioBeta")]
 KnownName_IsRoblox(string name) → true (Path, CompanyName null)
[Fact] RenamedExe_WithRobloxCompany_IsRoblox → Name "game", CompanyName "Roblox Corporation" → true
[Fact] StorePath_IsRoblox → Name "Windows10Universal", Path @"C:\Program Files\WindowsApps\ROBLOXCORPORATION.ROBLOX_2.0_x64__55nm5eh3cm0pr\Windows10Universal.exe" → true
[Fact] UnrelatedNameContainingRoblox_IsNot → Name "roblox_notes", CompanyName "Acme" → false
[Fact] SelfProcesses_AreNeverRoblox → Name "RobloxGuard.Tray", CompanyName "Roblox Corporation" → false
[Fact] ExtraName_IsRoblox → Name "Bloxstrap", extraNames ["Bloxstrap"] → true
[Fact] NullEverything_IsNot → Name "", null, null → false
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test` Expected: 컴파일 오류

- [ ] **Step 3: 구현** — 이름은 `.exe` 접미 제거 후 `OrdinalIgnoreCase` 비교. Self 검사가 제일 먼저.

- [ ] **Step 4: 통과 확인** — Run: `dotnet test` Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): 이름·회사명·스토어 경로 기반 로블록스 판정기"
```

---

### Task 5: hosts 파일 편집기

**Files:**
- Create: `src/RobloxGuard.Core/Network/HostsFileEditor.cs`
- Test: `tests/RobloxGuard.Core.Tests/HostsFileEditorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Network;
  public static class HostsFileEditor {
    public const string BeginMarker = "# RobloxGuard BEGIN";
    public const string EndMarker = "# RobloxGuard END";
    public static readonly string[] DefaultBlockedHosts = { /* Global Constraints 의 10개 */ };
    public static string InsertBlock(string content, IEnumerable<string> hosts);   // 기존 블록 제거 후 끝에 추가
    public static string RemoveBlock(string content);
    public static void Apply(string hostsPath, bool blocked, IEnumerable<string> hosts);   // 임시 파일 → File.Move(overwrite)
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Fact] InsertBlock_IntoEmpty_ProducesMarkedBlock → 결과에 BeginMarker, "0.0.0.0 roblox.com", EndMarker 순서대로 포함
[Fact] InsertBlock_Twice_IsIdempotent → InsertBlock(InsertBlock(x)) == InsertBlock(x)
[Fact] RemoveBlock_RestoresOriginalExactly → original = "127.0.0.1 localhost\r\n"; RemoveBlock(InsertBlock(original)) == original
[Fact] InsertBlock_NoTrailingNewline_StartsOnNewLine → original "127.0.0.1 localhost" (개행 없음) → 결과에 "localhost\r\n# RobloxGuard BEGIN" 포함, "localhost# RobloxGuard" 미포함
[Fact] RemoveBlock_NoTrailingNewline_RestoresOriginal → 위 original 에 대해 왕복 후 == original
[Fact] MixedLineEndings_RoundTrip → "a\nb\r\nc" 왕복 == 원문
[Fact] RemoveBlock_WithoutBlock_IsNoop
[Fact] Apply_WritesAndLeavesNoTempFile → 임시 디렉터리에 파일 생성, Apply(path,true,...) → 파일에 BeginMarker 포함; Apply(path,false,...) → 미포함; 디렉터리에 파일 1개만 존재
[Fact] Apply_OnMissingFile_CreatesIt
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test` Expected: 컴파일 오류

- [ ] **Step 3: 구현** — 블록은 `\r\n` 로 쓴다. 블록 제거는 마커 줄 포함 구간을 정규식 `(?:\r?\n)?# RobloxGuard BEGIN.*?# RobloxGuard END(?:\r?\n)?` (Singleline) 로 제거하되, 삽입 시 원문 끝 개행 유무를 기억해 제거 후 원문과 바이트 동일하게 돌아가도록 한다. `Apply` 는 같은 디렉터리의 `hosts.robloxguard.tmp` 에 쓴 뒤 `File.Move(tmp, path, overwrite: true)`. 인코딩 UTF-8 BOM 없음.

- [ ] **Step 4: 통과 확인** — Run: `dotnet test` Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): hosts 마커 블록 삽입/제거와 원자적 적용"
```

---

### Task 6: 시도 기록과 날짜별 요약

**Files:**
- Create: `src/RobloxGuard.Core/JsonFile.cs`, `src/RobloxGuard.Core/Logging/AttemptLog.cs`, `src/RobloxGuard.Core/Logging/LogSummary.cs`
- Test: `tests/RobloxGuard.Core.Tests/AttemptLogTests.cs`, `tests/RobloxGuard.Core.Tests/LogSummaryTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace RobloxGuard.Core;
  public static class JsonFile {
    public static JsonSerializerOptions Options { get; }   // camelCase, JsonStringEnumConverter, WriteIndented=false
    public static T LoadOrDefault<T>(string path, Func<T> fallback);   // 없거나 깨지면 fallback()
    public static void SaveAtomic<T>(string path, T value);            // 디렉터리 생성, tmp → Move overwrite
  }
  namespace RobloxGuard.Core.Logging;
  public static class AttemptTypes { BlockedKill="blocked_kill", LockoutStart="blocked_lockout_start", LockoutEnd="blocked_lockout_end", AllowStart="allow_start", AllowEnd="allow_end", BlockManual="block_manual", WrongPassword="wrong_password", PasswordLockout="password_lockout", ServiceStart="service_start", PasswordChanged="password_changed" }
  public sealed record AttemptEntry(DateTimeOffset Ts, string Type, string? Session, string? Process, string? Path, string? Detail);
  public sealed class AttemptLog {
    public AttemptLog(string path);
    public void Append(AttemptEntry entry);                       // lock, 한 줄 JSON + "\n"
    public IReadOnlyList<AttemptEntry> ReadSince(DateTimeOffset since);   // 오래된 → 최신 순
    public int Prune(DateTimeOffset olderThan);                   // 지운 줄 수, 원자적 재작성
  }
  public sealed record DaySummary(DateOnly Day, int AttemptCount, IReadOnlyList<AttemptEntry> Entries);
  public static class LogSummary {
    public static IReadOnlyList<DaySummary> GroupByDay(IEnumerable<AttemptEntry> entries, TimeZoneInfo tz);   // 최신 날짜 먼저, AttemptCount = LockoutStart 건수
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// AttemptLogTests (임시 파일 경로)
[Fact] Append_ThenReadSince_RoundTrips → 2건 기록, ReadSince(min) 2건, 필드 동일
[Fact] ReadSince_FiltersOlder → ts 10:00, 11:00 기록; ReadSince(10:30) → 1건
[Fact] ReadSince_SkipsCorruptLine → 파일에 정상 줄, "{garbage", 정상 줄 → 2건
[Fact] Prune_RemovesOldKeepsNew → 100일 전 1건, 어제 1건; Prune(now-90d) == 1; ReadSince(min) 1건
[Fact] ReadSince_MissingFile_ReturnsEmpty
// JsonFile
[Fact] SaveAtomic_ThenLoad_RoundTrips; LoadOrDefault_CorruptFile_ReturnsFallback; LoadOrDefault_Missing_ReturnsFallback
// LogSummaryTests (tz +9)
[Fact] GroupByDay_UsesLocalDay → UTC 2026-10-21T16:00 (로컬 22일 01:00) 과 UTC 22일 02:00 → 같은 날 22일
[Fact] AttemptCount_CountsLockoutStartOnly → 같은 날 blocked_kill 3, blocked_lockout_start 1 → AttemptCount 1, Entries 4
[Fact] Days_NewestFirst
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test` Expected: 컴파일 오류

- [ ] **Step 3: 구현** — `AttemptLog` 는 `FileShare.ReadWrite` 로 열고, 깨진 줄은 `JsonException` 을 잡아 건너뛴다.

- [ ] **Step 4: 통과 확인** — Run: `dotnet test` Expected: PASS

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): JSON Lines 시도 기록, 정리, 날짜별 요약"
```

---

### Task 7: 설정과 상태 파일

**Files:**
- Create: `src/RobloxGuard.Core/Config/GuardConfig.cs`
- Test: `tests/RobloxGuard.Core.Tests/GuardConfigTests.cs`

**Interfaces:**
- Consumes: `JsonFile`, `PasswordRecord`, `GuardStateSnapshot`
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Config;
  public sealed class GuardConfig {
    public PasswordRecord? Password { get; set; }
    public int LogRetentionDays { get; set; } = 90;
    public int LockoutSeconds { get; set; } = 15;        // 로드 시 1..60 으로 클램프
    public List<string> ExtraProcessNames { get; set; } = new();
    public List<string> ExtraBlockedHosts { get; set; } = new();
    public bool IsConfigured => Password is not null;
    public static GuardConfig Load(string path);         // JsonFile.LoadOrDefault + 클램프
    public void Save(string path);
    public IReadOnlyList<string> AllBlockedHosts();      // Default + Extra, 중복 제거
  }
  // GuardStateSnapshot 에 확장: public static GuardStateSnapshot Load(string path); public void Save(string path);
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Fact] Load_Missing_ReturnsDefaults → IsConfigured false, LockoutSeconds 15, LogRetentionDays 90
[Fact] Load_Corrupt_ReturnsDefaults → 파일에 "not json"
[Fact] Save_ThenLoad_RoundTrips → Password, ExtraBlockedHosts ["x.com"] 유지
[Fact] LockoutSeconds_Clamped → 파일에 999 → 60; 0 → 1
[Fact] AllBlockedHosts_MergesWithoutDuplicates → Extra ["roblox.com","x.com"] → Default 10 + 1
[Fact] Snapshot_Save_Load_RoundTrips; Snapshot_Missing_ReturnsDefault
```

- [ ] **Step 2: 실패 확인** → **Step 3: 구현** → **Step 4: 통과 확인** (Run: `dotnet test`, Expected: PASS)

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): config.json / state.json 로드·저장"
```

---

### Task 8: 파이프 프로토콜 메시지

**Files:**
- Create: `src/RobloxGuard.Core/Protocol/Messages.cs`, `src/RobloxGuard.Core/Protocol/ProtocolJson.cs`
- Test: `tests/RobloxGuard.Core.Tests/ProtocolTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Protocol;
  public static class RequestTypes { GetStatus, Allow, Block, GetLog, ChangePassword, Subscribe }   // 문자열 상수, 값은 이름과 동일
  public static class ErrorCodes { WrongPassword="wrong_password", Locked="locked", BadRequest="bad_request", NotConfigured="not_configured" }
  public static class EventNames { ModeChanged="mode_changed", Expiring="expiring", Blocked="blocked" }
  public sealed class PipeRequest { string Type; string? Password; int? Minutes; int? Days; string? OldPassword; string? NewPassword; }
  public sealed class PipeResponse {
    bool Ok; string? Error; string? Mode; DateTimeOffset? AllowUntil; DateTimeOffset? LockedUntil; List<AttemptEntry>? Entries;
    public static PipeResponse Success(); public static PipeResponse Fail(string error);
  }
  public sealed class PipeEvent { string Event; string? Mode; DateTimeOffset? AllowUntil; int? Minutes; string? Process; }
  public static class ProtocolJson {
    public static string Serialize<T>(T value);      // 한 줄, 개행 없음
    public static T? Deserialize<T>(string line);    // 실패 시 null
  }
  ```

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Fact] Request_RoundTrip → Allow, Password "p", Minutes 60
[Fact] Response_RoundTrip_WithEntries
[Fact] Event_RoundTrip
[Fact] Serialize_HasNoNewline
[Fact] Deserialize_Garbage_ReturnsNull → "{nope"
[Fact] Serialize_UsesCamelCase → 문자열에 "\"type\":\"Allow\"" 포함
```

- [ ] **Step 2: 실패 확인** → **Step 3: 구현** (`JsonFile.Options` 재사용) → **Step 4: 통과 확인**

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): 파이프 요청/응답/이벤트 메시지와 JSON 코덱"
```

---

### Task 9: GuardEngine — 모든 판단 로직

**Files:**
- Create: `src/RobloxGuard.Core/Engine/GuardEngine.cs`
- Test: `tests/RobloxGuard.Core.Tests/GuardEngineTests.cs`

**Interfaces:**
- Consumes: Task 2~8 전부
- Produces:
  ```csharp
  namespace RobloxGuard.Core.Engine;
  public interface INetworkEnforcer { void SetBlocked(bool blocked); }
  public interface ILockoutLauncher { void Launch(int sessionId, int seconds); }
  public sealed record DetectionDecision(bool Kill, bool LockoutStarted);
  public sealed class GuardEngine {
    public GuardEngine(IClock clock, GuardConfig config, GuardStateSnapshot initial, AttemptLog log, INetworkEnforcer network, ILockoutLauncher lockout);
    public GuardMode Mode { get; }
    public GuardConfig Config { get; }
    public event Action<PipeEvent>? EventRaised;
    public event Action<GuardStateSnapshot>? StateChanged;    // 저장용. Allow/Block/만료/비밀번호 실패·잠금 시
    public event Action<GuardConfig>? ConfigChanged;          // ChangePassword 시
    public void Start();                                      // service_start 기록, log.Prune(now-retention), network.SetBlocked(Mode==Blocked), 과거 allowUntil 이면 즉시 만료 처리
    public PipeResponse GetStatus();
    public PipeResponse Allow(string? password, int? minutes);
    public PipeResponse Block();
    public PipeResponse GetLog(string? password, int? days);
    public PipeResponse ChangePassword(string? oldPassword, string? newPassword);
    public void Tick();                                       // 1초마다. 만료 → SetBlocked(true), allow_end, mode_changed; 경고 → expiring
    public DetectionDecision OnRobloxDetected(ProcessInfo p, string? sessionUser);
    public void OnLockoutEnded(int sessionId);                // blocked_lockout_end 기록
    public static bool IsAdminSession(...) 는 여기 두지 않는다 (트레이 전용).
  }
  ```
  규칙:
  - 비밀번호 검증 순서: `!Config.IsConfigured` → `not_configured`; `throttle.IsLocked` → `locked` (LockedUntil 포함); `!Verify` → `RecordFailure`, `wrong_password` 기록, 잠기면 `password_lockout` 기록, `wrong_password` 반환; 성공 → `RecordSuccess`.
  - `Allow`: minutes null/≤0/>1440 → `bad_request` (비밀번호 검사보다 먼저 아님: 비밀번호 먼저, 그다음 범위). 성공 시 `allow_start` (Detail = minutes), `SetBlocked(false)`, `mode_changed` 이벤트, StateChanged.
  - `Block`: 비밀번호 없음. Allowed 였으면 `block_manual`, `SetBlocked(true)`, `mode_changed`. 이미 Blocked 면 ok 만.
  - `GetLog`: days 1..90 아니면 `bad_request`. Entries 는 `ReadSince(now-days)`.
  - `ChangePassword`: old 검증(위 순서), new 가 비었거나 4자 미만이면 `bad_request`; 성공 시 `password_changed`, ConfigChanged.
  - `OnRobloxDetected`: Mode==Allowed → `(false,false)`. Blocked → `blocked_kill` 기록(Session=sessionUser, Process, Path), `blocked` 이벤트; 세션별 마지막 잠금 시작 시각을 보고 `now ≥ last + LockoutSeconds + 10s` (또는 처음) 이면 `lockout.Launch(sessionId, LockoutSeconds)`, `blocked_lockout_start` 기록, `(true,true)`; 아니면 `(true,false)`.
  - `Tick` 만료: `allow_end` 기록, `SetBlocked(true)`.

- [ ] **Step 1: 실패하는 테스트 작성** (FakeClock, 임시 로그 파일, `FakeNetwork { List<bool> Calls }`, `FakeLockout { List<(int,int)> Calls }`, 비밀번호 "pw1234" 설정된 config)

```csharp
[Fact] Start_Blocked_AppliesNetworkBlock → network.Calls == [true]; 로그에 service_start
[Fact] Start_WithPastAllowUntil_ExpiresImmediately → initial(Allowed, now-1m) → Mode Blocked, network.Calls 마지막 true, 로그에 allow_end
[Fact] Start_WithFutureAllowUntil_StaysAllowed → network.Calls == [false]
[Fact] Allow_CorrectPassword_Unblocks → ok, Mode Allowed, AllowUntil now+60m, network.Calls 끝 false, 이벤트 mode_changed/Allowed, 로그 allow_start
[Fact] Allow_WrongPassword_Rejected → error wrong_password, Mode Blocked, 로그 wrong_password
[Fact] Allow_FiveWrong_Locks_ThenCorrectRejected → 5회 틀림 → 마지막 error wrong_password 이고 로그에 password_lockout; 이어서 맞는 비밀번호 → error locked, LockedUntil now+5m
[Fact] Allow_LockExpires_ThenCorrectWorks → Advance(5m) 후 ok
[Theory][InlineData(0)][InlineData(-5)][InlineData(1441)][InlineData(null)] Allow_BadMinutes_BadRequest → error bad_request, Mode Blocked, network.Calls 길이 변화 없음
[Fact] Allow_NotConfigured_ReturnsNotConfigured → Password null config
[Fact] Allow_Replaces → Allow 60 후 Advance 30, Allow 30 → AllowUntil now+30
[Fact] Block_WhenAllowed_Reblocks → ok, Blocked, network true, 로그 block_manual
[Fact] Block_WhenBlocked_OkNoLog
[Fact] Tick_Expiry_Reblocks → Allow 30, Advance 30, Tick → Blocked, network true, allow_end, 이벤트 mode_changed/Blocked
[Fact] Tick_Warnings_RaiseExpiringEvents → 5분, 1분 이벤트 Minutes 값 확인
[Fact] Detected_WhileBlocked_KillsAndLocks → (true,true), lockout.Calls == [(sid 2, 15)], 로그 blocked_kill + blocked_lockout_start, 이벤트 blocked
[Fact] Detected_Again_Within10sAfterLockout_KillsWithoutLock → Advance(20s) (15+5) → (true,false); Advance(6s) 더 → (true,true)
[Fact] Detected_DifferentSession_GetsOwnLockout → 세션 2 와 3 각각 Launch
[Fact] Detected_WhileAllowed_NoKill → (false,false), 로그·launch 없음
[Fact] Detected_NotConfigured_StillKills → Password null → (true,true) (fail closed)
[Fact] GetLog_BadDays_BadRequest → 0, 91
[Fact] GetLog_ReturnsEntriesWithinDays
[Fact] ChangePassword_Works_AndRaisesConfigChanged; ChangePassword_ShortNew_BadRequest ("abc")
[Fact] GetStatus_ReflectsState → Mode 문자열 "Allowed", AllowUntil
[Fact] StateChanged_RaisedOnAllow_WithSnapshot
```

- [ ] **Step 2: 실패 확인** — Run: `dotnet test` Expected: 컴파일 오류

- [ ] **Step 3: 구현** — 내부에 `GuardStateMachine`, `LoginThrottle` 보유. 세션별 잠금 시각은 `Dictionary<int, DateTimeOffset>`. 모든 public 메서드는 `lock(_gate)` 안에서.

- [ ] **Step 4: 통과 확인** — Run: `dotnet test` Expected: PASS (총 테스트 수 70개 내외)

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(core): GuardEngine - 허용/차단/만료/감지/잠금 판단 로직"
```

---

### Task 10: 서비스 뼈대, set-password, 프로세스 감시

**Files:**
- Create: `src/RobloxGuard.Service/RobloxGuard.Service.csproj` (`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.WindowsServices` 패키지, `OutputType=Exe`)
- Create: `src/RobloxGuard.Service/Program.cs`, `Native/Wts.cs`, `ProcessWatcher.cs`
- Create: `src/RobloxGuard.Service/NullNetworkEnforcer.cs`, `NullLockoutLauncher.cs` (Task 11, 12 전까지 임시. 각각 로그만 남김)

**Interfaces:**
- Consumes: `GuardEngine`, `GuardConfig`, `GuardStateSnapshot`, `AttemptLog`, `RobloxDetector`
- Produces:
  ```csharp
  // Program: args[0]=="set-password" → stdin 한 줄 읽어 GuardConfig.Load(ConfigPath) 에 Hash 설정 후 Save, exit 0 (4자 미만이면 stderr 메시지, exit 2). 그 외 → Host 실행.
  // DI: IClock(SystemClock), GuardConfig(Load), AttemptLog(LogPath), INetworkEnforcer, ILockoutLauncher, GuardEngine(singleton), HostedService: ProcessWatcher
  // engine.StateChanged → snapshot.Save(StatePath); engine.ConfigChanged → config.Save(ConfigPath)
  public static class Wts { public static string? GetSessionUserName(int sessionId); }   // WTSQuerySessionInformation(WTSUserName)
  public sealed class ProcessWatcher : BackgroundService {
    // 1초 루프: engine.Tick(); Process.GetProcesses() → ProcessInfo (Path 는 MainModule.FileName try/catch, CompanyName 은 FileVersionInfo 를 경로별 캐시) → RobloxDetector.IsRoblox(p, config.ExtraProcessNames) → engine.OnRobloxDetected → Kill 이면 Process.Kill(entireProcessTree:true) try/catch
    // PID 0, 4, 자기 자신 제외. 예외는 로그(ILogger)만 남기고 루프 계속.
  }
  ```

- [ ] **Step 1: 프로젝트 생성과 참조**

```powershell
dotnet new worker -o src/RobloxGuard.Service -n RobloxGuard.Service
dotnet sln add src/RobloxGuard.Service
dotnet add src/RobloxGuard.Service reference src/RobloxGuard.Core
dotnet add src/RobloxGuard.Service package Microsoft.Extensions.Hosting.WindowsServices
```
생성된 `Worker.cs` 삭제. csproj 에 `<ServiceName>` 불필요. `UseWindowsService(o => o.ServiceName = "RobloxGuard")`.

- [ ] **Step 2: Program.cs, Wts.cs, ProcessWatcher.cs, Null 구현 작성**

- [ ] **Step 3: 빌드 확인**

Run: `dotnet build src/RobloxGuard.Service`
Expected: `0 Warning(s)` 는 아니어도 `0 Error(s)`

- [ ] **Step 4: set-password 수동 검증** (관리자 PowerShell, 데이터 디렉터리는 테스트용으로 환경변수 `ROBLOXGUARD_DATA_DIR` 로 바꿀 수 있게 `GuardPaths` 에 오버라이드 추가 — Task 1 의 테스트는 오버라이드 없을 때 기준)

```powershell
$env:ROBLOXGUARD_DATA_DIR = "$env:TEMP\rgtest"
"pw1234" | dotnet run --project src/RobloxGuard.Service -- set-password
Get-Content "$env:TEMP\rgtest\config.json"
```
Expected: `passwordHash` 가 아닌 `password: { salt, hash, iterations: 100000 }` 포함.

- [ ] **Step 5: 콘솔 모드 수동 검증** (서비스가 아닌 콘솔로 실행되면 `UseWindowsService` 는 자동으로 콘솔 호스트가 된다)

```powershell
dotnet run --project src/RobloxGuard.Service
```
다른 창에서 메모장을 `RobloxPlayerBeta.exe` 로 복사해 실행 → 1초 안에 꺼지고 `attempts.log` 에 `blocked_kill`, `blocked_lockout_start` 줄 생김. Ctrl+C 로 종료.

- [ ] **Step 6: 커밋**

```bash
git add -A && git commit -m "feat(service): 호스트, set-password, 1초 프로세스 감시·종료"
```

---

### Task 11: Windows 네트워크 차단 (hosts + 방화벽)

**Files:**
- Create: `src/RobloxGuard.Service/WindowsNetworkEnforcer.cs`, `src/RobloxGuard.Service/MaintenanceService.cs`
- Delete: `NullNetworkEnforcer.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WindowsNetworkEnforcer : INetworkEnforcer {
    public WindowsNetworkEnforcer(GuardConfig config, ILogger<WindowsNetworkEnforcer> log);
    public void SetBlocked(bool blocked);        // HostsFileEditor.Apply(GuardPaths.HostsPath, blocked, config.AllBlockedHosts()); ipconfig /flushdns; 방화벽 규칙 enable=yes/no
    public void RefreshFirewallRules();          // C:\Users\*\AppData\Local\Roblox\Versions\*\*.exe 열거 → 규칙 "RobloxGuard-<경로 SHA1 앞 12자>" 없으면 netsh advfirewall firewall add rule dir=out action=block program=<path> enable=(현재 차단 여부); 사라진 경로의 규칙 삭제
    public static void RemoveAllRules();         // netsh ... show rule name=all 파싱해 RobloxGuard- 규칙 전부 delete (uninstall 보조용)
  }
  public sealed class MaintenanceService : BackgroundService {
    // 시작 직후와 60초마다: enforcer.RefreshFirewallRules(); lockoutLauncher.Sweep() (Task 12 이후); 24시간마다: engine 의 log.Prune 은 engine.Start 가 하므로 여기서는 AttemptLog.Prune(now-retention) 직접 호출
  }
  ```

- [ ] **Step 1: 구현** — 모든 외부 명령은 `ProcessStartInfo` + 10초 타임아웃, 실패는 Warning 로그만.

- [ ] **Step 2: 빌드 확인** — Run: `dotnet build` Expected: 0 Error

- [ ] **Step 3: 수동 검증** (관리자 콘솔 모드)

```powershell
dotnet run --project src/RobloxGuard.Service
Get-Content C:\Windows\System32\drivers\etc\hosts | Select-String RobloxGuard
Get-NetFirewallRule -DisplayName "RobloxGuard-*" | Format-Table DisplayName, Enabled
```
Expected: hosts 에 마커 블록과 10개 도메인, 로블록스가 설치된 사용자마다 규칙 존재. Ctrl+C 후에도 hosts 블록은 남아 있음 (의도: fail closed).

- [ ] **Step 4: 커밋**

```bash
git add -A && git commit -m "feat(service): hosts 블록과 방화벽 규칙 적용, 60초 유지보수 루프"
```

---

### Task 12: 잠금 화면 앱과 SYSTEM 세션 실행기

**Files:**
- Create: `src/RobloxGuard.Lockout/RobloxGuard.Lockout.csproj` (WinForms, `OutputType=WinExe`), `Program.cs`, `InputBlocker.cs`, `LockoutForm.cs`, `NativeMethods.cs`
- Create: `src/RobloxGuard.Service/Native/SystemProcessLauncher.cs`, `src/RobloxGuard.Service/WindowsLockoutLauncher.cs`
- Delete: `NullLockoutLauncher.cs`

**Interfaces:**
- Produces:
  ```csharp
  // Lockout Program: 인자 --seconds N (기본 15, 1..60 클램프). Application.Run 전에 System.Threading.Timer (N+3초) → Environment.Exit(0) 비상 종료.
  public sealed class InputBlocker : IDisposable { public InputBlocker(); /* BlockInput(true) + WH_KEYBOARD_LL/WH_MOUSE_LL 훅, 모든 입력 삼킴 */ public void Dispose(); /* UnhookWindowsHookEx, BlockInput(false) */ }
  public sealed class LockoutForm : Form {
    public LockoutForm(Screen screen, int seconds, bool primary);   // FormBorderStyle None, TopMost, Bounds=screen.Bounds, BackColor Black, ShowInTaskbar false
    // primary 만 텍스트: "로블록스 실행이 감지되었습니다" (24pt 흰색), 남은 초 (72pt), "부모님의 허용이 필요합니다" (14pt 회색). Cursor.Hide(). 1초 Timer 로 갱신, 0 이 되면 Application.Exit().
  }
  // Service
  public static class SystemProcessLauncher {
    public static Process? Launch(string exePath, string arguments, int sessionId);   // OpenProcessToken(self) → DuplicateTokenEx(TOKEN_ALL_ACCESS, SecurityIdentification, TokenPrimary) → SetTokenInformation(TokenSessionId) → CreateEnvironmentBlock → CreateProcessAsUser(lpDesktop "winsta0\\default", CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE 아님, CREATE_NO_WINDOW 아님) → Process.GetProcessById
  }
  public sealed class WindowsLockoutLauncher : ILockoutLauncher {
    public WindowsLockoutLauncher(GuardEngine engineForEndCallback 는 순환 → 대신 Action<int> onEnded 를 Program 에서 engine.OnLockoutEnded 로 연결);
    public void Launch(int sessionId, int seconds);   // 세션당 살아 있는 프로세스가 있으면 무시. exe 경로 = AppContext.BaseDirectory + "RobloxGuard.Lockout.exe". Process.Exited → onEnded(sessionId)
    public void Sweep();                               // 시작 후 seconds+5초(최소 20초) 지난 프로세스 Kill
    public void KillAll();                             // 서비스 StopAsync 에서 호출
  }
  ```

- [ ] **Step 1: Lockout 프로젝트 생성**

```powershell
dotnet new winforms -o src/RobloxGuard.Lockout -n RobloxGuard.Lockout
dotnet sln add src/RobloxGuard.Lockout
```
생성된 Form1 관련 파일 삭제. csproj 에 `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`.

- [ ] **Step 2: Lockout 구현 후 단독 수동 검증** (일반 콘솔에서. BlockInput 은 비관리자에서 실패할 수 있으므로 훅만으로도 입력이 막히는지 본다)

Run: `dotnet run --project src/RobloxGuard.Lockout -- --seconds 5`
Expected: 모든 모니터 검정, 카운트다운 5→0, 그동안 Alt+Tab·Win 키·마우스 클릭 무시, 5초 뒤 사라지고 입력 정상. `--seconds 999` → 60초에서 끝남.

- [ ] **Step 3: 서비스 쪽 실행기 구현, Program 에 연결** (`StopAsync` 오버라이드 또는 `IHostApplicationLifetime.ApplicationStopping` 에서 `KillAll`)

- [ ] **Step 4: 빌드 확인** — Run: `dotnet build` Expected: 0 Error

- [ ] **Step 5: 서비스 경유 수동 검증은 Task 15 설치 후 한다** (SYSTEM 토큰 복제는 실제 서비스 컨텍스트에서만 동작한다). 콘솔 모드에서는 `Launch` 가 실패 로그를 남기고 서비스는 계속 도는지만 확인.

- [ ] **Step 6: 커밋**

```bash
git add -A && git commit -m "feat(lockout): 전체 화면 입력 잠금 앱과 SYSTEM 세션 실행기"
```

---

### Task 13: 명명된 파이프 서버

**Files:**
- Create: `src/RobloxGuard.Service/PipeServer.cs`
- Modify: `src/RobloxGuard.Service/Program.cs` (HostedService 등록)

**Interfaces:**
- Consumes: `GuardEngine`, `ProtocolJson`, `PipeRequest/PipeResponse/PipeEvent`
- Produces:
  ```csharp
  public sealed class PipeServer : BackgroundService {
    // 루프: NamedPipeServerStreamAcl.Create(GuardPaths.PipeName, InOut, maxNumberOfServerInstances: 16, Byte, Asynchronous, 0, 0, security)
    //   security: BUILTIN\Users → ReadWrite; NT AUTHORITY\SYSTEM, BUILTIN\Administrators → FullControl
    // 연결마다 Task: 첫 줄 읽기 → ProtocolJson.Deserialize<PipeRequest> (null → Fail(bad_request))
    //   Type 별 engine 호출 → 응답 한 줄 쓰기 → 닫기
    //   Subscribe: Success 한 줄 쓴 뒤 engine.EventRaised 를 구독해 PipeEvent 를 줄로 밀어 보냄. 쓰기 실패(IOException) 시 구독 해제·종료.
    // 모든 예외는 로그 후 루프 계속.
  }
  ```

- [ ] **Step 1: 구현**

- [ ] **Step 2: 빌드 확인** — Run: `dotnet build` Expected: 0 Error

- [ ] **Step 3: 수동 검증** (콘솔 모드로 서비스 실행 후 PowerShell 에서)

```powershell
$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'RobloxGuard', 'InOut'); $p.Connect(2000)
$w = New-Object System.IO.StreamWriter($p); $w.AutoFlush=$true; $r = New-Object System.IO.StreamReader($p)
$w.WriteLine('{"type":"GetStatus"}'); $r.ReadLine()
```
Expected: `{"ok":true,"mode":"Blocked",...}`. 같은 방식으로 `{"type":"Allow","password":"pw1234","minutes":1}` → ok, hosts 블록 사라짐, 1분 뒤 `blocked` 로 복귀하고 블록 재생성. 틀린 비밀번호 5회 → `locked`.

- [ ] **Step 4: 커밋**

```bash
git add -A && git commit -m "feat(service): 명명된 파이프 서버와 이벤트 구독"
```

---

### Task 14: 트레이 앱

**Files:**
- Create: `src/RobloxGuard.Tray/RobloxGuard.Tray.csproj` (WinForms, WinExe), `Program.cs`, `Strings.cs`, `ServiceConnection.cs`, `IconFactory.cs`, `TrayContext.cs`, `Forms/PasswordDialog.cs`, `Forms/AllowDialog.cs`, `Forms/ChangePasswordDialog.cs`, `Forms/LogViewerForm.cs`

**Interfaces:**
- Consumes: `ProtocolJson`, 메시지 타입, `AllowDurations`, `LogSummary`, `GuardPaths.PipeName`
- Produces:
  ```csharp
  // Program: Mutex "Local\RobloxGuard.Tray" 로 세션당 하나. Application.Run(new TrayContext()).
  public static class Strings { /* 아래 문구 전부 상수 */ }
  //  AppName="RobloxGuard", StatusBlocked="상태: 차단 중", StatusAllowedFmt="상태: 허용 중 ({0}분 남음)", StatusDisconnected="서비스에 연결할 수 없음",
  //  MenuAllow="허용하기…", MenuBlockNow="지금 차단하기", MenuLog="기록 보기…", MenuChangePassword="비밀번호 변경…", MenuAbout="정보",
  //  PasswordPrompt="부모 비밀번호를 입력하세요", PasswordHint="가급적 부모 계정으로 로그인해서 입력하세요", PasswordHintStrong="⚠ 지금은 일반 계정입니다. 부모 계정에서 입력하는 것이 안전합니다",
  //  Allow30="30분", Allow60="1시간", Allow120="2시간", AllowToday="오늘 하루",
  //  ErrWrongPassword="비밀번호가 틀렸습니다", ErrLockedFmt="시도가 너무 많습니다. {0}분 뒤 다시 시도하세요", ErrNotConfigured="비밀번호가 설정되지 않았습니다. 설치 스크립트를 다시 실행하세요", ErrDisconnected="서비스에 연결할 수 없습니다",
  //  ExpiringFmt="{0}분 뒤 로블록스가 종료됩니다", LogTitle="시도 기록", LogDayFmt="{0:M월 d일 (ddd)} 시도 {1}회", LogShow30="30일 보기", LogEmpty="기록이 없습니다",
  //  About="RobloxGuard 1.0\n부모가 풀어주기 전까지 로블록스를 차단합니다."
  public sealed class ServiceConnection : IDisposable {
    public bool IsConnected { get; }
    public event Action<PipeResponse?>? StatusUpdated;     // 2초마다 GetStatus 결과 (실패 시 null)
    public event Action<PipeEvent>? EventReceived;         // Subscribe 연결에서 수신. 끊기면 3초 뒤 재연결
    public void Start();
    public Task<PipeResponse> SendAsync(PipeRequest req);  // 연결 실패 시 Fail("disconnected")
  }
  public static class IconFactory { public static Icon Locked(); public static Icon Unlocked(); public static Icon Disconnected(); }   // 16x16 GDI, 자물쇠 모양, 색: 빨강/초록/회색
  public sealed class TrayContext : ApplicationContext { /* NotifyIcon + ContextMenuStrip, 메뉴는 열릴 때마다 상태로 재구성. 허용 중이면 MenuBlockNow 표시. expiring 이벤트 → ShowBalloonTip(ExpiringFmt). */ }
  public sealed class PasswordDialog : Form { public static string? Ask(IWin32Window? owner, string title); }   // 힌트 문구는 WindowsPrincipal.IsInRole(Administrator) 로 분기
  public sealed class AllowDialog : Form { public static int? AskMinutes(IWin32Window? owner); }   // 30/60/120 또는 (int)Math.Ceiling(AllowDurations.UntilEndOfDay(now, TimeZoneInfo.Local).TotalMinutes)
  public sealed class ChangePasswordDialog : Form { public static (string old, string @new)? Ask(IWin32Window? owner); }   // 새 비밀번호 두 번 입력 일치 확인, 4자 이상
  public sealed class LogViewerForm : Form { public LogViewerForm(IReadOnlyList<AttemptEntry> entries, Func<int, Task<IReadOnlyList<AttemptEntry>?>> reload); }   // TreeView: 날짜 노드(LogDayFmt) → 자식 "HH:mm  프로세스  (세션)" 는 blocked_lockout_start 와 wrong_password, password_lockout, allow_start, allow_end, block_manual 만 표시(blocked_kill 은 숨김). 체크박스 LogShow30 → reload(30)
  ```
  허용 흐름: `MenuAllow` → `PasswordDialog.Ask` → `AllowDialog.AskMinutes` → `SendAsync(Allow)` → 오류면 `MessageBox` (Err*). 기록 흐름: 비밀번호 → `SendAsync(GetLog days 7)` → `LogViewerForm`.

- [ ] **Step 1: 프로젝트 생성**

```powershell
dotnet new winforms -o src/RobloxGuard.Tray -n RobloxGuard.Tray
dotnet sln add src/RobloxGuard.Tray
dotnet add src/RobloxGuard.Tray reference src/RobloxGuard.Core
```

- [ ] **Step 2: 구현** (Strings → ServiceConnection → IconFactory → TrayContext → Forms 순)

- [ ] **Step 3: 빌드 확인** — Run: `dotnet build` Expected: 0 Error

- [ ] **Step 4: 수동 검증** (콘솔 모드 서비스 + `dotnet run --project src/RobloxGuard.Tray`)
  - 아이콘 빨강, 메뉴 상태 "차단 중".
  - 허용하기 → 틀린 비밀번호 → "비밀번호가 틀렸습니다". 맞는 비밀번호 → 30분 → 아이콘 초록, 툴팁 "허용 중 (29분 남음)" 전후, hosts 블록 사라짐.
  - 지금 차단하기 → 빨강.
  - 1분 허용 뒤(파이프로 직접 Allow minutes 6) 5분 전·1분 전 풍선 알림.
  - 기록 보기 → 날짜 노드와 시도 횟수.
  - 서비스 Ctrl+C → 아이콘 회색, 재시작 → 3초 안에 복구.

- [ ] **Step 5: 커밋**

```bash
git add -A && git commit -m "feat(tray): 트레이 메뉴, 허용/차단, 기록 보기, 비밀번호 변경"
```

---

### Task 15: 빌드·설치·제거 스크립트와 실제 설치 검증

**Files:**
- Create: `scripts/build.ps1`, `scripts/install.ps1`, `scripts/uninstall.ps1`

**Interfaces:**
- `build.ps1`: 세 실행 프로젝트를 `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/` 로 발행하고 `scripts/install.ps1`, `uninstall.ps1`, `README.md` 를 `dist/` 로 복사. 끝에 `dotnet test` 실행.
- `install.ps1` (dist 안에서 실행, 관리자 아니면 메시지 후 exit 1):
  1. `Get-Service RobloxGuard` 있으면 `Stop-Service`; `RobloxGuard.Tray`, `RobloxGuard.Lockout` 프로세스 종료.
  2. `C:\Program Files\RobloxGuard\` 에 exe 복사.
  3. `C:\ProgramData\RobloxGuard\` 생성; `icacls <dir> /inheritance:r /grant "SYSTEM:(OI)(CI)F" /grant "Administrators:(OI)(CI)F"`.
  4. config.json 에 password 가 이미 있으면 "비밀번호를 재설정하시겠습니까? (y/N)". 설정 시 `Read-Host -AsSecureString` 두 번, 불일치·4자 미만이면 반복; 평문을 stdin 으로 `RobloxGuard.Service.exe set-password` 에 전달.
  5. 서비스 없으면 `sc.exe create RobloxGuard binPath= "<exe>" start= auto obj= LocalSystem DisplayName= "RobloxGuard"`; `sc.exe description RobloxGuard "로블록스 차단 서비스"`; `sc.exe failure RobloxGuard reset= 86400 actions= restart/1000/restart/1000/restart/1000`; `Start-Service`.
  6. `HKLM:\Software\Microsoft\Windows\CurrentVersion\Run` 에 `RobloxGuard.Tray` = 트레이 exe 경로; 현재 세션에서 트레이 실행.
  7. 출력: 설치 완료, 아이 계정이 일반 사용자인지 확인하는 법 (`설정 > 계정 > 가족 및 다른 사용자 > 계정 유형 변경 > 표준 사용자`), 현재 로컬 Administrators 그룹 구성원 목록(`Get-LocalGroupMember Administrators`).
- `uninstall.ps1` (관리자): `Stop-Service`, `sc.exe delete`, 트레이·잠금 프로세스 종료, hosts 에서 마커 블록 제거(정규식) + `ipconfig /flushdns`, `Get-NetFirewallRule -DisplayName "RobloxGuard-*" | Remove-NetFirewallRule`, Run 키 삭제, Program Files 디렉터리 삭제, "기록을 삭제할까요? (y/N)" 에 따라 ProgramData 삭제.

- [ ] **Step 1: 세 스크립트 작성**

- [ ] **Step 2: 빌드**

Run: `pwsh -File scripts/build.ps1`
Expected: `dist/` 에 `RobloxGuard.Service.exe`, `RobloxGuard.Tray.exe`, `RobloxGuard.Lockout.exe`, 스크립트, README. 테스트 전부 PASS.

- [ ] **Step 3: 실제 설치 검증** (관리자 PowerShell. 이 PC에 실제로 서비스가 설치되고 로블록스가 차단된다.)

```powershell
cd dist; .\install.ps1
Get-Service RobloxGuard          # Running
Get-Content C:\Windows\System32\drivers\etc\hosts | Select-String RobloxGuard
```
- 메모장을 `RobloxPlayerBeta.exe` 로 복사해 실행 → 즉시 종료, **검은 잠금 화면 15초**, 카운트다운, 입력 막힘, 15초 뒤 해제. 이것이 가장 중요한 검증이다.
- 잠금 중 작업 관리자(Ctrl+Shift+Esc) 안 열림. Ctrl+Alt+Del 은 열리지만 돌아오면 잠금 유지.
- 잠금 중 다시 `RobloxPlayerBeta.exe` 실행 → 종료만 되고 잠금 연장 없음.
- 일반 사용자 계정으로 로그인: 트레이 아이콘 자동 실행, `services.msc` 에서 RobloxGuard 중지 불가, `C:\Program Files\RobloxGuard` 삭제 불가, `C:\ProgramData\RobloxGuard` 접근 거부.
- 부모 계정에서 허용 30분 → 아이 계정에서 `RobloxPlayerBeta.exe` 유지됨 → 지금 차단하기 → 종료됨.
- `Stop-Service RobloxGuard` 중에 잠금이 떠 있었다면 즉시 사라짐.
- `.\uninstall.ps1` → 서비스·hosts 블록·방화벽 규칙·Run 키 전부 제거.

- [ ] **Step 4: 발견된 문제 수정 후 커밋**

```bash
git add -A && git commit -m "feat: 빌드/설치/제거 스크립트"
```

---

### Task 16: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: 작성** — 다음 절을 포함: 무엇을 하는가(설계 2절 요약), 설치 전 준비(아이 계정을 일반 사용자로 바꾸는 법, 스크린샷 없이 경로 텍스트), 설치(`build.ps1` → `install.ps1`), 사용법(트레이 메뉴 각 항목), 비밀번호를 잊었을 때, 제거, **알려진 한계**(설계 8절 그대로), **수동 테스트 체크리스트**(Task 15 Step 3 항목을 체크박스로, "15초 뒤 반드시 풀리는지"와 "서비스 중지 시 즉시 풀리는지"를 맨 위에).

- [ ] **Step 2: 커밋**

```bash
git add -A && git commit -m "docs: README - 설치, 사용법, 한계, 수동 테스트 체크리스트"
```

---

## 자체 검토 메모

- 설계 5.1 의 "서비스 자신·트레이·PID 0/4 제외" → Task 4 (SelfPrefix) + Task 10.
- 설계 5.1 "허용 중 Allow 교체" → Task 3, 9. "만료 시 로블록스 종료" → Task 10 의 1초 루프가 모드 복귀 직후 종료 (≤1초).
- 설계 5.3 안전장치 세 겹 → Lockout 자체 타이머 + 비상 Exit 타이머(Task 12), BlockInput 자동 해제(OS), 서비스 Sweep 20초(Task 11/12), 서비스 중지 시 KillAll(Task 12).
- 설계 5.2 "종료 메뉴 없음" → Task 14 메뉴 목록에 없음.
- 설계 5.4 설치 7단계 → Task 15.
- `GuardPaths` 의 `ROBLOXGUARD_DATA_DIR` 오버라이드는 Task 10 에서 추가하며, 설치된 서비스는 환경변수가 없으므로 기본 경로를 쓴다.
