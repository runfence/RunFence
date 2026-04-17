using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="IpcConnectionProcessor"/> using real in-process named pipes
/// with a test-double <see cref="IIpcIdentityExtractor"/>.
///
/// Design note: The server side calls FlushAsync after writing a response, which blocks until
/// the client reads. Therefore client and server must run concurrently. Each test runs
/// the server-side ProcessConnectionAsync as a background task, while the client writes
/// and reads on the calling thread, then awaits the server task.
/// </summary>
public class IpcConnectionProcessorTests
{
    private const string TestSid1 = "S-1-5-21-100-200-300-1001";
    private const string TestSid2 = "S-1-5-21-100-200-300-1002";
    private const string TestIdentity1 = @"DOMAIN\User1";
    private const string TestIdentity2 = @"DOMAIN\User2";

    private static readonly IpcCallerContext CallerContext1 = new(TestIdentity1, TestSid1, false, true);
    private static readonly IpcCallerContext CallerContext2 = new(TestIdentity2, TestSid2, false, true);

    private readonly Mock<ILoggingService> _log = new();

    private IpcConnectionProcessor CreateProcessor(IIpcIdentityExtractor extractor)
        => new(_log.Object, extractor);

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

    private static async Task ConnectPipeAsync(NamedPipeServerStream server, NamedPipeClientStream client)
    {
        var serverConnectTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await serverConnectTask;
    }

    private static async Task WriteMessageAsync(NamedPipeClientStream client, IpcMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        await client.WriteAsync(bytes, 0, bytes.Length);
        await client.FlushAsync();
    }

    private static async Task<IpcResponse?> ReadResponseAsync(NamedPipeClientStream client)
    {
        var buffer = new byte[Constants.MaxPipeMessageSize];
        int bytesRead;
        try
        {
            bytesRead = await client.ReadAsync(buffer, 0, buffer.Length);
        }
        catch
        {
            return null;
        }
        if (bytesRead == 0)
            return null;
        var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return JsonSerializer.Deserialize<IpcResponse>(json, JsonDefaults.Options);
    }

    /// <summary>
    /// Runs a single client→server→client exchange: connects, sends a message, reads the response.
    /// The server side runs concurrently so that FlushAsync on the server doesn't deadlock waiting
    /// for the client to read.
    /// </summary>
    private static async Task<IpcResponse?> ExchangeAsync(
        IpcConnectionProcessor processor,
        IpcMessage message,
        Func<IpcMessage, IpcCallerContext, IpcResponse> dispatch)
    {
        var (server, client) = CreateMessageModePipePair();
        try
        {
            await ConnectPipeAsync(server, client);

            // Run server and client concurrently so neither side blocks waiting for the other
            var serverTask = processor.ProcessConnectionAsync(server, dispatch, CancellationToken.None);

            await WriteMessageAsync(client, message);
            var response = await ReadResponseAsync(client);
            await serverTask;

            return response;
        }
        finally
        {
            client.Dispose();
            server.Dispose();
        }
    }

    // --- Rate limiting per caller ---

    [Fact]
    public async Task SameCallerSid_ConsecutiveRequestsWithin100ms_SecondIsRateLimited()
    {
        // Arrange: both requests arrive from the same SID — use a shared processor instance
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Ping };

        var response1 = await ExchangeAsync(processor, message, Dispatch);

        // Second request immediately (< 100ms) — same SID, should be rate-limited
        var response2 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success, "First request should succeed");
        Assert.Equal(1, dispatchCount);
        Assert.False(response2?.Success, "Second request from same SID within 100ms should be rate-limited");
        Assert.Contains("rate", response2?.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DifferentCallerSids_ConsecutiveRequestsWithin100ms_BothProcessed()
    {
        // Arrange: first request uses Sid1, second uses Sid2 — different rate-limit buckets
        var extractor = new SequencedIpcIdentityExtractor([CallerContext1, CallerContext2]);
        var processor = CreateProcessor(extractor);

        var dispatchCount = 0;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            dispatchCount++;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Ping };

        var response1 = await ExchangeAsync(processor, message, Dispatch);
        var response2 = await ExchangeAsync(processor, message, Dispatch);

        Assert.True(response1?.Success, "First request (Sid1) should succeed");
        Assert.True(response2?.Success, "Second request (Sid2) should also succeed — different rate-limit bucket");
        Assert.Equal(2, dispatchCount);
    }

    // --- IdentityFromImpersonation = false context passthrough ---

    [Fact]
    public async Task ImpersonationFailed_ContextPassedThroughToDispatch()
    {
        // Arrange: extractor returns context with IdentityFromImpersonation = false and null SID.
        // The message includes CallerName which should be used as fallback CallerIdentity.
        var failedContext = new IpcCallerContext(null, null, false, IdentityFromImpersonation: false);
        var extractor = new FixedIpcIdentityExtractor(failedContext);
        var processor = CreateProcessor(extractor);

        IpcCallerContext? receivedContext = null;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            receivedContext = ctx;
            return new IpcResponse { Success = true };
        }

        var message = new IpcMessage { Command = IpcCommands.Ping, CallerName = "SomeUser" };

        await ExchangeAsync(processor, message, Dispatch);

        Assert.NotNull(receivedContext);
        Assert.False(receivedContext!.IdentityFromImpersonation, "IdentityFromImpersonation should be false");
        Assert.Null(receivedContext.CallerSid);
        // CallerName from message is applied as fallback CallerIdentity when impersonation fails
        Assert.Equal("SomeUser", receivedContext.CallerIdentity);
    }

    // --- Multi-chunk message assembly ---

    [Fact]
    public async Task MultiChunkMessage_ReassembledCorrectly()
    {
        // Arrange: build a message whose JSON exceeds Constants.MaxPipeMessageSize (64 KB).
        // AssembleMessage reads into a byte[MaxPipeMessageSize] buffer; if the message is
        // larger, IsMessageComplete = false after the first read and the while-loop reads
        // additional chunks until the full message is assembled. This test sends a message
        // of ~66 KB (AppId string of 66,000 'X' chars plus JSON overhead) to exercise that code path.
        var extractor = new FixedIpcIdentityExtractor(CallerContext1);
        var processor = CreateProcessor(extractor);

        IpcMessage? receivedMessage = null;
        IpcResponse Dispatch(IpcMessage msg, IpcCallerContext ctx)
        {
            receivedMessage = msg;
            return new IpcResponse { Success = true };
        }

        // MaxPipeMessageSize = 65,536 bytes. The JSON wrapper adds ~50 bytes of overhead
        // (keys, braces, newlines). Use 66,000-char AppId to reliably exceed the buffer.
        var longAppId = new string('X', 66_000);
        var message = new IpcMessage { Command = IpcCommands.Launch, AppId = longAppId };
        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > Constants.MaxPipeMessageSize,
            $"Test requires message > {Constants.MaxPipeMessageSize} bytes; got {bytes.Length}");
        Assert.True(bytes.Length <= 2L * Constants.MaxPipeMessageSize,
            "Message must not exceed the 2× overflow limit in AssembleMessage");

        var response = await ExchangeAsync(processor, message, Dispatch);

        // The processor must have reassembled the multi-chunk message correctly
        Assert.True(response?.Success);
        Assert.NotNull(receivedMessage);
        Assert.Equal(IpcCommands.Launch, receivedMessage!.Command);
        Assert.Equal(longAppId, receivedMessage.AppId);
    }
}
