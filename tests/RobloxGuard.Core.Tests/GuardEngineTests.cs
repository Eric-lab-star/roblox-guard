using RobloxGuard.Core.Config;
using RobloxGuard.Core.Detection;
using RobloxGuard.Core.Engine;
using RobloxGuard.Core.Logging;
using RobloxGuard.Core.Protocol;
using RobloxGuard.Core.Security;
using RobloxGuard.Core.State;

namespace RobloxGuard.Core.Tests;

public sealed class FakeNetwork : INetworkEnforcer
{
    public List<bool> Calls { get; } = new();
    public void SetBlocked(bool blocked) => Calls.Add(blocked);
}

public sealed class FakeLockout : ILockoutLauncher
{
    public List<(int SessionId, int Seconds)> Calls { get; } = new();
    public void Launch(int sessionId, int seconds) => Calls.Add((sessionId, seconds));
}

public class GuardEngineTests : IDisposable
{
    private const string Pw = "pw1234";
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("rg-engine-");
    private readonly FakeClock _clock = new();
    private readonly FakeNetwork _network = new();
    private readonly FakeLockout _lockout = new();
    private readonly AttemptLog _log;
    private readonly List<PipeEvent> _events = new();
    private readonly List<GuardStateSnapshot> _snapshots = new();
    private GuardConfig _config;

    public GuardEngineTests()
    {
        _log = new AttemptLog(Path.Combine(_dir.FullName, "attempts.log"));
        _config = new GuardConfig { Password = PasswordHasher.Hash(Pw) };
    }

    public void Dispose() => _dir.Delete(true);

    private GuardEngine Make(GuardStateSnapshot? initial = null, bool start = true)
    {
        var e = new GuardEngine(_clock, _config, initial ?? GuardStateSnapshot.Default, _log, _network, _lockout);
        e.EventRaised += ev => _events.Add(ev);
        e.StateChanged += s => _snapshots.Add(s);
        if (start) e.Start();
        return e;
    }

    private IReadOnlyList<AttemptEntry> Log() => _log.ReadSince(DateTimeOffset.MinValue);
    private bool Logged(string type) => Log().Any(e => e.Type == type);

    private static ProcessInfo Roblox(int session = 2, string name = "RobloxPlayerBeta")
        => new(4242, name, @"C:\Users\kid\AppData\Local\Roblox\Versions\v1\" + name + ".exe", "Roblox Corporation", session);

    // ── Start ──────────────────────────────────────────────────────────

    [Fact]
    public void Start_Blocked_AppliesNetworkBlock()
    {
        Make();
        Assert.Equal(new[] { true }, _network.Calls);
        Assert.True(Logged(AttemptTypes.ServiceStart));
    }

    [Fact]
    public void Start_WithPastAllowUntil_ExpiresImmediately()
    {
        var e = Make(new GuardStateSnapshot(GuardMode.Allowed, _clock.UtcNow.AddMinutes(-1), 0, null));
        Assert.Equal(GuardMode.Blocked, e.Mode);
        Assert.True(_network.Calls.Last());
        Assert.True(Logged(AttemptTypes.AllowEnd));
    }

    [Fact]
    public void Start_WithFutureAllowUntil_StaysAllowed()
    {
        var e = Make(new GuardStateSnapshot(GuardMode.Allowed, _clock.UtcNow.AddMinutes(10), 0, null));
        Assert.Equal(GuardMode.Allowed, e.Mode);
        Assert.Equal(new[] { false }, _network.Calls);
    }

    [Fact]
    public void Start_PrunesOldLogEntries()
    {
        _log.Append(new AttemptEntry(_clock.UtcNow.AddDays(-100), AttemptTypes.BlockedKill, null, null, null, null));
        Make();
        Assert.DoesNotContain(Log(), x => x.Ts == _clock.UtcNow.AddDays(-100));
    }

    // ── Allow ──────────────────────────────────────────────────────────

    [Fact]
    public void Allow_CorrectPassword_Unblocks()
    {
        var e = Make();
        var r = e.Allow(Pw, 60);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(GuardMode.Allowed, e.Mode);
        Assert.Equal(_clock.UtcNow.AddMinutes(60), r.AllowUntil);
        Assert.False(_network.Calls.Last());
        Assert.Contains(_events, ev => ev.Event == EventNames.ModeChanged && ev.Mode == "Allowed");
        Assert.True(Logged(AttemptTypes.AllowStart));
    }

    [Fact]
    public void Allow_WrongPassword_Rejected()
    {
        var e = Make();
        var r = e.Allow("nope", 60);
        Assert.False(r.Ok);
        Assert.Equal(ErrorCodes.WrongPassword, r.Error);
        Assert.Equal(GuardMode.Blocked, e.Mode);
        Assert.True(Logged(AttemptTypes.WrongPassword));
    }

    [Fact]
    public void Allow_FiveWrong_Locks_ThenCorrectRejected()
    {
        var e = Make();
        PipeResponse? last = null;
        for (var i = 0; i < 5; i++) last = e.Allow("nope", 60);
        Assert.Equal(ErrorCodes.WrongPassword, last!.Error);
        Assert.True(Logged(AttemptTypes.PasswordLockout));

        var r = e.Allow(Pw, 60);
        Assert.Equal(ErrorCodes.Locked, r.Error);
        Assert.Equal(_clock.UtcNow.AddMinutes(5), r.LockedUntil);
        Assert.Equal(GuardMode.Blocked, e.Mode);
    }

    [Fact]
    public void Allow_LockExpires_ThenCorrectWorks()
    {
        var e = Make();
        for (var i = 0; i < 5; i++) e.Allow("nope", 60);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(e.Allow(Pw, 60).Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    [InlineData(null)]
    public void Allow_BadMinutes_BadRequest(int? minutes)
    {
        var e = Make();
        var before = _network.Calls.Count;
        var r = e.Allow(Pw, minutes);
        Assert.Equal(ErrorCodes.BadRequest, r.Error);
        Assert.Equal(GuardMode.Blocked, e.Mode);
        Assert.Equal(before, _network.Calls.Count);
    }

    [Fact]
    public void Allow_NotConfigured_ReturnsNotConfigured()
    {
        _config = new GuardConfig();
        var e = Make();
        Assert.Equal(ErrorCodes.NotConfigured, e.Allow("anything", 60).Error);
        Assert.Equal(GuardMode.Blocked, e.Mode);
    }

    [Fact]
    public void Allow_Replaces()
    {
        var e = Make();
        e.Allow(Pw, 60);
        _clock.Advance(TimeSpan.FromMinutes(30));
        var r = e.Allow(Pw, 30);
        Assert.Equal(_clock.UtcNow.AddMinutes(30), r.AllowUntil);
    }

    // ── Block ──────────────────────────────────────────────────────────

    [Fact]
    public void Block_WhenAllowed_Reblocks()
    {
        var e = Make();
        e.Allow(Pw, 60);
        var r = e.Block();
        Assert.True(r.Ok);
        Assert.Equal(GuardMode.Blocked, e.Mode);
        Assert.True(_network.Calls.Last());
        Assert.True(Logged(AttemptTypes.BlockManual));
        Assert.Contains(_events, ev => ev.Event == EventNames.ModeChanged && ev.Mode == "Blocked");
    }

    [Fact]
    public void Block_WhenBlocked_OkNoLog()
    {
        var e = Make();
        Assert.True(e.Block().Ok);
        Assert.False(Logged(AttemptTypes.BlockManual));
    }

    // ── Tick ───────────────────────────────────────────────────────────

    [Fact]
    public void Tick_Expiry_Reblocks()
    {
        var e = Make();
        e.Allow(Pw, 30);
        _clock.Advance(TimeSpan.FromMinutes(30));
        e.Tick();
        Assert.Equal(GuardMode.Blocked, e.Mode);
        Assert.True(_network.Calls.Last());
        Assert.True(Logged(AttemptTypes.AllowEnd));
        Assert.Contains(_events, ev => ev.Event == EventNames.ModeChanged && ev.Mode == "Blocked");
    }

    [Fact]
    public void Tick_Warnings_RaiseExpiringEvents()
    {
        var e = Make();
        e.Allow(Pw, 30);
        _clock.Advance(TimeSpan.FromMinutes(25));
        e.Tick();
        Assert.Contains(_events, ev => ev.Event == EventNames.Expiring && ev.Minutes == 5);
        _clock.Advance(TimeSpan.FromMinutes(4));
        e.Tick();
        Assert.Contains(_events, ev => ev.Event == EventNames.Expiring && ev.Minutes == 1);
    }

    // ── Detection ──────────────────────────────────────────────────────

    [Fact]
    public void Detected_WhileBlocked_KillsAndLocks()
    {
        var e = Make();
        var d = e.OnRobloxDetected(Roblox(session: 2), "kid");
        Assert.Equal(new DetectionDecision(true, true), d);
        Assert.Equal(new[] { (2, 15) }, _lockout.Calls);
        Assert.True(Logged(AttemptTypes.BlockedKill));
        Assert.True(Logged(AttemptTypes.LockoutStart));
        Assert.Contains(_events, ev => ev.Event == EventNames.Blocked && ev.Process == "RobloxPlayerBeta");
        var kill = Log().First(x => x.Type == AttemptTypes.BlockedKill);
        Assert.Equal("kid", kill.Session);
        Assert.Equal("RobloxPlayerBeta", kill.Process);
    }

    [Fact]
    public void Detected_Again_Within10sAfterLockout_KillsWithoutLock()
    {
        var e = Make();
        e.OnRobloxDetected(Roblox(), "kid");
        _clock.Advance(TimeSpan.FromSeconds(20)); // 15초 잠금 + 5초
        Assert.Equal(new DetectionDecision(true, false), e.OnRobloxDetected(Roblox(), "kid"));
        _clock.Advance(TimeSpan.FromSeconds(6)); // 총 26초 ≥ 15 + 10
        Assert.Equal(new DetectionDecision(true, true), e.OnRobloxDetected(Roblox(), "kid"));
        Assert.Equal(2, _lockout.Calls.Count);
    }

    [Fact]
    public void Detected_DifferentSession_GetsOwnLockout()
    {
        var e = Make();
        e.OnRobloxDetected(Roblox(session: 2), "kid");
        e.OnRobloxDetected(Roblox(session: 3), "kid2");
        Assert.Equal(new[] { (2, 15), (3, 15) }, _lockout.Calls);
    }

    [Fact]
    public void Detected_WhileAllowed_NoKill()
    {
        var e = Make();
        e.Allow(Pw, 60);
        var before = Log().Count;
        Assert.Equal(new DetectionDecision(false, false), e.OnRobloxDetected(Roblox(), "kid"));
        Assert.Empty(_lockout.Calls);
        Assert.Equal(before, Log().Count);
    }

    [Fact]
    public void Detected_NotConfigured_StillKills()
    {
        _config = new GuardConfig();
        var e = Make();
        Assert.Equal(new DetectionDecision(true, true), e.OnRobloxDetected(Roblox(), "kid"));
    }

    [Fact]
    public void Detected_UsesConfiguredLockoutSeconds()
    {
        _config.LockoutSeconds = 20;
        var e = Make();
        e.OnRobloxDetected(Roblox(), "kid");
        Assert.Equal(new[] { (2, 20) }, _lockout.Calls);
    }

    [Fact]
    public void OnLockoutEnded_LogsEnd()
    {
        var e = Make();
        e.OnRobloxDetected(Roblox(), "kid");
        e.OnLockoutEnded(2);
        Assert.True(Logged(AttemptTypes.LockoutEnd));
    }

    // ── GetLog ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    [InlineData(null)]
    public void GetLog_BadDays_BadRequest(int? days)
        => Assert.Equal(ErrorCodes.BadRequest, Make().GetLog(Pw, days).Error);

    [Fact]
    public void GetLog_WrongPassword_Rejected()
        => Assert.Equal(ErrorCodes.WrongPassword, Make().GetLog("nope", 7).Error);

    [Fact]
    public void GetLog_ReturnsEntriesWithinDays()
    {
        _log.Append(new AttemptEntry(_clock.UtcNow.AddDays(-10), AttemptTypes.LockoutStart, null, null, null, null));
        _log.Append(new AttemptEntry(_clock.UtcNow.AddDays(-1), AttemptTypes.LockoutStart, null, null, null, null));
        var r = Make().GetLog(Pw, 7);
        Assert.True(r.Ok);
        Assert.Contains(r.Entries!, x => x.Type == AttemptTypes.LockoutStart && x.Ts == _clock.UtcNow.AddDays(-1));
        Assert.DoesNotContain(r.Entries!, x => x.Ts == _clock.UtcNow.AddDays(-10));
    }

    // ── ChangePassword ─────────────────────────────────────────────────

    [Fact]
    public void ChangePassword_Works_AndRaisesConfigChanged()
    {
        var e = Make();
        GuardConfig? changed = null;
        e.ConfigChanged += c => changed = c;
        Assert.True(e.ChangePassword(Pw, "newpw99").Ok);
        Assert.NotNull(changed);
        Assert.True(PasswordHasher.Verify("newpw99", changed!.Password!));
        Assert.True(Logged(AttemptTypes.PasswordChanged));
        Assert.True(e.Allow("newpw99", 30).Ok);
    }

    [Fact]
    public void ChangePassword_ShortNew_BadRequest()
        => Assert.Equal(ErrorCodes.BadRequest, Make().ChangePassword(Pw, "abc").Error);

    [Fact]
    public void ChangePassword_WrongOld_Rejected()
        => Assert.Equal(ErrorCodes.WrongPassword, Make().ChangePassword("nope", "newpw99").Error);

    // ── Status / State ─────────────────────────────────────────────────

    [Fact]
    public void GetStatus_ReflectsState()
    {
        var e = Make();
        Assert.Equal("Blocked", e.GetStatus().Mode);
        e.Allow(Pw, 60);
        var s = e.GetStatus();
        Assert.True(s.Ok);
        Assert.Equal("Allowed", s.Mode);
        Assert.Equal(_clock.UtcNow.AddMinutes(60), s.AllowUntil);
    }

    [Fact]
    public void StateChanged_RaisedOnAllow_WithSnapshot()
    {
        var e = Make();
        e.Allow(Pw, 60);
        var snap = _snapshots.Last();
        Assert.Equal(GuardMode.Allowed, snap.Mode);
        Assert.Equal(_clock.UtcNow.AddMinutes(60), snap.AllowUntil);
    }

    [Fact]
    public void StateChanged_RaisedOnWrongPassword_WithFailedAttempts()
    {
        var e = Make();
        e.Allow("nope", 60);
        Assert.Equal(1, _snapshots.Last().FailedAttempts);
    }

    [Fact]
    public void Restore_LockedState_StillLocked()
    {
        var e = Make(new GuardStateSnapshot(GuardMode.Blocked, null, 5, _clock.UtcNow.AddMinutes(3)));
        Assert.Equal(ErrorCodes.Locked, e.Allow(Pw, 60).Error);
    }
}
