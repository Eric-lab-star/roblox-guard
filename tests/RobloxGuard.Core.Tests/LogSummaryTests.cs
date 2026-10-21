using RobloxGuard.Core.Logging;

namespace RobloxGuard.Core.Tests;

public class LogSummaryTests
{
    private static readonly TimeZoneInfo Seoul =
        TimeZoneInfo.CreateCustomTimeZone("Seoul+9", TimeSpan.FromHours(9), "Seoul", "Seoul");

    private static AttemptEntry E(DateTimeOffset ts, string type) => new(ts, type, "kid", "RobloxPlayerBeta", null, null);

    [Fact]
    public void GroupByDay_UsesLocalDay()
    {
        var a = E(new DateTimeOffset(2026, 10, 21, 16, 0, 0, TimeSpan.Zero), AttemptTypes.LockoutStart); // 로컬 22일 01:00
        var b = E(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero), AttemptTypes.LockoutStart);
        var days = LogSummary.GroupByDay(new[] { a, b }, Seoul);
        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 10, 22), day.Day);
        Assert.Equal(2, day.AttemptCount);
    }

    [Fact]
    public void AttemptCount_CountsLockoutStartOnly()
    {
        var t = new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            E(t, AttemptTypes.BlockedKill), E(t, AttemptTypes.BlockedKill), E(t, AttemptTypes.BlockedKill),
            E(t, AttemptTypes.LockoutStart),
        };
        var day = Assert.Single(LogSummary.GroupByDay(entries, Seoul));
        Assert.Equal(1, day.AttemptCount);
        Assert.Equal(4, day.Entries.Count);
    }

    [Fact]
    public void Days_NewestFirst()
    {
        var old = E(new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero), AttemptTypes.LockoutStart);
        var recent = E(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero), AttemptTypes.LockoutStart);
        var days = LogSummary.GroupByDay(new[] { old, recent }, Seoul);
        Assert.Equal(new DateOnly(2026, 10, 22), days[0].Day);
        Assert.Equal(new DateOnly(2026, 10, 20), days[1].Day);
    }
}
