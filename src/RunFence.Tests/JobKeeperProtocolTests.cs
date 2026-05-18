using System.Text;
using System.Text.Json;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperProtocolTests
{
    [Fact]
    public void WriteThenRead_RoundTripsLaunchRequest()
    {
        using var stream = new MemoryStream();
        var request = new JobKeeperLaunchRequest(
            @"C:\Apps\App.exe",
            "--flag",
            @"C:\Apps",
            HideWindow: true,
            SuppressStartupFeedback: true,
            new Dictionary<string, string> { ["A"] = "B" });

        JobKeeperProtocol.WriteMessage(stream, request);
        stream.Position = 0;
        var result = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(stream);

        Assert.NotNull(result);
        Assert.Equal(request.ExePath, result.ExePath);
        Assert.Equal(request.Arguments, result.Arguments);
        Assert.Equal(request.WorkingDirectory, result.WorkingDirectory);
        Assert.Equal(request.HideWindow, result.HideWindow);
        Assert.Equal(request.SuppressStartupFeedback, result.SuppressStartupFeedback);
        Assert.Equal("B", result.EnvOverrides?["A"]);
    }

    [Fact]
    public void ReadMessage_OversizedPayload_ThrowsIOException()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(10 * 1024 * 1024 + 1));
        stream.Position = 0;

        var ex = Assert.Throws<IOException>(() => JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(stream));

        Assert.Contains("Invalid message length", ex.Message);
    }

    [Fact]
    public void ReadMessage_TruncatedPayload_ThrowsIOException()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(10));
        stream.Write(Encoding.UTF8.GetBytes("{}"));
        stream.Position = 0;

        Assert.Throws<IOException>(() => JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(stream));
    }

    [Fact]
    public void ReadMessage_InvalidJson_ThrowsJsonException()
    {
        using var stream = new MemoryStream();
        var invalidJson = Encoding.UTF8.GetBytes("{bad");
        stream.Write(BitConverter.GetBytes(invalidJson.Length));
        stream.Write(invalidJson);
        stream.Position = 0;

        Assert.Throws<JsonException>(() => JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(stream));
    }

    [Fact]
    public async Task ReadMessageAsync_CancelledRead_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JobKeeperProtocol.ReadMessageAsync<JobKeeperLaunchRequest>(new BlockingReadStream(), cts.Token));
    }

    [Fact]
    public async Task WriteMessageAsync_CancelledWrite_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JobKeeperProtocol.WriteMessageAsync(
                new BlockingWriteStream(),
                new JobKeeperLaunchRequest(@"C:\Apps\App.exe", null, null, false, false, null),
                cts.Token));
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var blocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            return await blocked.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await blocked.Task.WaitAsync(cancellationToken);
        }
    }
}
