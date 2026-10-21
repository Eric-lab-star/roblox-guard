using RobloxGuard.Core;

namespace RobloxGuard.Core.Tests;

public class ClockTests
{
    [Fact]
    public void FakeClock_Advance_MovesTime()
    {
        var c = new FakeClock { UtcNow = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero) };
        c.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 5, 0, TimeSpan.Zero), c.UtcNow);
    }

    [Fact]
    public void GuardPaths_AreUnderExpectedRoots()
    {
        Assert.EndsWith(@"\RobloxGuard\config.json", GuardPaths.ConfigPath);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), GuardPaths.DataDir);
    }
}
