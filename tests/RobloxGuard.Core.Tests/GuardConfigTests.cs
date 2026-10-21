using RobloxGuard.Core.Config;
using RobloxGuard.Core.Network;
using RobloxGuard.Core.Security;
using RobloxGuard.Core.State;

namespace RobloxGuard.Core.Tests;

public class GuardConfigTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("rg-cfg-");
    private string ConfigPath => Path.Combine(_dir.FullName, "config.json");
    private string StatePath => Path.Combine(_dir.FullName, "state.json");
    public void Dispose() => _dir.Delete(true);

    [Fact]
    public void Load_Missing_ReturnsDefaults()
    {
        var c = GuardConfig.Load(ConfigPath);
        Assert.False(c.IsConfigured);
        Assert.Equal(15, c.LockoutSeconds);
        Assert.Equal(90, c.LogRetentionDays);
        Assert.Empty(c.ExtraProcessNames);
        Assert.Empty(c.ExtraBlockedHosts);
    }

    [Fact]
    public void Load_Corrupt_ReturnsDefaults()
    {
        File.WriteAllText(ConfigPath, "not json");
        Assert.False(GuardConfig.Load(ConfigPath).IsConfigured);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var c = new GuardConfig { Password = PasswordHasher.Hash("pw1234"), ExtraBlockedHosts = { "x.com" } };
        c.Save(ConfigPath);
        var loaded = GuardConfig.Load(ConfigPath);
        Assert.True(loaded.IsConfigured);
        Assert.Equal(c.Password, loaded.Password);
        Assert.Equal(new[] { "x.com" }, loaded.ExtraBlockedHosts);
        Assert.True(PasswordHasher.Verify("pw1234", loaded.Password!));
    }

    [Theory]
    [InlineData(999, 60)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(20, 20)]
    public void LockoutSeconds_Clamped(int stored, int expected)
    {
        File.WriteAllText(ConfigPath, $"{{\"lockoutSeconds\":{stored}}}");
        Assert.Equal(expected, GuardConfig.Load(ConfigPath).LockoutSeconds);
    }

    [Fact]
    public void AllBlockedHosts_MergesWithoutDuplicates()
    {
        var c = new GuardConfig { ExtraBlockedHosts = { "roblox.com", "x.com" } };
        var all = c.AllBlockedHosts();
        Assert.Equal(HostsFileEditor.DefaultBlockedHosts.Length + 1, all.Count);
        Assert.Contains("x.com", all);
    }

    [Fact]
    public void Snapshot_Save_Load_RoundTrips()
    {
        var until = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var s = new GuardStateSnapshot(GuardMode.Allowed, until, 2, null);
        s.Save(StatePath);
        Assert.Equal(s, GuardStateSnapshot.Load(StatePath));
    }

    [Fact]
    public void Snapshot_Missing_ReturnsDefault()
        => Assert.Equal(GuardStateSnapshot.Default, GuardStateSnapshot.Load(StatePath));
}
