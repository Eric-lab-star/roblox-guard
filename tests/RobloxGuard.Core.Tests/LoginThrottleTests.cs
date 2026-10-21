using RobloxGuard.Core.Security;

namespace RobloxGuard.Core.Tests;

public class LoginThrottleTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void FourFailures_NotLocked()
    {
        var t = new LoginThrottle(_clock);
        for (var i = 0; i < 4; i++) Assert.False(t.RecordFailure());
        Assert.False(t.IsLocked);
        Assert.Equal(4, t.FailedAttempts);
    }

    [Fact]
    public void FifthFailure_LocksForFiveMinutes()
    {
        var t = new LoginThrottle(_clock);
        for (var i = 0; i < 4; i++) t.RecordFailure();
        Assert.True(t.RecordFailure());
        Assert.True(t.IsLocked);
        Assert.Equal(_clock.UtcNow.AddMinutes(5), t.LockedUntil);
    }

    [Fact]
    public void Lock_ExpiresAndResetsCounter()
    {
        var t = new LoginThrottle(_clock);
        for (var i = 0; i < 5; i++) t.RecordFailure();
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(t.IsLocked);
        Assert.Equal(0, t.FailedAttempts);
    }

    [Fact]
    public void Success_ResetsCounter()
    {
        var t = new LoginThrottle(_clock);
        for (var i = 0; i < 3; i++) t.RecordFailure();
        t.RecordSuccess();
        Assert.Equal(0, t.FailedAttempts);
    }

    [Fact]
    public void Restore_ReappliesLock()
    {
        var t = new LoginThrottle(_clock);
        t.Restore(0, _clock.UtcNow.AddMinutes(2));
        Assert.True(t.IsLocked);
    }
}

public class LoginThrottleRestoreTests
{
    [Fact]
    public void Restore_FarFutureLock_IsClampedToLockDuration()
    {
        var clock = new FakeClock();
        var t = new LoginThrottle(clock);
        t.Restore(0, clock.UtcNow.AddDays(30));
        Assert.Equal(clock.UtcNow.AddMinutes(5), t.LockedUntil);
    }

    [Fact]
    public void Restore_FailuresAtCap_WithoutLockedUntil_DerivesLock()
    {
        var clock = new FakeClock();
        var t = new LoginThrottle(clock);
        t.Restore(5, null);
        Assert.True(t.IsLocked);
    }
}
