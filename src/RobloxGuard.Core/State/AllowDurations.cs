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
        var nextMidnightLocal = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        // 자정이 DST 로 존재하지 않는 시각이면 한 시간씩 뒤로 민다 (실제로는 거의 없음)
        while (tz.IsInvalidTime(nextMidnightLocal)) nextMidnightLocal = nextMidnightLocal.AddHours(1);
        var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnightLocal, tz);
        var remaining = nextMidnightUtc - nowUtc.UtcDateTime;
        if (remaining < TimeSpan.FromMinutes(1)) return TimeSpan.FromMinutes(1);
        var max = TimeSpan.FromMinutes(MaxMinutes);
        return remaining > max ? max : remaining;
    }
}
