using System.Text;
using System.Text.RegularExpressions;

namespace RobloxGuard.Core.Network;

/// <summary>
/// hosts 파일 안의 "# RobloxGuard BEGIN ~ END" 블록만 넣고 뺀다.
/// 제거하면 삽입 전 원문과 바이트 단위로 같아진다.
/// </summary>
public static class HostsFileEditor
{
    public const string BeginMarker = "# RobloxGuard BEGIN";
    public const string EndMarker = "# RobloxGuard END";

    public static readonly string[] DefaultBlockedHosts =
    {
        "roblox.com", "www.roblox.com", "web.roblox.com", "apis.roblox.com",
        "auth.roblox.com", "games.roblox.com", "setup.rbxcdn.com",
        "now.gg", "www.now.gg", "app.now.gg",
    };

    private static readonly Regex BlockRegex =
        new(Regex.Escape(BeginMarker) + ".*?" + Regex.Escape(EndMarker), RegexOptions.Singleline | RegexOptions.Compiled);

    public static string InsertBlock(string content, IEnumerable<string> hosts)
    {
        var baseText = RemoveBlock(content);
        var block = BuildBlock(hosts);
        if (baseText.Length == 0) return block + "\r\n";
        if (baseText.EndsWith('\n')) return baseText + block + "\r\n";
        return baseText + "\r\n" + block;
    }

    public static string RemoveBlock(string content)
    {
        var m = BlockRegex.Match(content);
        if (!m.Success) return content;

        var start = m.Index;
        var end = m.Index + m.Length;
        if (end + 1 < content.Length + 1 && content.AsSpan(end).StartsWith("\r\n")) end += 2;
        else if (end < content.Length && content[end] == '\n') end += 1;
        else if (start >= 2 && content.AsSpan(start - 2, 2).SequenceEqual("\r\n")) start -= 2;

        return string.Concat(content.AsSpan(0, start), content.AsSpan(end));
    }

    public static void Apply(string hostsPath, bool blocked, IEnumerable<string> hosts)
    {
        var current = File.Exists(hostsPath) ? File.ReadAllText(hostsPath) : "";
        var next = blocked ? InsertBlock(current, hosts) : RemoveBlock(current);
        if (next == current && File.Exists(hostsPath)) return;

        var dir = Path.GetDirectoryName(Path.GetFullPath(hostsPath))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "hosts.robloxguard.tmp");
        File.WriteAllText(tmp, next, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, hostsPath, overwrite: true);
    }

    private static string BuildBlock(IEnumerable<string> hosts)
    {
        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append("\r\n");
        foreach (var h in hosts.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase))
            sb.Append("0.0.0.0 ").Append(h.Trim()).Append("\r\n");
        sb.Append(EndMarker);
        return sb.ToString();
    }
}
