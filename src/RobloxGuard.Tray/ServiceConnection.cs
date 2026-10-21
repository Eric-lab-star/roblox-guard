using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RobloxGuard.Core;
using RobloxGuard.Core.Protocol;

namespace RobloxGuard.Tray;

/// <summary>
/// 서비스와의 파이프 통신. 2초마다 상태를 조회하고, 별도 연결로 이벤트를 구독한다.
/// 끊기면 아이콘을 회색으로 두고 3초마다 재연결한다.
/// 모든 이벤트는 UI 스레드로 마샬링해 호출자에게 전달한다.
/// </summary>
internal sealed class ServiceConnection : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(3);

    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _cts = new();

    public ServiceConnection(SynchronizationContext ui) => _ui = ui;

    public bool IsConnected { get; private set; }

    /// <summary>2초 주기 상태. 연결 실패 시 null.</summary>
    public event Action<PipeResponse?>? StatusUpdated;
    /// <summary>구독 연결로 받은 이벤트.</summary>
    public event Action<PipeEvent>? EventReceived;

    public void Start()
    {
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
        _ = Task.Run(() => SubscribeLoopAsync(_cts.Token));
    }

    public async Task<PipeResponse> SendAsync(PipeRequest req)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", GuardPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            if (!ServerIsTrustworthy(pipe)) return PipeResponse.Fail("disconnected");
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(ProtocolJson.Serialize(req));
            var line = await reader.ReadLineAsync();
            return (line is null ? null : ProtocolJson.Deserialize<PipeResponse>(line)) ?? PipeResponse.Fail("disconnected");
        }
        catch (Exception)
        {
            return PipeResponse.Fail("disconnected");
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var res = await SendAsync(new PipeRequest { Type = RequestTypes.GetStatus });
            IsConnected = res.Ok && res.Error != "disconnected";
            Post(() => StatusUpdated?.Invoke(IsConnected ? res : null));
            try { await Task.Delay(PollInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SubscribeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", GuardPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(3000, token);
                if (!ServerIsTrustworthy(pipe)) { await Task.Delay(Reconnect, token); continue; }
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(ProtocolJson.Serialize(new PipeRequest { Type = RequestTypes.Subscribe }));
                await reader.ReadLineAsync(token); // 첫 ok 응답

                string? line;
                while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync(token)) is not null)
                {
                    var ev = ProtocolJson.Deserialize<PipeEvent>(line);
                    if (ev is not null) Post(() => EventReceived?.Invoke(ev));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* 끊김 — 재연결 */ }

            try { await Task.Delay(Reconnect, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    /// <summary>
    /// 비밀번호를 보내기 전에 서버가 진짜 서비스인지 확인한다.
    /// 일반 사용자는 세션 0에 프로세스를 띄울 수 없으므로, 서버가 세션 0일 때만 신뢰한다.
    /// 확인할 수 없거나 세션 0이 아니면 거절한다(fail closed) — 가로채기한 가짜 서버에
    /// 비밀번호를 흘리지 않는 것이 우선이다. ProcessIdToSessionId 는 핸들을 열지 않아
    /// 일반 사용자도 SYSTEM 프로세스의 세션을 조회할 수 있다.
    /// </summary>
    private static bool ServerIsTrustworthy(NamedPipeClientStream pipe)
    {
        try
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) return false;
            if (!ProcessIdToSessionId(pid, out var sessionId)) return false;
            return sessionId == 0;
        }
        catch (Exception)
        {
            return false; // 확인 불가 → fail closed
        }
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
