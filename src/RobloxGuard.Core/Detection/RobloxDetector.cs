namespace RobloxGuard.Core.Detection;

/// <summary>프로세스 한 건의 판정에 필요한 정보. Name 은 .exe 없이 와도 되고 있어도 된다.</summary>
public sealed record ProcessInfo(int Pid, string Name, string? Path, string? CompanyName, int SessionId);

public static class RobloxDetector
{
    public static readonly string[] KnownProcessNames =
    {
        "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxPlayerInstaller",
        "RobloxStudioBeta", "RobloxStudioLauncher", "RobloxStudioInstaller",
    };

    public const string StorePathMarker = @"\ROBLOXCORPORATION.ROBLOX_";
    public const string RobloxCompanyName = "Roblox Corporation";
    public const string SelfPrefix = "RobloxGuard";

    public static bool IsRoblox(ProcessInfo p, IReadOnlyCollection<string>? extraNames = null)
    {
        var name = NormalizeName(p.Name);
        if (name.Length == 0 && string.IsNullOrEmpty(p.Path) && string.IsNullOrEmpty(p.CompanyName)) return false;
        if (name.StartsWith(SelfPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        if (KnownProcessNames.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        if (extraNames is not null && extraNames.Any(k => NormalizeName(k).Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        if (p.CompanyName is not null && p.CompanyName.Contains(RobloxCompanyName, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Path is not null && p.Path.Contains(StorePathMarker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string NormalizeName(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }
}
