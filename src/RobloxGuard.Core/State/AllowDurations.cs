namespace RobloxGuard.Core.State;

public static class AllowDurations
{
    public static readonly int[] PresetMinutes = { 30, 60, 120 };
    public const int MaxMinutes = 1440;
    public static readonly int[] WarningMinutes = { 5, 1 };

    /// <summary>로컬 다음 자정까지. 최소 1분.</summary>
    public static TimeSpan UntilEndOfDay(DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var nextMidnightLocal = new DateTimeOffset(local.Date.AddDays(1), local.Offset);
        var remaining = nextMidnightLocal - local;
        return remaining < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining;
    }
}
