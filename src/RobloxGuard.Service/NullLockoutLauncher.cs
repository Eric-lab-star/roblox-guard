using RobloxGuard.Core.Engine;

namespace RobloxGuard.Service;

/// <summary>Task 12 전까지의 임시 구현. 로그만 남긴다.</summary>
public sealed class NullLockoutLauncher : ILockoutLauncher
{
    private readonly ILogger<NullLockoutLauncher> _log;
    public NullLockoutLauncher(ILogger<NullLockoutLauncher> log) => _log = log;
    public void Launch(int sessionId, int seconds) => _log.LogInformation("(미구현) 잠금 화면 세션={Session} {Seconds}초", sessionId, seconds);
}
