using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="IpcConnectionProcessor"/> using real in-process named pipes
/// with a test-double <see cref="IIpcIdentityExtractor"/>.
/// </summary>
public class IpcConnectionProcessorTests
{
    private const string TestSid1 = "S-1-5-21-100-200-300-1001";
    private const string TestSid2 = "S-1-5-21-100-200-300-1002";
    private const string TestIdentity1 = @"DOMAIN\User1";
    private const string TestIdentity2 = @"DOMAIN\User2";

    private static readonly IpcCallerContext CallerContext1 = new(TestIdentity1, TestSid1, false, true);
    private static readonly IpcCallerContext CallerContext2 = new(TestIdentity2, TestSid2, false, true);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private readonly Mock<ILoggingService> _log = new();

    private IpcConnectionProcessor CreateProcessor(IIpcIdentityExtractor extractor)
        => CreateProcessor(extractor, new SystemClock());

    private IpcConnectionProcessor CreateProcessor(IIpcIdentityExtractor extractor, IClock clock)
        => new(_log.Object, extractor, clock);

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreateMessageModePipePair()
    {
        var pipeName = $"RunFenceTest_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        return (server, client);
    }

    private static async Task ConnectPipeAsync(
        NamedPipeServerStream server,
        NamedPipeClientStream client,
        CancellationToken ct = default)
    {
        var serverConnectTask = server.WaitForConnectionAsync(ct);
        await client.ConnectAsync(ct);
        await serverConnectTask;
    }

    private static async Task WriteMessageAsync(NamedPipeClientStream client, IpcMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        await WriteBytesAsync(client, bytes, ct);
    }

    private static async Task WriteBytesAsync(NamedPipeClientStream client, byte[] bytes, CancellationToken ct = default)
    {
        await client.WriteAsync(bytes, 0, bytes.Length, ct);
        await client.FlushAsync(ct);
    }

    private static async Task<IpcResponse?> ReadResponseAsync(NamedPipeClientStream client, CancellationToken ct = default)
    {
        var buffer = new byte[IpcConstants.MaxPipeMessageSize];
        int bytesRead;
        try
        {
            bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, ct);
        }
        catch
        {
            return null;
        }

        if (bytesRead == 0)
            return null;

        if (bytesRead == 1 && buffer[0] == IpcCommands.RateLimitedSignal)
            return new IpcResponse { Success = false, ErrorMessage = "Rate limited. Try again." };

        var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return JsonSerializer.Deserialize<IpcResponse>(json, JsonDefaults.Options);
    }

    private static async Task<IpcResponse?> ExchangeAsync(
        IpcConnectionProcessor processor,
        IpcMessage message,
        Func<IpcMessage, IpcCallerContext, IpcResponse> dispatch,
        CancellationToken cancellationToken = default,
        bool expectResponse = true)
    {
        var (server, client) = CreateMessageModePipePair();
        try
        {
            using var timeoutCts = new CancellationTokenSource(OperationTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            await ConnectPipeAsync(server, client, linkedCts.Token);

            var serverTask = processor.ProcessConnectionAsync(server, dispatch, linkedCts.Token);

            await WriteMessageAsync(client, message, linkedCts.Token);
            var response = expectResponse ? await ReadResponseAsync(client, linkedCts.Token) : null;

            await serverTask.WaitAsync(linkedCts.Token);

            return response;
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    private static async Task<bool> ExchangePingAsync(
        IpcConnectionProcessor processor,
        Func<IpcMessage, IpcCallerContext, IpcResponse> dispatch)
    {
        var (server, client) = CreateMessageModePipePair();
        try
        {
            using var timeoutCts = new CancellationTokenSource(OperationTimeout);

            await ConnectPipeAsync(server, client, timeoutCts.Token);
            var serverTask = processor.ProcessConnectionAsync(server, dispatch, timeoutCts.Token);

            await client.WriteAsync(IpcCommands.PingBytes, 0, IpcCommands.PingBytes.Length, timeoutCts.Token);
            await client.FlushAsync(timeoutCts.Token);

            var buffer = new byte[16];
            int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
            await serverTask.WaitAsync(timeoutCts.Token);

            return buffer.AsSpan(0, bytesRead).SequenceEqual(IpcCommands.PongBytes);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    private static async Task<IpcResponse?> ExchangeRawRequestAsync(
        IpcConnectionProcessor processor,
        byte[] requestBytes,
        Func<IpcMessage, IpcCallerContext, IpcResponse> dispatch,
        bool expectResponse = true)
    {
        var (server, client) = CreateMessageModePipePair();
        try
        {
            using var timeoutCts = new CancellationTokenSource(OperationTimeout);
            await ConnectPipeAsync(server, client, timeoutCts.Token);

            var serverTask = processor.ProcessConnectionAsync(server, dispatch, timeoutCts.Token);

            await WriteBytesAsync(client, requestBytes, timeoutCts.Token);
            var response = expectResponse ? await ReadResponseAsync(client, timeoutCts.Token) : null;

            await serverTask.WaitAsync(timeoutCts.Token);
            return response;
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task SameCallerSid_ConsecutiveNonPingRequestsAtSameTimestamp_SecondIsRateLimited()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var processor = CreateProcessor(extractor, clock);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };
        var response1 = await ExchangeAsync(processor, message, Dispatch);
        var response2 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success, "First request should succeed.");
        Assert.Equal(1, dispatchCount);
        Assert.False(response2?.Success, "Second request at same timestamp should be rate-limited.");
        Assert.Contains("rate", response2?.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameCallerSid_RequestAfter100ms_SecondRequestIsAllowed()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var processor = CreateProcessor(extractor, clock);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };
        var response1 = await ExchangeAsync(processor, message, Dispatch);
        clock.Advance(TimeSpan.FromMilliseconds(101));
        var response2 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success);
        Assert.True(response2?.Success);
        Assert.Equal(2, dispatchCount);
    }

    [Fact]
    public async Task PingFollowedByNonPingCommand_NonPingCommandIsNotRateLimited()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var processor = CreateProcessor(extractor, clock);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var realMessage = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };

        var pingOk = await ExchangePingAsync(processor, Dispatch);
        var realResponse = await ExchangeAsync(processor, realMessage, Dispatch);

        Assert.True(pingOk, "Ping should receive PONG.");
        Assert.True(realResponse?.Success, "Real command immediately after ping should not be rate-limited.");
        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    public async Task DifferentCallerSids_ConsecutiveRequestsWithin100ms_BothProcessed()
    {
        var extractor = new SequencedIpcIdentityExtractor([CallerContext1, CallerContext2]);
        var processor = CreateProcessor(extractor);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };

        var response1 = await ExchangeAsync(processor, message, Dispatch);
        var response2 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success, "First request (Sid1) should succeed.");
        Assert.True(response2?.Success, "Second request (Sid2) should succeed.");
        Assert.Equal(2, dispatchCount);
    }

    [Fact]
    public async Task AdvancePast10Seconds_TriggersCleanupPath_AndRequestStillSucceeds()
    {
        var extractor = new SequencedIpcIdentityExtractor([CallerContext1, CallerContext2, CallerContext1]);
        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var processor = CreateProcessor(extractor, clock);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };

        var response1 = await ExchangeAsync(processor, message, Dispatch);
        var response2 = await ExchangeAsync(processor, message, Dispatch);
        clock.Advance(TimeSpan.FromSeconds(11));
        var response3 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success);
        Assert.True(response2?.Success);
        Assert.True(response3?.Success);
        Assert.Equal(3, dispatchCount);
    }

    [Fact]
    public async Task ImpersonationFailed_ContextPassedThroughToDispatch()
    {
        var failedContext = new IpcCallerContext(null, null, false, IdentityFromImpersonation: false);
        var extractor = new FixedIpcIdentityExtractor(failedContext);
        var processor = CreateProcessor(extractor);

        IpcCallerContext? receivedContext = null;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            receivedContext = ctx;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1", CallerName = "SomeUser" };

        await ExchangeAsync(processor, message, Dispatch);

        Assert.NotNull(receivedContext);
        Assert.False(receivedContext!.IdentityFromImpersonation);
        Assert.Null(receivedContext.CallerSid);
        Assert.Equal("SomeUser", receivedContext.CallerIdentity);
    }

    [Fact]
    public async Task MultiChunkMessage_ReassembledCorrectly()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);

        IpcMessage? receivedMessage = null;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            receivedMessage = msg;
            return new IpcResponse { Success = true };
        }

        var longAppId = new string('X', 66_000);
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = longAppId };
        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > IpcConstants.MaxPipeMessageSize,
            $"Test requires message > {IpcConstants.MaxPipeMessageSize} bytes; got {bytes.Length}.");
        Assert.True(bytes.Length <= 2L * IpcConstants.MaxPipeMessageSize,
            "Message must not exceed the 2x overflow limit in AssembleMessage.");

        var response = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response?.Success);
        Assert.NotNull(receivedMessage);
        Assert.Equal(IpcCommands.Launch, receivedMessage!.Command);
        Assert.Equal(longAppId, receivedMessage.AppId);
    }

    [Fact]
    public async Task ProcessConnectionAsync_DropsOversizedMessageWithoutResponse()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage _, IpcCallerContext __)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var oversized = new byte[IpcConstants.MaxPipeMessageSize * 2 + 1];
        Array.Fill(oversized, (byte)'x');

        var response = await ExchangeRawRequestAsync(processor, oversized, Dispatch, expectResponse: false);

        Assert.Null(response);
        Assert.Equal(0, dispatchCount);
    }

    [Fact]
    public async Task ProcessConnectionAsync_InvalidJsonReturnsInternalError()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage _, IpcCallerContext __)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var invalidJson = Encoding.UTF8.GetBytes("{not-json");
        var response = await ExchangeRawRequestAsync(processor, invalidJson, Dispatch, expectResponse: true);

        Assert.Equal(0, dispatchCount);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("Internal error.", response.ErrorMessage);
    }

    [Fact]
    public async Task ProcessConnectionAsync_DispatchExceptionReturnsInternalError()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };

        IpcResponse Dispatch(IpcMessage _, IpcCallerContext __)
            => throw new InvalidOperationException("dispatch failed");

        var response = await ExchangeAsync(processor, message, Dispatch);

        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("Internal error.", response.ErrorMessage);
    }

    [Fact]
    public async Task ProcessConnectionAsync_RespectsCancellationToken()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);
        var (server, client) = CreateMessageModePipePair();

        using var cancellation = new CancellationTokenSource();
        try
        {
            await ConnectPipeAsync(server, client);
            cancellation.Cancel();

            var processorTask = processor.ProcessConnectionAsync(
                server,
                (_, _) => new IpcResponse { Success = true },
                cancellation.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => processorTask.WaitAsync(OperationTimeout));
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProcessConnectionAsync_WithoutPayloadDisconnectDoesNotHang()
    {
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);
        var (server, client) = CreateMessageModePipePair();

        using var timeoutCts = new CancellationTokenSource(OperationTimeout);
        try
        {
            await ConnectPipeAsync(server, client, timeoutCts.Token);

            var processorTask = processor.ProcessConnectionAsync(
                server,
                (_, _) => new IpcResponse { Success = true },
                timeoutCts.Token);

            await client.DisposeAsync();

            await processorTask.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task SameCallerSid_ConcurrentRequestsAtSameTimestamp_OnlyOneDispatches()
    {
        var extractor = new BarrierIpcIdentityExtractor(CallerContext1, participants: 2);
        var clock = new ManualClock(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        var processor = CreateProcessor(extractor, clock);
        var dispatchCount = 0;

        IpcResponse Dispatch(IpcMessage _, IpcCallerContext __)
        {
            Interlocked.Increment(ref dispatchCount);
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = "app1" };
        var responseTasks = new[]
        {
            ExchangeAsync(processor, message, Dispatch),
            ExchangeAsync(processor, message, Dispatch)
        };

        var responses = await Task.WhenAll(responseTasks);

        Assert.Equal(1, dispatchCount);
        Assert.Equal(1, responses.Count(r => r?.Success == true));
        Assert.Equal(1, responses.Count(r => r?.Success == false && (r.ErrorMessage ?? string.Empty).Contains("rate", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class BarrierIpcIdentityExtractor(IpcCallerContext context, int participants) : IIpcIdentityExtractor
    {
        private readonly Barrier _barrier = new(participants);

        public IpcCallerContext Extract(NamedPipeServerStream pipe)
        {
            _barrier.SignalAndWait(TimeSpan.FromSeconds(1));
            return context;
        }
    }
}
