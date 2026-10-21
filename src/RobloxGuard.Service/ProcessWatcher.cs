using System.Diagnostics;
using RobloxGuard.Core.Detection;
using RobloxGuard.Core.Engine;
using RobloxGuard.Service.Native;

namespace RobloxGuard.Service;

/// <summary>
/// 1초마다 엔진 Tick 을 돌리고 전체 프로세스를 훑어 로블록스를 종료한다.
/// 판단은 전부 GuardEngine 이 하고, 여기서는 열거와 Kill 만 한다.
/// </summary>
public sealed class ProcessWatcher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly GuardEngine _engine;
    private readonly ILogger<ProcessWatcher> _log;
    private readonly Dictionary<string, string?> _companyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _selfPid = Environment.ProcessId;

    public ProcessWatcher(GuardEngine engine, ILogger<ProcessWatcher> log)
    {
        _engine = engine;
        _log = log;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _engine.Start();
        _log.LogInformation("RobloxGuard 시작. 모드={Mode}", _engine.Mode);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { _engine.Tick(); }
            catch (Exception ex) { _log.LogError(ex, "Tick 실패"); }

            try { Scan(); }
            catch (Exception ex) { _log.LogError(ex, "프로세스 스캔 실패"); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Scan()
    {
        var extra = _engine.Config.ExtraProcessNames;
        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                if (proc.Id is 0 or 4 || proc.Id == _selfPid) continue;

                ProcessInfo info;
                try { info = Describe(proc); }
                catch (Exception) { continue; }

                if (!RobloxDetector.IsRoblox(info, extra)) continue;

                var decision = _engine.OnRobloxDetected(info, Wts.GetSessionUserName(info.SessionId));
                if (!decision.Kill) continue;

                try
                {
                    proc.Kill(entireProcessTree: true);
                    _log.LogInformation("종료: {Name} (pid {Pid}, 세션 {Session}){Lock}",
                        info.Name, info.Pid, info.SessionId, decision.LockoutStarted ? " + 잠금" : "");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
                {
                    _log.LogWarning(ex, "종료 실패: {Name} (pid {Pid})", info.Name, info.Pid);
                }
            }
        }
    }

    private ProcessInfo Describe(Process proc)
    {
        string? path = null;
        try { path = proc.MainModule?.FileName; }
        catch (Exception) { /* 접근 불가(보호된 프로세스 등) — 이름만으로 판정 */ }

        string? company = null;
        if (path is not null)
        {
            if (!_companyCache.TryGetValue(path, out company))
            {
                try { company = FileVersionInfo.GetVersionInfo(path).CompanyName; }
                catch (Exception) { company = null; }
                if (_companyCache.Count > 2000) _companyCache.Clear();
                _companyCache[path] = company;
            }
        }

        return new ProcessInfo(proc.Id, proc.ProcessName, path, company, proc.SessionId);
    }
}
