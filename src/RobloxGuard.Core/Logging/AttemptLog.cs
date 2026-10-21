using System.Text.Json;

namespace RobloxGuard.Core.Logging;

public static class AttemptTypes
{
    public const string BlockedKill = "blocked_kill";
    public const string LockoutStart = "blocked_lockout_start";
    public const string LockoutEnd = "blocked_lockout_end";
    public const string AllowStart = "allow_start";
    public const string AllowEnd = "allow_end";
    public const string BlockManual = "block_manual";
    public const string WrongPassword = "wrong_password";
    public const string PasswordLockout = "password_lockout";
    public const string ServiceStart = "service_start";
    public const string PasswordChanged = "password_changed";
}

public sealed record AttemptEntry(DateTimeOffset Ts, string Type, string? Session, string? Process, string? Path, string? Detail);

/// <summary>JSON Lines 기록 파일. 한 줄 = 한 건. 깨진 줄은 읽을 때 건너뛴다.</summary>
public sealed class AttemptLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public AttemptLog(string path) => _path = path;

    public void Append(AttemptEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonFile.Options) + "\n";
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
            using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var w = new StreamWriter(fs);
            w.Write(line);
        }
    }

    public IReadOnlyList<AttemptEntry> ReadSince(DateTimeOffset since)
    {
        lock (_gate)
        {
            return ReadAllUnlocked().Where(e => e.Ts >= since).ToList();
        }
    }

    public int Prune(DateTimeOffset olderThan)
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return 0;
            var all = ReadAllUnlocked();
            var keep = all.Where(e => e.Ts >= olderThan).ToList();
            var removed = all.Count - keep.Count;
            if (removed == 0) return 0;
            var tmp = _path + ".tmp";
            File.WriteAllLines(tmp, keep.Select(e => JsonSerializer.Serialize(e, JsonFile.Options)));
            File.Move(tmp, _path, overwrite: true);
            return removed;
        }
    }

    private List<AttemptEntry> ReadAllUnlocked()
    {
        var result = new List<AttemptEntry>();
        if (!File.Exists(_path)) return result;
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(fs);
        while (r.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<AttemptEntry>(line, JsonFile.Options);
                if (e is not null && !string.IsNullOrEmpty(e.Type)) result.Add(e);
            }
            catch (JsonException) { /* 부분 기록된 줄은 건너뛴다 */ }
        }
        return result;
    }
}
