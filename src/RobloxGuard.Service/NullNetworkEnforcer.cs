using RobloxGuard.Core.Engine;

namespace RobloxGuard.Service;

/// <summary>Task 11 전까지의 임시 구현. 로그만 남긴다.</summary>
public sealed class NullNetworkEnforcer : INetworkEnforcer
{
    private readonly ILogger<NullNetworkEnforcer> _log;
    public NullNetworkEnforcer(ILogger<NullNetworkEnforcer> log) => _log = log;
    public void SetBlocked(bool blocked) => _log.LogInformation("(미구현) 네트워크 차단 = {Blocked}", blocked);
}
