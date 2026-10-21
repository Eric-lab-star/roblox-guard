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

    /// <summary>
    /// 자기 자신(현재 프로세스)과 설치 폴더 아래의 실행 파일만 면제한다.
    /// 이름만으로는 면제하지 않는다: 아이가 로블록스 exe 를 RobloxGuard*.exe 로 바꿔도 잡혀야 한다.
    /// </summary>
    public static bool IsRoblox(ProcessInfo p, IReadOnlyCollection<string>? extraNames = null)
    {
        var name = NormalizeName(p.Name);
        if (name.Length == 0 && string.IsNullOrEmpty(p.Path) && string.IsNullOrEmpty(p.CompanyName)) return false;
        if (p.Pid == Environment.ProcessId) return false;
        if (IsUnderInstallDir(p.Path)) return false;

        if (KnownProcessNames.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        if (extraNames is not null && extraNames.Any(k => NormalizeName(k).Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        if (p.CompanyName is not null && p.CompanyName.Contains(RobloxCompanyName, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Path is not null && p.Path.Contains(StorePathMarker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsUnderInstallDir(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var root = System.IO.Path.GetFullPath(GuardPaths.InstallDir).TrimEnd('\\') + "\\";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeName(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }
}
