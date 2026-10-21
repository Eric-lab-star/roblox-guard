using RobloxGuard.Core.State;

namespace RobloxGuard.Core.Tests;

public class GuardStateMachineTests
{
    private readonly FakeClock _clock = new();
    private readonly GuardStateMachine _sm;
    private readonly DateTimeOffset _start;

    public GuardStateMachineTests()
    {
        _sm = new GuardStateMachine(_clock);
        _start = _clock.UtcNow;
    }

    [Fact]
    public void Initial_IsBlocked()
    {
        Assert.Equal(GuardMode.Blocked, _sm.Mode);
        Assert.Null(_sm.AllowUntil);
    }

    [Fact]
    public void Allow30_SetsAllowedUntilNowPlus30()
    {
        var ev = _sm.Allow(TimeSpan.FromMinutes(30));
        Assert.Equal(GuardMode.Allowed, _sm.Mode);
        Assert.Equal(_start.AddMinutes(30), _sm.AllowUntil);
        Assert.Equal(GuardEventKind.ModeChanged, ev.Kind);
        Assert.Equal(GuardMode.Allowed, ev.Mode);
        Assert.Equal(_start.AddMinutes(30), ev.AllowUntil);
    }

    [Fact]
    public void Allow_Replaces_NotAccumulates()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(10));
        _sm.Allow(TimeSpan.FromMinutes(30));
        Assert.Equal(_start.AddMinutes(40), _sm.AllowUntil);
    }

    [Fact]
    public void Block_FromAllowed_ReturnsModeChanged()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        var ev = _sm.Block();
        Assert.NotNull(ev);
        Assert.Equal(GuardMode.Blocked, ev!.Mode);
        Assert.Equal(GuardMode.Blocked, _sm.Mode);
        Assert.Null(_sm.AllowUntil);
    }

    [Fact]
    public void Block_FromBlocked_ReturnsNull() => Assert.Null(_sm.Block());

    [Fact]
    public void Tick_BeforeExpiry_NoEvents()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(_sm.Tick());
    }

    [Fact]
    public void Tick_At5MinLeft_RaisesExpiring5_Once()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(25));
        var evs = _sm.Tick();
        var ev = Assert.Single(evs);
        Assert.Equal(GuardEventKind.Expiring, ev.Kind);
        Assert.Equal(5, ev.MinutesLeft);
        Assert.Empty(_sm.Tick());
    }

    [Fact]
    public void Tick_At1MinLeft_RaisesExpiring1()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(25));
        _sm.Tick();
        _clock.Advance(TimeSpan.FromMinutes(4));
        var ev = Assert.Single(_sm.Tick());
        Assert.Equal(GuardEventKind.Expiring, ev.Kind);
        Assert.Equal(1, ev.MinutesLeft);
    }

    [Fact]
    public void Tick_AtExpiry_ReturnsBlocked()
    {
        _sm.Allow(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(30));
        var evs = _sm.Tick();
        Assert.Contains(evs, e => e.Kind == GuardEventKind.ModeChanged && e.Mode == GuardMode.Blocked);
        Assert.Equal(GuardMode.Blocked, _sm.Mode);
        Assert.Null(_sm.AllowUntil);
    }

    [Fact]
    public void Allow3Min_Only1MinWarning()
    {
        _sm.Allow(TimeSpan.FromMinutes(3));
        Assert.Empty(_sm.Tick());
        _clock.Advance(TimeSpan.FromMinutes(2));
        var ev = Assert.Single(_sm.Tick());
        Assert.Equal(1, ev.MinutesLeft);
    }

    [Fact]
    public void Allow1Min_NoWarnings()
    {
        _sm.Allow(TimeSpan.FromMinutes(1));
        Assert.Empty(_sm.Tick());
        _clock.Advance(TimeSpan.FromMinutes(1));
        var evs = _sm.Tick();
        Assert.DoesNotContain(evs, e => e.Kind == GuardEventKind.Expiring);
        Assert.Single(evs, e => e.Kind == GuardEventKind.ModeChanged && e.Mode == GuardMode.Blocked);
    }

    [Fact]
    public void Restore_PastAllowUntil_FirstTickExpires()
    {
        _sm.Restore(GuardMode.Allowed, _start.AddMinutes(-1));
        var ev = Assert.Single(_sm.Tick());
        Assert.Equal(GuardEventKind.ModeChanged, ev.Kind);
        Assert.Equal(GuardMode.Blocked, ev.Mode);
    }

    [Fact]
    public void Restore_FutureAllowUntil_KeepsAllowed()
    {
        _sm.Restore(GuardMode.Allowed, _start.AddMinutes(10));
        Assert.Empty(_sm.Tick());
        Assert.Equal(GuardMode.Allowed, _sm.Mode);
    }
}

public class GuardStateMachineCapTests
{
    [Fact]
    public void Allow_BeyondMax_IsClampedTo1440Minutes()
    {
        var clock = new FakeClock();
        var sm = new GuardStateMachine(clock);
        sm.Allow(TimeSpan.FromDays(30));
        Assert.Equal(clock.UtcNow.AddMinutes(1440), sm.AllowUntil);
    }

    [Fact]
    public void Restore_BeyondMax_IsClampedTo1440Minutes()
    {
        var clock = new FakeClock();
        var sm = new GuardStateMachine(clock);
        sm.Restore(GuardMode.Allowed, clock.UtcNow.AddDays(30));
        Assert.Equal(clock.UtcNow.AddMinutes(1440), sm.AllowUntil);
    }

    [Fact]
    public void Restore_AllowedWithoutUntil_FallsBackToBlocked()
    {
        var sm = new GuardStateMachine(new FakeClock());
        sm.Restore(GuardMode.Allowed, null);
        Assert.Equal(GuardMode.Blocked, sm.Mode);
    }

    [Fact]
    public void Allow_ZeroOrNegative_Throws()
    {
        var sm = new GuardStateMachine(new FakeClock());
        Assert.Throws<ArgumentOutOfRangeException>(() => sm.Allow(TimeSpan.Zero));
    }
}
