namespace RobloxGuard.Core.Logging;

public sealed record DaySummary(DateOnly Day, int AttemptCount, IReadOnlyList<AttemptEntry> Entries);

public static class LogSummary
{
    /// <summary>로컬 날짜별로 묶는다. 최신 날짜 먼저. 시도 횟수는 blocked_lockout_start 건수.</summary>
    public static IReadOnlyList<DaySummary> GroupByDay(IEnumerable<AttemptEntry> entries, TimeZoneInfo tz)
        => entries
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Ts, tz).DateTime))
            .OrderByDescending(g => g.Key)
            .Select(g => new DaySummary(
                g.Key,
                g.Count(e => e.Type == AttemptTypes.LockoutStart),
                g.OrderBy(e => e.Ts).ToList()))
            .ToList();
}
