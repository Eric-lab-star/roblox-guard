using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using RobloxGuard.Core;
using RobloxGuard.Core.Engine;
using RobloxGuard.Core.Protocol;

namespace RobloxGuard.Service;

/// <summary>
/// 명명된 파이프 서버. 연결마다 요청 한 건을 처리한다.
/// Subscribe 는 연결을 유지하며 엔진 이벤트를 밀어 보낸다.
/// </summary>
public sealed class PipeServer : BackgroundService
{
    private const int MaxServerInstances = 16;

    private readonly GuardEngine _engine;
    private readonly ILogger<PipeServer> _log;

    public PipeServer(GuardEngine engine, ILogger<PipeServer> log)
    {
        _engine = engine;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try { server = CreateServer(); }
            catch (Exception ex)
            {
                _log.LogError(ex, "파이프 생성 실패");
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "연결 대기 실패");
                await server.DisposeAsync();
                continue;
            }

            _ = HandleConnectionAsync(server, stoppingToken);
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            GuardPaths.PipeName, PipeDirection.InOut, MaxServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            var writer = new StreamWriter(server) { AutoFlush = true };

            var line = await reader.ReadLineAsync(token);
            var req = line is null ? null : ProtocolJson.Deserialize<PipeRequest>(line);
            if (req is null)
            {
                await writer.WriteLineAsync(ProtocolJson.Serialize(PipeResponse.Fail(ErrorCodes.BadRequest)));
                return;
            }

            if (req.Type == RequestTypes.Subscribe)
            {
                await HandleSubscribeAsync(writer, token);
                return;
            }

            var response = Dispatch(req);
            await writer.WriteLineAsync(ProtocolJson.Serialize(response));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* 상대가 끊음 */ }
        catch (Exception ex) { _log.LogDebug(ex, "연결 처리 실패"); }
        finally { await server.DisposeAsync(); }
    }

    private PipeResponse Dispatch(PipeRequest req) => req.Type switch
    {
        RequestTypes.GetStatus => _engine.GetStatus(),
        RequestTypes.Allow => _engine.Allow(req.Password, req.Minutes),
        RequestTypes.Block => _engine.Block(),
        RequestTypes.GetLog => _engine.GetLog(req.Password, req.Days),
        RequestTypes.ChangePassword => _engine.ChangePassword(req.OldPassword, req.NewPassword),
        _ => PipeResponse.Fail(ErrorCodes.BadRequest),
    };

    private async Task HandleSubscribeAsync(StreamWriter writer, CancellationToken token)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PipeEvent>();

        void OnEvent(PipeEvent ev) => channel.Writer.TryWrite(ev);
        _engine.EventRaised += OnEvent;
        try
        {
            await writer.WriteLineAsync(ProtocolJson.Serialize(PipeResponse.Success()));
            await foreach (var ev in channel.Reader.ReadAllAsync(token))
                await writer.WriteLineAsync(ProtocolJson.Serialize(ev));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* 구독자가 끊김 */ }
        finally { _engine.EventRaised -= OnEvent; }
    }
}
