using RobloxGuard.Core;
using RobloxGuard.Core.Config;
using RobloxGuard.Core.Logging;

namespace RobloxGuard.Service;

/// <summary>60초마다 방화벽 규칙 갱신과 잠금 프로세스 감시, 하루 한 번 기록 정리.</summary>
public sealed class MaintenanceService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    private readonly WindowsNetworkEnforcer _network;
    private readonly IEnumerable<ISweepable> _sweepables;
    private readonly AttemptLog _log;
    private readonly GuardConfig _config;
    private readonly IClock _clock;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(WindowsNetworkEnforcer network, IEnumerable<ISweepable> sweepables, AttemptLog log,
        GuardConfig config, IClock clock, ILogger<MaintenanceService> logger)
    {
        _network = network;
        _sweepables = sweepables;
        _log = log;
        _config = config;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPrune = _clock.UtcNow;
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { _network.RefreshFirewallRules(); }
            catch (Exception ex) { _logger.LogWarning(ex, "방화벽 갱신 실패"); }

            foreach (var s in _sweepables)
            {
                try { s.Sweep(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Sweep 실패"); }
            }

            if (_clock.UtcNow - lastPrune >= PruneInterval)
            {
                lastPrune = _clock.UtcNow;
                try { _log.Prune(_clock.UtcNow.AddDays(-_config.LogRetentionDays)); }
                catch (Exception ex) { _logger.LogWarning(ex, "기록 정리 실패"); }
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

/// <summary>주기적으로 정리가 필요한 구성 요소 (잠금 프로세스 감시 등).</summary>
public interface ISweepable
{
    void Sweep();
}
