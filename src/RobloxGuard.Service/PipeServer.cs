using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Channels;
using RobloxGuard.Core;
using RobloxGuard.Core.Engine;
using RobloxGuard.Core.Protocol;

namespace RobloxGuard.Service;

/// <summary>
/// 명명된 파이프 서버. 연결마다 요청 한 건을 처리한다.
/// Subscribe 는 연결을 유지하며 엔진 이벤트를 밀어 보낸다.
///
/// 보안: 첫 인스턴스는 FirstPipeInstance 로 만들어 이름을 가로채이지 않았음을 보장한다.
/// 이미 누군가 같은 이름을 쓰고 있으면(스쿼팅) 제어 채널을 열지 않고 계속 재시도한다.
/// 프로세스 종료(ProcessWatcher)는 파이프와 무관하게 계속 돌므로 차단은 유지된다(fail closed).
/// </summary>
public sealed class PipeServer : BackgroundService
{
    private const int MaxServerInstances = 32;
    private const int MaxSubscribers = 4;      // 장기 연결(Subscribe) 동시 개수 상한
    private const int MaxRequestBytes = 8 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(2);

    private int _subscriberCount;

    private readonly GuardEngine _engine;
    private readonly ILogger<PipeServer> _log;

    public PipeServer(GuardEngine engine, ILogger<PipeServer> log)
    {
        _engine = engine;
        _log = log;
    }

    // 살아 있는 서버 인스턴스 수. 0이 되면 다음 생성 때 FirstPipeInstance 로 이름을 다시 주장한다.
    private int _liveInstances;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                // 현재 쥔 인스턴스가 없으면(최초/오류 복구) 이름을 FirstPipeInstance 로 다시 주장한다.
                server = CreateServer(firstInstance: Volatile.Read(ref _liveInstances) == 0);
                Interlocked.Increment(ref _liveInstances);
            }
            catch (IOException ex)
            {
                // 이름 선점 의심. 차단(ProcessWatcher)은 파이프와 무관하게 계속된다(fail closed).
                _log.LogCritical(ex, "파이프 이름 '{Pipe}' 이 이미 사용 중입니다(선점 의심). 재시도합니다.", GuardPaths.PipeName);
                try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { break; }
                continue;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "파이프 생성 실패");
                try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await DisposeInstanceAsync(server);
                break;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "연결 대기 실패");
                await DisposeInstanceAsync(server);
                continue;
            }

            _ = HandleConnectionAsync(server, stoppingToken);
        }
    }

    private async Task DisposeInstanceAsync(NamedPipeServerStream server)
    {
        await server.DisposeAsync();
        Interlocked.Decrement(ref _liveInstances);
    }

    private static NamedPipeServerStream CreateServer(bool firstInstance)
    {
        var security = new PipeSecurity();
        // Users: 상태 조회·구독·자가 차단용 읽기/쓰기. 권한이 필요한 명령은 비밀번호를 동반한다.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        var options = PipeOptions.Asynchronous;
        if (firstInstance) options |= PipeOptions.FirstPipeInstance;

        return NamedPipeServerStreamAcl.Create(
            GuardPaths.PipeName, PipeDirection.InOut, MaxServerInstances,
            PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            var writer = new StreamWriter(server) { AutoFlush = true };

            // 핸드셰이크 타임아웃: 연결만 하고 보내지 않는 클라이언트가 인스턴스를 영원히 점유하지 못하게.
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(HandshakeTimeout);

            var line = await ReadLineCappedAsync(reader, handshake.Token);
            var req = line is null ? null : ProtocolJson.Deserialize<PipeRequest>(line);
            if (req is null)
            {
                await writer.WriteLineAsync(ProtocolJson.Serialize(PipeResponse.Fail(ErrorCodes.BadRequest)));
                return;
            }

            if (req.Type == RequestTypes.Subscribe)
            {
                await HandleSubscribeAsync(server, writer, token);
                return;
            }

            var response = Dispatch(req);
            await writer.WriteLineAsync(ProtocolJson.Serialize(response));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* 상대가 끊음 */ }
        catch (Exception ex) { _log.LogDebug(ex, "연결 처리 실패"); }
        finally { await DisposeInstanceAsync(server); }
    }

    /// <summary>개행까지 읽되 최대 바이트를 넘으면 중단한다(무한 입력 방어).</summary>
    private static async Task<string?> ReadLineCappedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[256];
        var sb = new System.Text.StringBuilder();
        while (sb.Length < MaxRequestBytes)
        {
            var n = await reader.ReadAsync(buffer, token);
            if (n == 0) return sb.Length == 0 ? null : sb.ToString();
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] == '\n') return sb.ToString();
                if (buffer[i] != '\r') sb.Append(buffer[i]);
            }
        }
        return null; // 너무 긴 요청 → bad_request 로 처리됨
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

    private async Task HandleSubscribeAsync(NamedPipeServerStream server, StreamWriter writer, CancellationToken token)
    {
        // 장기 연결 상한. 일반 사용자가 Subscribe 를 여러 개 붙들어 인스턴스를 고갈시키고
        // 부모의 제어 연결을 막는 것을 방지한다. 초과하면 즉시 거절해 인스턴스를 돌려준다.
        if (Interlocked.Increment(ref _subscriberCount) > MaxSubscribers)
        {
            Interlocked.Decrement(ref _subscriberCount);
            await writer.WriteLineAsync(ProtocolJson.Serialize(PipeResponse.Fail(ErrorCodes.BadRequest)));
            return;
        }
        try
        {
            await SubscribeLoopAsync(server, writer, token);
        }
        finally
        {
            Interlocked.Decrement(ref _subscriberCount);
        }
    }

    private async Task SubscribeLoopAsync(NamedPipeServerStream server, StreamWriter writer, CancellationToken token)
    {
        using var conn = CancellationTokenSource.CreateLinkedTokenSource(token);
        // 구독자가 끊기면 즉시 알아채도록 읽기를 병행한다(다음 이벤트를 기다리지 않음).
        var disconnectWatch = WatchForDisconnectAsync(server, conn);

        var channel = Channel.CreateBounded<PipeEvent>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

        void OnEvent(PipeEvent ev) => channel.Writer.TryWrite(ev);
        _engine.EventRaised += OnEvent;
        try
        {
            await writer.WriteLineAsync(ProtocolJson.Serialize(PipeResponse.Success()));
            await foreach (var ev in channel.Reader.ReadAllAsync(conn.Token))
                await writer.WriteLineAsync(ProtocolJson.Serialize(ev));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            _engine.EventRaised -= OnEvent;
            conn.Cancel();
            try { await disconnectWatch; } catch { /* ignore */ }
        }
    }

    private static async Task WatchForDisconnectAsync(NamedPipeServerStream server, CancellationTokenSource conn)
    {
        var buf = new byte[1];
        try
        {
            // 구독 연결에서 클라이언트는 더 보내지 않는다. 읽기가 0(EOF)이면 끊긴 것.
            while (!conn.IsCancellationRequested)
            {
                var n = await server.ReadAsync(buf, conn.Token);
                if (n == 0) break;
            }
        }
        catch (Exception) { /* 끊김 */ }
        finally { conn.Cancel(); }
    }
}
