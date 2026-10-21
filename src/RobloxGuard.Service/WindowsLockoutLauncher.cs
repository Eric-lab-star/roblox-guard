using System.Diagnostics;
using RobloxGuard.Core.Engine;
using RobloxGuard.Service.Native;

namespace RobloxGuard.Service;

/// <summary>
/// 잠금 화면 프로세스를 아이 세션에 SYSTEM 권한으로 띄우고 추적한다.
/// 세션당 하나만 살아 있게 하고, 예정 시간을 크게 넘겨 살아 있으면 강제 종료한다(안전장치).
/// </summary>
public sealed class WindowsLockoutLauncher : ILockoutLauncher, ISweepable, IDisposable
{
    private static readonly TimeSpan SweepGrace = TimeSpan.FromSeconds(5);

    private readonly ILogger<WindowsLockoutLauncher> _log;
    private readonly object _gate = new();
    private readonly Dictionary<int, Running> _active = new(); // sessionId → 실행 중

    public WindowsLockoutLauncher(ILogger<WindowsLockoutLauncher> log) => _log = log;

    /// <summary>잠금 창이 끝났을 때 호출될 콜백. Program 이 engine.OnLockoutEnded 로 연결한다.</summary>
    public Action<int>? LockoutEnded { get; set; }

    private sealed record Running(Process Process, DateTimeOffset StartedAt, int Seconds);

    public void Launch(int sessionId, int seconds)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(sessionId, out var existing) && !existing.Process.HasExited)
                return; // 이미 떠 있음

            var exe = Path.Combine(AppContext.BaseDirectory, "RobloxGuard.Lockout.exe");
            if (!File.Exists(exe))
            {
                _log.LogWarning("잠금 실행 파일이 없습니다: {Exe}", exe);
                return;
            }

            var proc = SystemProcessLauncher.Launch(exe, $"--seconds {seconds}", sessionId, _log);
            if (proc is null) return;

            try
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => OnExited(sessionId);
            }
            catch (InvalidOperationException) { /* 이미 종료 */ OnExited(sessionId); }

            _active[sessionId] = new Running(proc, DateTimeOffset.UtcNow, seconds);
            _log.LogInformation("세션 {Session} 에 {Seconds}초 잠금 화면 표시", sessionId, seconds);
        }
    }

    private void OnExited(int sessionId)
    {
        lock (_gate)
        {
            if (_active.Remove(sessionId, out var r)) r.Process.Dispose();
        }
        LockoutEnded?.Invoke(sessionId);
    }

    /// <summary>예정 시간 + 여유를 넘겨 살아 있는 잠금 프로세스를 강제 종료한다.</summary>
    public void Sweep()
    {
        lock (_gate)
        {
            foreach (var (session, r) in _active.ToArray())
            {
                var deadline = r.StartedAt.AddSeconds(r.Seconds).Add(SweepGrace);
                if (r.Process.HasExited)
                {
                    _active.Remove(session);
                    r.Process.Dispose();
                }
                else if (DateTimeOffset.UtcNow > deadline)
                {
                    _log.LogWarning("세션 {Session} 잠금 프로세스가 예정 시간을 넘겨 강제 종료", session);
                    KillQuiet(r.Process);
                }
            }
        }
    }

    public void KillAll()
    {
        lock (_gate)
        {
            foreach (var r in _active.Values) KillQuiet(r.Process);
            _active.Clear();
        }
    }

    private void KillQuiet(Process p)
    {
        try { if (!p.HasExited) p.Kill(); }
        catch (Exception ex) { _log.LogDebug(ex, "잠금 프로세스 종료 실패"); }
    }

    public void Dispose() => KillAll();
}
