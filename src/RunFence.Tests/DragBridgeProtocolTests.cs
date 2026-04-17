using System.IO.Pipes;
using System.Text;
using RunFence.Core.Ipc;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeProtocolTests
{
    // --- Round-trip via MemoryStream ---

    [Fact]
    public async Task RoundTrip_SingleFile()
    {
        var data = new DragBridgeData { FilePaths = [@"C:\Users\alice\file.txt"] };

        var ms = new MemoryStream();
        await DragBridgeProtocol.WriteAsync(ms, data);
        ms.Position = 0;
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        Assert.Equal(data.FilePaths, result.FilePaths);
    }

    [Fact]
    public async Task RoundTrip_EmptyFileList()
    {
        var data = new DragBridgeData { FilePaths = [] };

        var ms = new MemoryStream();
        await DragBridgeProtocol.WriteAsync(ms, data);
        ms.Position = 0;
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        Assert.Empty(result.FilePaths);
    }

    [Theory]
    [InlineData(@"C:\Users\фёдор\документы\файл.txt")]
    [InlineData(@"C:\path with spaces\file name.txt")]
    [InlineData(@"C:\深度\测试\文件.docx")]
    public async Task RoundTrip_UnicodeFilePaths(string filePath)
    {
        var data = new DragBridgeData { FilePaths = [filePath] };

        var ms = new MemoryStream();
        await DragBridgeProtocol.WriteAsync(ms, data);
        ms.Position = 0;
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.Equal([filePath], result!.FilePaths);
    }

    [Fact]
    public async Task RoundTrip_LargeFileList()
    {
        var paths = Enumerable.Range(1, 500)
            .Select(i => $@"C:\folder\file_{i:D4}.txt")
            .ToList();
        var data = new DragBridgeData { FilePaths = paths };

        var ms = new MemoryStream();
        await DragBridgeProtocol.WriteAsync(ms, data);
        ms.Position = 0;
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.Equal(paths, result!.FilePaths);
    }

    [Fact]
    public async Task Read_TruncatedLengthPrefix_ReturnsNull()
    {
        var ms = new MemoryStream([0x00, 0x01]); // only 2 bytes instead of 4
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.Null(result);
    }

    [Fact]
    public async Task Read_EmptyStream_ReturnsNull()
    {
        var ms = new MemoryStream([]);
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.Null(result);
    }

    [Fact]
    public async Task Read_InvalidLength_Throws()
    {
        // Length of -1 (0xFFFFFFFF as signed)
        var ms = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]);
        await Assert.ThrowsAsync<InvalidDataException>(() => DragBridgeProtocol.ReadAsync(ms));
    }

    [Fact]
    public async Task Read_ZeroLength_Throws()
    {
        // Length prefix = 0 → invalid per protocol (we never send 0-length payloads)
        var ms = new MemoryStream([0x00, 0x00, 0x00, 0x00]);
        await Assert.ThrowsAsync<InvalidDataException>(() => DragBridgeProtocol.ReadAsync(ms));
    }

    [Fact]
    public async Task Deserialize_NullFilePathsField_ReturnsNullFilePaths()
    {
        // Simulate DragBridgeData with "FilePaths":null in JSON
        var json = """{"FilePaths":null}""";
        var payload = Encoding.UTF8.GetBytes(json);
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(payload.Length));
        ms.Write(payload);
        ms.Position = 0;

        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        Assert.Null(result.FilePaths);
    }

    [Fact]
    public async Task Deserialize_MissingFilePathsField_ReturnsEmptyList()
    {
        // JSON without FilePaths field → property initializer value is used
        var json = """{}""";
        var payload = Encoding.UTF8.GetBytes(json);
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(payload.Length));
        ms.Write(payload);
        ms.Position = 0;

        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        // Default initializer = [] so result is empty (not null) for missing field
        Assert.NotNull(result.FilePaths);
        Assert.Empty(result.FilePaths);
    }

    [Fact]
    public async Task RoundTrip_FilesResolved_True_Preserved()
    {
        // FilesResolved=true must survive serialization so the window starts pre-resolved.
        var data = new DragBridgeData { FilePaths = [@"C:\file.txt"], FilesResolved = true };

        var ms = new MemoryStream();
        await DragBridgeProtocol.WriteAsync(ms, data);
        ms.Position = 0;
        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        Assert.True(result.FilesResolved);
    }

    [Fact]
    public async Task Deserialize_MissingFilesResolvedField_DefaultsFalse()
    {
        // Older DragBridgeWindow.exe without FilesResolved in its JSON must still work —
        // absence of the field must deserialize to false (not throw).
        var json = """{"FilePaths":["C:\\file.txt"]}""";
        var payload = Encoding.UTF8.GetBytes(json);
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(payload.Length));
        ms.Write(payload);
        ms.Position = 0;

        var result = await DragBridgeProtocol.ReadAsync(ms);

        Assert.NotNull(result);
        Assert.False(result.FilesResolved);
    }

    // --- Round-trip via actual named pipe ---

    [Fact]
    public async Task NamedPipe_RoundTrip_Works()
    {
        var pipeName = $"TestDragBridge_{Guid.NewGuid():N}";
        var data = new DragBridgeData { FilePaths = [@"C:\file1.txt", @"C:\file2.txt"] };

        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await DragBridgeProtocol.WriteAsync(server, data);
        });

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        await serverTask;

        var result = await DragBridgeProtocol.ReadAsync(client);

        Assert.NotNull(result);
        Assert.Equal(data.FilePaths, result.FilePaths);
    }

    [Fact]
    public async Task MultipleMessages_SentAndReceived_InOrder()
    {
        var pipeName = $"TestDragBridge_{Guid.NewGuid():N}";
        var data1 = new DragBridgeData { FilePaths = [@"C:\a.txt"] };
        var data2 = new DragBridgeData { FilePaths = [@"C:\b.txt", @"C:\c.txt"] };

        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await DragBridgeProtocol.WriteAsync(server, data1);
            await DragBridgeProtocol.WriteAsync(server, data2);
        });

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        await serverTask;

        var r1 = await DragBridgeProtocol.ReadAsync(client);
        var r2 = await DragBridgeProtocol.ReadAsync(client);

        Assert.Equal(data1.FilePaths, r1!.FilePaths);
        Assert.Equal(data2.FilePaths, r2!.FilePaths);
    }
}