using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using RobloxGuard.Core;
using RobloxGuard.Core.Config;
using RobloxGuard.Core.Engine;
using RobloxGuard.Core.Network;

namespace RobloxGuard.Service;

/// <summary>
/// hosts 블록 + Windows 방화벽 아웃바운드 규칙. 둘 다 보조 수단이라 실패해도 예외를 밖으로 내지 않는다.
/// </summary>
public sealed class WindowsNetworkEnforcer : INetworkEnforcer
{
    public const string RulePrefix = "RobloxGuard-";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private readonly GuardConfig _config;
    private readonly ILogger<WindowsNetworkEnforcer> _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _rulesByPath = new(StringComparer.OrdinalIgnoreCase); // path → rule name
    private bool _blocked = true;

    public WindowsNetworkEnforcer(GuardConfig config, ILogger<WindowsNetworkEnforcer> log)
    {
        _config = config;
        _log = log;
    }

    public void SetBlocked(bool blocked)
    {
        lock (_gate)
        {
            _blocked = blocked;
            try
            {
                HostsFileEditor.Apply(GuardPaths.HostsPath, blocked, _config.AllBlockedHosts());
                RunQuiet("ipconfig", "/flushdns");
                _log.LogInformation("hosts 차단 = {Blocked}", blocked);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "hosts 적용 실패 (blocked={Blocked})", blocked);
            }

            foreach (var rule in _rulesByPath.Values)
                SetRuleEnabled(rule, blocked);
        }
    }

    /// <summary>알려진 로블록스 설치 경로를 다시 훑어 규칙을 만들거나 지운다. 60초마다 호출.</summary>
    public void RefreshFirewallRules()
    {
        lock (_gate)
        {
            HashSet<string> found;
            try { found = FindRobloxExecutables(); }
            catch (Exception ex) { _log.LogWarning(ex, "로블록스 실행 파일 스캔 실패"); return; }

            foreach (var path in found)
            {
                if (_rulesByPath.ContainsKey(path)) continue;
                var name = RuleNameFor(path);
                if (AddRule(name, path, _blocked)) _rulesByPath[path] = name;
            }

            foreach (var stale in _rulesByPath.Keys.Where(p => !found.Contains(p)).ToList())
            {
                DeleteRule(_rulesByPath[stale]);
                _rulesByPath.Remove(stale);
            }
        }
    }

    /// <summary>uninstall 보조. 이름이 RobloxGuard- 로 시작하는 규칙을 전부 지운다.</summary>
    public static void RemoveAllRules(ILogger? log = null)
    {
        var (code, output) = Run("netsh", "advfirewall firewall show rule name=all", log);
        if (code != 0) return;
        var names = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains(RulePrefix, StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(RulePrefix, StringComparison.Ordinal))..].Trim())
            .Distinct()
            .ToList();
        foreach (var n in names) Run("netsh", $"advfirewall firewall delete rule name=\"{n}\"", log);
    }

    internal static HashSet<string> FindRobloxExecutables()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usersRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
        if (!Directory.Exists(usersRoot)) return result;

        foreach (var user in Directory.EnumerateDirectories(usersRoot))
        {
            var versions = Path.Combine(user, "AppData", "Local", "Roblox", "Versions");
            if (!Directory.Exists(versions)) continue;
            try
            {
                foreach (var exe in Directory.EnumerateFiles(versions, "*.exe", SearchOption.AllDirectories))
                    result.Add(exe);
            }
            catch (Exception) { /* 권한 등 — 다음 사용자 */ }
        }
        return result;
    }

    internal static string RuleNameFor(string path)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return RulePrefix + Convert.ToHexString(hash)[..12];
    }

    private bool AddRule(string name, string path, bool enabled)
    {
        var yn = enabled ? "yes" : "no";
        var (code, _) = Run("netsh",
            $"advfirewall firewall add rule name=\"{name}\" dir=out action=block program=\"{path}\" enable={yn} description=\"RobloxGuard\"", _log);
        if (code == 0) _log.LogInformation("방화벽 규칙 추가: {Name} → {Path}", name, path);
        return code == 0;
    }

    private void SetRuleEnabled(string name, bool enabled)
        => Run("netsh", $"advfirewall firewall set rule name=\"{name}\" new enable={(enabled ? "yes" : "no")}", _log);

    private void DeleteRule(string name)
        => Run("netsh", $"advfirewall firewall delete rule name=\"{name}\"", _log);

    private void RunQuiet(string file, string args) => Run(file, args, _log);

    private static (int Code, string Output) Run(string file, string args, ILogger? log)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                log?.LogWarning("{File} {Args} 시간 초과", file, args);
                return (-1, "");
            }
            var output = stdout.Result + stderr.Result;
            if (p.ExitCode != 0) log?.LogDebug("{File} {Args} → {Code}: {Out}", file, args, p.ExitCode, output.Trim());
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "{File} 실행 실패", file);
            return (-1, "");
        }
    }
}
