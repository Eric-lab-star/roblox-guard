namespace RobloxGuard.Core.State;

public enum GuardMode { Blocked, Allowed }

public enum GuardEventKind { ModeChanged, Expiring }

public sealed record GuardEvent(GuardEventKind Kind, GuardMode Mode, DateTimeOffset? AllowUntil, int? MinutesLeft);

/// <summary>
/// 차단 ↔ 허용 상태 기계. 허용은 교체(누적 아님). 만료 전 5분/1분 경고는 한 번씩만.
/// </summary>
public sealed class GuardStateMachine
{
    private readonly IClock _clock;
    private readonly List<int> _pendingWarnings = new();

    public GuardStateMachine(IClock clock) => _clock = clock;

    public GuardMode Mode { get; private set; } = GuardMode.Blocked;
    public DateTimeOffset? AllowUntil { get; private set; }

    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(AllowDurations.MaxMinutes);

    public GuardEvent Allow(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (duration > MaxDuration) duration = MaxDuration;
        Mode = GuardMode.Allowed;
        AllowUntil = _clock.UtcNow.Add(duration);
        ResetWarnings(duration);
        return new GuardEvent(GuardEventKind.ModeChanged, Mode, AllowUntil, null);
    }

    public GuardEvent? Block()
    {
        if (Mode == GuardMode.Blocked) return null;
        Mode = GuardMode.Blocked;
        AllowUntil = null;
        _pendingWarnings.Clear();
        return new GuardEvent(GuardEventKind.ModeChanged, Mode, null, null);
    }

    public IReadOnlyList<GuardEvent> Tick()
    {
        if (Mode != GuardMode.Allowed || AllowUntil is null) return Array.Empty<GuardEvent>();

        var remaining = AllowUntil.Value - _clock.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            var ev = Block();
            return ev is null ? Array.Empty<GuardEvent>() : new[] { ev };
        }

        var events = new List<GuardEvent>();
        foreach (var minutes in _pendingWarnings.ToArray())
        {
            if (remaining <= TimeSpan.FromMinutes(minutes))
            {
                _pendingWarnings.Remove(minutes);
                events.Add(new GuardEvent(GuardEventKind.Expiring, Mode, AllowUntil, minutes));
            }
        }
        return events;
    }

    public void Restore(GuardMode mode, DateTimeOffset? allowUntil)
    {
        Mode = mode;
        AllowUntil = mode == GuardMode.Allowed ? allowUntil : null;
        var maxUntil = _clock.UtcNow.Add(MaxDuration);
        if (AllowUntil > maxUntil) AllowUntil = maxUntil;
        if (Mode == GuardMode.Allowed && AllowUntil is not null)
            ResetWarnings(AllowUntil.Value - _clock.UtcNow);
        else
            _pendingWarnings.Clear();
    }

    private void ResetWarnings(TimeSpan remaining)
    {
        _pendingWarnings.Clear();
        foreach (var minutes in AllowDurations.WarningMinutes)
            if (remaining > TimeSpan.FromMinutes(minutes))
                _pendingWarnings.Add(minutes);
    }
}
