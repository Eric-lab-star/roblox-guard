namespace RobloxGuard.Core.Security;

/// <summary>연속 실패 N회 → 일정 시간 잠금. 잠금이 지나면 카운터 초기화.</summary>
public sealed class LoginThrottle
{
    private readonly IClock _clock;
    private readonly int _maxFailures;
    private readonly TimeSpan _lockDuration;

    public LoginThrottle(IClock clock, int maxFailures = 5, TimeSpan? lockDuration = null)
    {
        _clock = clock;
        _maxFailures = maxFailures;
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
    }

    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsLocked
    {
        get
        {
            if (LockedUntil is null) return false;
            if (_clock.UtcNow < LockedUntil.Value) return true;
            LockedUntil = null;
            FailedAttempts = 0;
            return false;
        }
    }

    /// <returns>이번 실패로 잠겼으면 true.</returns>
    public bool RecordFailure()
    {
        if (IsLocked) return false;
        FailedAttempts++;
        if (FailedAttempts < _maxFailures) return false;
        LockedUntil = _clock.UtcNow.Add(_lockDuration);
        return true;
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }

    public void Restore(int failedAttempts, DateTimeOffset? lockedUntil)
    {
        FailedAttempts = Math.Max(0, failedAttempts);
        LockedUntil = lockedUntil;
    }
}
