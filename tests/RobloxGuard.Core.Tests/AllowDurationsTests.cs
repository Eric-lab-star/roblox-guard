using RobloxGuard.Core.State;

namespace RobloxGuard.Core.Tests;

public class AllowDurationsTests
{
    private static readonly TimeZoneInfo Seoul =
        TimeZoneInfo.CreateCustomTimeZone("Seoul+9", TimeSpan.FromHours(9), "Seoul", "Seoul");

    private static DateTimeOffset LocalToUtc(int h, int m, int s = 0)
        => new DateTimeOffset(2026, 10, 22, h, m, s, TimeSpan.FromHours(9)).ToUniversalTime();

    [Fact]
    public void UntilEndOfDay_At2300_Is60Min()
        => Assert.Equal(TimeSpan.FromMinutes(60), AllowDurations.UntilEndOfDay(LocalToUtc(23, 0), Seoul));

    [Fact]
    public void UntilEndOfDay_At235930_IsAtLeast1Min()
        => Assert.Equal(TimeSpan.FromMinutes(1), AllowDurations.UntilEndOfDay(LocalToUtc(23, 59, 30), Seoul));

    [Fact]
    public void UntilEndOfDay_AtMidnight_Is24h()
        => Assert.Equal(TimeSpan.FromHours(24), AllowDurations.UntilEndOfDay(LocalToUtc(0, 0), Seoul));

    [Fact]
    public void UntilEndOfDay_DstSpringForward_UsesZoneRules()
    {
        // 미국 동부 2026-03-08 02:00 → 03:00 건너뜀. 01:00 EST 에서 자정까지는 22시간.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var nowUtc = new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero); // 01:00 EST
        Assert.Equal(TimeSpan.FromHours(22), AllowDurations.UntilEndOfDay(nowUtc, eastern));
    }

    [Fact]
    public void Presets_AndLimits()
    {
        Assert.Equal(new[] { 30, 60, 120 }, AllowDurations.PresetMinutes);
        Assert.Equal(1440, AllowDurations.MaxMinutes);
        Assert.Equal(new[] { 5, 1 }, AllowDurations.WarningMinutes);
    }
}
