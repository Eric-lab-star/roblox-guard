using RobloxGuard.Core.Config;
using RobloxGuard.Core.Detection;
using RobloxGuard.Core.Logging;
using RobloxGuard.Core.Protocol;
using RobloxGuard.Core.Security;
using RobloxGuard.Core.State;

namespace RobloxGuard.Core.Engine;

/// <summary>hosts/방화벽 같은 네트워크 차단의 적용·해제.</summary>
public interface INetworkEnforcer
{
    void SetBlocked(bool blocked);
}

/// <summary>특정 세션에 잠금 화면을 띄운다.</summary>
public interface ILockoutLauncher
{
    void Launch(int sessionId, int seconds);
}

public sealed record DetectionDecision(bool Kill, bool LockoutStarted);

/// <summary>
/// 모든 판단 로직. 서비스는 이 클래스를 호출만 한다.
/// 모든 public 메서드는 단일 락 아래에서 실행된다.
/// </summary>
public sealed class GuardEngine
{
    private const int MinPasswordLength = 4;
    private const int MaxLogDays = 90;
    private static readonly TimeSpan LockoutDebounce = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly AttemptLog _log;
    private readonly INetworkEnforcer _network;
    private readonly ILockoutLauncher _lockout;
    private readonly GuardStateMachine _state;
    private readonly LoginThrottle _throttle;
    private readonly Dictionary<int, DateTimeOffset> _lockoutStartedAt = new();

    public GuardEngine(IClock clock, GuardConfig config, GuardStateSnapshot initial, AttemptLog log,
        INetworkEnforcer network, ILockoutLauncher lockout)
    {
        _clock = clock;
        Config = config;
        _log = log;
        _network = network;
        _lockout = lockout;
        _state = new GuardStateMachine(clock);
        _state.Restore(initial.Mode, initial.AllowUntil);
        _throttle = new LoginThrottle(clock);
        _throttle.Restore(initial.FailedAttempts, initial.LockedUntil);
    }

    public GuardConfig Config { get; }
    public GuardMode Mode { get { lock (_gate) return _state.Mode; } }

    public event Action<PipeEvent>? EventRaised;
    public event Action<GuardStateSnapshot>? StateChanged;
    public event Action<GuardConfig>? ConfigChanged;

    public void Start()
    {
        lock (_gate)
        {
            Append(AttemptTypes.ServiceStart, detail: _state.Mode.ToString());
            _log.Prune(_clock.UtcNow.AddDays(-Config.LogRetentionDays));
            // 과거 allowUntil 이면 즉시 만료 처리 (allow_end 기록 + 차단)
            HandleStateEvents(_state.Tick());
            _network.SetBlocked(_state.Mode == GuardMode.Blocked);
        }
    }

    public PipeResponse GetStatus()
    {
        lock (_gate) return StatusUnlocked();
    }

    public PipeResponse Allow(string? password, int? minutes)
    {
        lock (_gate)
        {
            if (CheckPassword(password) is { } denied) return denied;
            if (minutes is null || minutes <= 0 || minutes > AllowDurations.MaxMinutes)
                return PipeResponse.Fail(ErrorCodes.BadRequest);

            var ev = _state.Allow(TimeSpan.FromMinutes(minutes.Value));
            Append(AttemptTypes.AllowStart, detail: minutes.Value.ToString());
            _network.SetBlocked(false);
            RaiseModeChanged(ev);
            RaiseStateChanged();
            return StatusUnlocked();
        }
    }

    public PipeResponse Block()
    {
        lock (_gate)
        {
            var ev = _state.Block();
            if (ev is not null)
            {
                Append(AttemptTypes.BlockManual);
                _network.SetBlocked(true);
                RaiseModeChanged(ev);
                RaiseStateChanged();
            }
            return StatusUnlocked();
        }
    }

    public PipeResponse GetLog(string? password, int? days)
    {
        lock (_gate)
        {
            if (CheckPassword(password) is { } denied) return denied;
            if (days is null || days < 1 || days > MaxLogDays) return PipeResponse.Fail(ErrorCodes.BadRequest);
            var r = PipeResponse.Success();
            r.Entries = _log.ReadSince(_clock.UtcNow.AddDays(-days.Value)).ToList();
            return r;
        }
    }

    public PipeResponse ChangePassword(string? oldPassword, string? newPassword)
    {
        lock (_gate)
        {
            if (CheckPassword(oldPassword) is { } denied) return denied;
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
                return PipeResponse.Fail(ErrorCodes.BadRequest);
            Config.Password = PasswordHasher.Hash(newPassword);
            Append(AttemptTypes.PasswordChanged);
            ConfigChanged?.Invoke(Config);
            return PipeResponse.Success();
        }
    }

    /// <summary>1초마다 호출. 만료와 경고를 처리한다.</summary>
    public void Tick()
    {
        lock (_gate) HandleStateEvents(_state.Tick());
    }

    public DetectionDecision OnRobloxDetected(ProcessInfo p, string? sessionUser)
    {
        lock (_gate)
        {
            if (_state.Mode == GuardMode.Allowed) return new DetectionDecision(false, false);

            Append(AttemptTypes.BlockedKill, sessionUser, p.Name, p.Path);
            EventRaised?.Invoke(new PipeEvent { Event = EventNames.Blocked, Process = p.Name });

            var now = _clock.UtcNow;
            var seconds = Config.LockoutSeconds;
            if (_lockoutStartedAt.TryGetValue(p.SessionId, out var last) &&
                now < last + TimeSpan.FromSeconds(seconds) + LockoutDebounce)
            {
                return new DetectionDecision(true, false);
            }

            _lockoutStartedAt[p.SessionId] = now;
            _lockout.Launch(p.SessionId, seconds);
            Append(AttemptTypes.LockoutStart, sessionUser, p.Name, p.Path, seconds.ToString());
            return new DetectionDecision(true, true);
        }
    }

    public void OnLockoutEnded(int sessionId)
    {
        lock (_gate) Append(AttemptTypes.LockoutEnd, detail: sessionId.ToString());
    }

    // ── 내부 ───────────────────────────────────────────────────────────

    /// <returns>거절 응답, 통과면 null.</returns>
    private PipeResponse? CheckPassword(string? password)
    {
        if (!Config.IsConfigured) return PipeResponse.Fail(ErrorCodes.NotConfigured);
        if (_throttle.IsLocked)
        {
            var r = PipeResponse.Fail(ErrorCodes.Locked);
            r.LockedUntil = _throttle.LockedUntil;
            return r;
        }
        if (password is null || !PasswordHasher.Verify(password, Config.Password!))
        {
            var locked = _throttle.RecordFailure();
            Append(AttemptTypes.WrongPassword, detail: _throttle.FailedAttempts.ToString());
            if (locked) Append(AttemptTypes.PasswordLockout, detail: _throttle.LockedUntil?.ToString("O"));
            RaiseStateChanged();
            return PipeResponse.Fail(ErrorCodes.WrongPassword);
        }
        _throttle.RecordSuccess();
        return null;
    }

    private void HandleStateEvents(IReadOnlyList<GuardEvent> events)
    {
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case GuardEventKind.ModeChanged when ev.Mode == GuardMode.Blocked:
                    Append(AttemptTypes.AllowEnd);
                    _network.SetBlocked(true);
                    RaiseModeChanged(ev);
                    RaiseStateChanged();
                    break;
                case GuardEventKind.Expiring:
                    EventRaised?.Invoke(new PipeEvent { Event = EventNames.Expiring, Minutes = ev.MinutesLeft, AllowUntil = ev.AllowUntil });
                    break;
            }
        }
    }

    private void RaiseModeChanged(GuardEvent ev)
        => EventRaised?.Invoke(new PipeEvent { Event = EventNames.ModeChanged, Mode = ev.Mode.ToString(), AllowUntil = ev.AllowUntil });

    private void RaiseStateChanged()
        => StateChanged?.Invoke(new GuardStateSnapshot(_state.Mode, _state.AllowUntil, _throttle.FailedAttempts, _throttle.LockedUntil));

    private PipeResponse StatusUnlocked()
    {
        var r = PipeResponse.Success();
        r.Mode = _state.Mode.ToString();
        r.AllowUntil = _state.AllowUntil;
        r.LockedUntil = _throttle.IsLocked ? _throttle.LockedUntil : null;
        return r;
    }

    private void Append(string type, string? session = null, string? process = null, string? path = null, string? detail = null)
        => _log.Append(new AttemptEntry(_clock.UtcNow, type, session, process, path, detail));
}
