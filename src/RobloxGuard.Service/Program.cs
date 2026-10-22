using RobloxGuard.Core;
using RobloxGuard.Core.Config;
using RobloxGuard.Core.Engine;
using RobloxGuard.Core.Logging;
using RobloxGuard.Core.Security;
using RobloxGuard.Core.State;
using RobloxGuard.Service;

try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* 서비스 모드: 콘솔 없음 */ }

if (args.Length > 0 && args[0].Equals("set-password", StringComparison.OrdinalIgnoreCase))
    return SetPassword();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "RobloxGuard");

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(_ => GuardConfig.Load(GuardPaths.ConfigPath));
builder.Services.AddSingleton(_ => new AttemptLog(GuardPaths.LogPath));
builder.Services.AddSingleton<WindowsNetworkEnforcer>();
builder.Services.AddSingleton<INetworkEnforcer>(sp => sp.GetRequiredService<WindowsNetworkEnforcer>());
builder.Services.AddSingleton<WindowsLockoutLauncher>();
builder.Services.AddSingleton<ILockoutLauncher>(sp => sp.GetRequiredService<WindowsLockoutLauncher>());
builder.Services.AddSingleton<ISweepable>(sp => sp.GetRequiredService<WindowsLockoutLauncher>());
builder.Services.AddSingleton(sp =>
{
    var engine = new GuardEngine(
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<GuardConfig>(),
        GuardStateSnapshot.Load(GuardPaths.StatePath),
        sp.GetRequiredService<AttemptLog>(),
        sp.GetRequiredService<INetworkEnforcer>(),
        sp.GetRequiredService<ILockoutLauncher>());
    var log = sp.GetRequiredService<ILogger<GuardEngine>>();
    engine.StateChanged += s => Safe(() => s.Save(GuardPaths.StatePath), log, "state.json 저장 실패");
    engine.ConfigChanged += c => Safe(() => c.Save(GuardPaths.ConfigPath), log, "config.json 저장 실패");
    sp.GetRequiredService<WindowsLockoutLauncher>().LockoutEnded += engine.OnLockoutEnded;
    return engine;
});
// PipeServer 를 가장 먼저 등록한다: 제어 채널(트레이 연결)이 다른 시작 작업보다
// 먼저 올라오게 해, 엔진/네트워크 시작에 문제가 있어도 부모가 항상 연결할 수 있다.
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<ProcessWatcher>();
builder.Services.AddHostedService<MaintenanceService>();

var host = builder.Build();
host.Services.GetRequiredService<GuardEngine>(); // LockoutEnded 연결을 위해 미리 생성
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() => host.Services.GetRequiredService<WindowsLockoutLauncher>().KillAll());
host.Run();
return 0;

static void Safe(Action action, ILogger log, string what)
{
    try { action(); }
    catch (Exception ex) { log.LogError(ex, "{What}", what); }
}

/// <summary>stdin 에서 한 줄을 읽어 비밀번호 해시를 config.json 에 기록한다. 설치 스크립트가 호출한다.</summary>
static int SetPassword()
{
    // stdin 을 UTF-8(BOM 없음)로 명시적으로 읽는다. 로그인 경로(트레이→파이프 JSON)도
    // UTF-8 이므로, 설치 때 저장하는 해시와 로그인 때 비교하는 해시가 같은 바이트열에서
    // 나온다. Console.InputEncoding 기본값은 OEM 코드페이지라 한글 비밀번호가 깨졌다.
    using var stdin = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
    var pw = stdin.ReadLine()?.TrimEnd('\r', '\n');
    if (string.IsNullOrEmpty(pw) || pw.Length < 4)
    {
        Console.Error.WriteLine("비밀번호는 4자 이상이어야 합니다.");
        return 2;
    }
    var config = GuardConfig.Load(GuardPaths.ConfigPath);
    config.Password = PasswordHasher.Hash(pw);
    config.Save(GuardPaths.ConfigPath);
    Console.WriteLine($"비밀번호를 저장했습니다: {GuardPaths.ConfigPath}");
    return 0;
}
