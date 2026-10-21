using RobloxGuard.Core;
using RobloxGuard.Core.Logging;

namespace RobloxGuard.Core.Tests;

public class AttemptLogTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("rg-log-");
    private string LogPath => Path.Combine(_dir.FullName, "attempts.log");
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    public void Dispose() => _dir.Delete(true);

    private static AttemptEntry E(DateTimeOffset ts, string type = AttemptTypes.BlockedKill)
        => new(ts, type, "kid", "RobloxPlayerBeta", @"C:\x\RobloxPlayerBeta.exe", null);

    [Fact]
    public void Append_ThenReadSince_RoundTrips()
    {
        var log = new AttemptLog(LogPath);
        log.Append(E(T0));
        log.Append(E(T0.AddMinutes(1), AttemptTypes.LockoutStart));
        var all = log.ReadSince(DateTimeOffset.MinValue);
        Assert.Equal(2, all.Count);
        Assert.Equal(E(T0), all[0]);
        Assert.Equal(AttemptTypes.LockoutStart, all[1].Type);
    }

    [Fact]
    public void ReadSince_FiltersOlder()
    {
        var log = new AttemptLog(LogPath);
        log.Append(E(T0));
        log.Append(E(T0.AddHours(1)));
        Assert.Single(log.ReadSince(T0.AddMinutes(30)));
    }

    [Fact]
    public void ReadSince_SkipsCorruptLine()
    {
        var log = new AttemptLog(LogPath);
        log.Append(E(T0));
        File.AppendAllText(LogPath, "{garbage\n");
        log.Append(E(T0.AddMinutes(1)));
        Assert.Equal(2, log.ReadSince(DateTimeOffset.MinValue).Count);
    }

    [Fact]
    public void Prune_RemovesOldKeepsNew()
    {
        var log = new AttemptLog(LogPath);
        log.Append(E(T0.AddDays(-100)));
        log.Append(E(T0.AddDays(-1)));
        Assert.Equal(1, log.Prune(T0.AddDays(-90)));
        Assert.Single(log.ReadSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public void ReadSince_MissingFile_ReturnsEmpty()
        => Assert.Empty(new AttemptLog(LogPath).ReadSince(DateTimeOffset.MinValue));
}

public class JsonFileTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("rg-json-");
    public void Dispose() => _dir.Delete(true);

    private sealed record Sample(string Name, int Count);

    [Fact]
    public void SaveAtomic_ThenLoad_RoundTrips()
    {
        var path = Path.Combine(_dir.FullName, "sub", "a.json");
        JsonFile.SaveAtomic(path, new Sample("x", 3));
        Assert.Equal(new Sample("x", 3), JsonFile.LoadOrDefault(path, () => new Sample("", 0)));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void LoadOrDefault_CorruptFile_ReturnsFallback()
    {
        var path = Path.Combine(_dir.FullName, "a.json");
        File.WriteAllText(path, "not json");
        Assert.Equal(new Sample("fb", 1), JsonFile.LoadOrDefault(path, () => new Sample("fb", 1)));
    }

    [Fact]
    public void LoadOrDefault_Missing_ReturnsFallback()
        => Assert.Equal(new Sample("fb", 1), JsonFile.LoadOrDefault(Path.Combine(_dir.FullName, "none.json"), () => new Sample("fb", 1)));
}
