using RobloxGuard.Core.Network;
using RobloxGuard.Core.Security;

namespace RobloxGuard.Core.Config;

/// <summary>config.json 의 모양.</summary>
public sealed class GuardConfig
{
    public const int MinLockoutSeconds = 1;
    public const int MaxLockoutSeconds = 60;

    public PasswordRecord? Password { get; set; }
    public int LogRetentionDays { get; set; } = 90;
    public int LockoutSeconds { get; set; } = 15;
    public List<string> ExtraProcessNames { get; set; } = new();
    public List<string> ExtraBlockedHosts { get; set; } = new();

    public bool IsConfigured => Password is not null;

    public static GuardConfig Load(string path)
    {
        var c = JsonFile.LoadOrDefault(path, () => new GuardConfig());
        c.LockoutSeconds = Math.Clamp(c.LockoutSeconds, MinLockoutSeconds, MaxLockoutSeconds);
        if (c.LogRetentionDays < 1) c.LogRetentionDays = 90;
        c.ExtraProcessNames ??= new();
        c.ExtraBlockedHosts ??= new();
        return c;
    }

    public void Save(string path) => JsonFile.SaveAtomic(path, this);

    public IReadOnlyList<string> AllBlockedHosts()
        => HostsFileEditor.DefaultBlockedHosts
            .Concat(ExtraBlockedHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
