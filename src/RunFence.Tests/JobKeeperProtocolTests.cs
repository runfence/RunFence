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
            new Dictionary<string, string> { ["A"] = "B" });

        JobKeeperProtocol.WriteMessage(stream, request);
        stream.Position = 0;
        var result = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(stream);

        Assert.NotNull(result);
        Assert.Equal(request.ExePath, result.ExePath);
        Assert.Equal(request.Arguments, result.Arguments);
        Assert.Equal(request.WorkingDirectory, result.WorkingDirectory);
        Assert.Equal(request.HideWindow, result.HideWindow);
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
}
