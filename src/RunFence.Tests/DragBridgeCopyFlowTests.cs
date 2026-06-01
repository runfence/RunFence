using System.IO.Pipes;
using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.DragBridge;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeCopyFlowTests
{
    // ── GetCapturedFiles — captured file state ───────────────────────────

    [Fact]
    public void GetCapturedFiles_WithinFiveMinutes_ReturnsCapturedFilesAndSid()
    {
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        var files = new List<string> { @"C:\file1.txt", @"C:\file2.txt" };
        store.SetCapturedFiles(files, "S-1-5-21-1-2-3-4", null);
        fakeTick = 4 * 60_000; // advance to 4 minutes — still within 5-minute expiry

        var (captured, sourceSid, sourceContainerSid, expired) = store.GetCapturedFiles();

        Assert.Equal(files, captured);
        Assert.Equal("S-1-5-21-1-2-3-4", sourceSid);
        Assert.Null(sourceContainerSid);
        Assert.False(expired);
    }

    [Fact]
    public void GetCapturedFiles_WithinFiveMinutes_ReturnsCapturedContainerSid()
    {
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        var files = new List<string> { @"C:\file1.txt" };
        store.SetCapturedFiles(files, "S-1-5-21-1-2-3-4", "S-1-15-2-42");
        fakeTick = 4 * 60_000;

        var (captured, sourceSid, sourceContainerSid, expired) = store.GetCapturedFiles();

        Assert.Equal(files, captured);
        Assert.Equal("S-1-5-21-1-2-3-4", sourceSid);
        Assert.Equal("S-1-15-2-42", sourceContainerSid);
        Assert.False(expired);
    }

    [Fact]
    public void SetCapturedFiles_CallerMutationAfterCapture_DoesNotChangeStoredSnapshot()
    {
        var store = new CapturedFileStore(() => 0);
        var files = new List<string> { @"C:\file1.txt" };
        store.SetCapturedFiles(files, "S-1-5-21-1-2-3-4", null);
        files.Add(@"C:\file2.txt");

        var (captured, _, _, _) = store.GetCapturedFiles();

        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal(@"C:\file1.txt", captured[0]);
    }

    [Fact]
    public void GetCapturedFiles_AfterFiveMinutes_ReturnsExpiredAndNullPaths()
    {
        // Simulate time: capture at tick 0, read at tick 6 minutes later
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        store.SetCapturedFiles([@"C:\file1.txt"], "S-1-5-21-1-2-3-4", null);
        fakeTick = 6 * 60_000; // advance time by 6 minutes

        var (captured, sourceSid, sourceContainerSid, expired) = store.GetCapturedFiles();

        Assert.Null(captured);
        Assert.Null(sourceSid);
        Assert.Null(sourceContainerSid);
        Assert.True(expired);
    }

    [Fact]
    public void GetCapturedFiles_CalledTwiceAfterExpiry_SecondCallNotExpired()
    {
        // After expiry, state is cleared. A second call should return not-expired (no files, no expiry).
        long fakeTick = 0;
        var store = new CapturedFileStore(() => fakeTick);
        store.SetCapturedFiles(["file.txt"], "S-1-2-3", null);
        fakeTick = 6 * 60_000; // advance time past expiry

        store.GetCapturedFiles(); // clears state
        var (captured, sourceSid, sourceContainerSid, expired) = store.GetCapturedFiles();

        Assert.Null(captured);
        Assert.Null(sourceSid);
        Assert.Null(sourceContainerSid);
        Assert.False(expired);
    }

    // ── BuildArgs — arg format ────────────────────────────────────────────

    private static List<string> InvokeBuildArgs(string pipeName, Point cursorPos, nint restoreHwnd = 0)
        => DragBridgeCopyFlow.BuildArgs(pipeName, cursorPos, restoreHwnd);

    [Fact]
    public void BuildArgs_ContainsRequiredFlagsAndValues()
    {
        var result = InvokeBuildArgs("TestPipe-abc123", new Point(123, 456), restoreHwnd: 12345);

        Assert.Contains("--pipe", result);
        Assert.Contains("TestPipe-abc123", result);
        Assert.Contains("--x", result);
        Assert.Contains("--y", result);
        Assert.Contains("--runfence-pid", result);
        var pidIdx = result.IndexOf("--runfence-pid");
        Assert.Equal(Environment.ProcessId.ToString(), result[pidIdx + 1]);
        var hwndIdx = result.IndexOf("--restore-hwnd");
        Assert.True(hwndIdx >= 0, "--restore-hwnd flag must be present");
        Assert.Equal("12345", result[hwndIdx + 1]);
    }

    [Theory]
    [InlineData("RunFence-DragBridge-unique", 10, 20, "10", "20")]
    [InlineData("pipe1", 0, 0, "0", "0")]
    [InlineData("pipe2", -5, -10, "-5", "-10")]
    public void BuildArgs_PipeNameAndCoordinatesFormattedCorrectly(
        string pipeName, int x, int y, string expectedX, string expectedY)
    {
        var result = InvokeBuildArgs(pipeName, new Point(x, y));

        var pipeIdx = result.IndexOf("--pipe");
        var xIdx = result.IndexOf("--x");
        var yIdx = result.IndexOf("--y");

        Assert.True(pipeIdx >= 0, "--pipe flag must be present");
        Assert.True(xIdx >= 0, "--x flag must be present");
        Assert.True(yIdx >= 0, "--y flag must be present");

        Assert.Equal(pipeName, result[pipeIdx + 1]);
        Assert.Equal(expectedX, result[xIdx + 1]);
        Assert.Equal(expectedY, result[yIdx + 1]);
    }

    // ── RunBridgeAsync — bridge flow scenarios ────────────────────────────

    private static readonly SecurityIdentifier TestSid = new("S-1-5-32-545");
    private static readonly WindowOwnerInfo TestOwner = new(TestSid, 0x2000, false);

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreatePipePair()
    {
        var pipeName = $"RunFenceTest_DragBridgeFlow_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        return (server, client);
    }

    private static Mock<IDragBridgePipeLauncher> CreateLauncher(
        NamedPipeServerStream server, bool verifyResult)
    {
        var launcher = new Mock<IDragBridgePipeLauncher>();
        launcher.Setup(l => l.CreatePipeServer(It.IsAny<string>(), TestSid, null, It.IsAny<bool>())).Returns(server);
        launcher.Setup(l => l.LaunchForSid(It.IsAny<WindowOwnerInfo>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<INotificationService>())).Returns((ProcessInfo?)null);
        launcher.Setup(l => l.VerifyClientProcess(It.IsAny<NamedPipeServerStream>(), null)).Returns(verifyResult);
        return launcher;
    }

    [Fact]
    public async Task RunBridgeAsync_ConnectionTimeout_ReturnsWithoutCapture()
    {
        // Arrange: launcher returns a real unconnected pipe server; no client ever connects
        // within the short timeout → WaitForConnectionAsync is cancelled by the timeout CTS.
        var pipeName = $"RunFenceTest_Timeout_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

        var launcher = new Mock<IDragBridgePipeLauncher>();
        launcher.Setup(l => l.CreatePipeServer(It.IsAny<string>(), TestSid, null, It.IsAny<bool>())).Returns(server);
        launcher.Setup(l => l.LaunchForSid(It.IsAny<WindowOwnerInfo>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<INotificationService>())).Returns((ProcessInfo?)null);
        launcher.Setup(l => l.KillProcess(null));

        var notifications = new Mock<INotificationService>();
        var log = new Mock<ILoggingService>();
        var capturedFileStore = new Mock<ICapturedFileStore>();

        var flow = new DragBridgeCopyFlow(
            launcher.Object, notifications.Object, log.Object, capturedFileStore.Object,
            pipeConnectTimeoutMs: 50); // very short timeout — client never connects

        // Act
        await flow.RunBridgeAsync(TestOwner, [], new Point(0, 0), null, false, 0, CancellationToken.None);

        // Assert: no files captured when the pipe never connects
        capturedFileStore.Verify(s => s.SetCapturedFiles(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(NativeTokenHelper.MandatoryLevelLow, true, true)]
    [InlineData(NativeTokenHelper.MandatoryLevelLow, false, true)]
    [InlineData(NativeTokenHelper.MandatoryLevelMedium, true, false)]
    public async Task RunBridgeAsync_CreatesLowIntegrityPipeOnlyForLowIntegrityOwner(
        int integrityLevel,
        bool isInRestrictedJob,
        bool expectedAllowLowIntegrityClient)
    {
        var pipeName = $"RunFenceTest_LowIlFlag_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

        var owner = new WindowOwnerInfo(TestSid, integrityLevel, isInRestrictedJob);
        var launcher = new Mock<IDragBridgePipeLauncher>();
        launcher.Setup(l => l.CreatePipeServer(It.IsAny<string>(), TestSid, null, expectedAllowLowIntegrityClient))
            .Returns(server);
        launcher.Setup(l => l.LaunchForSid(It.IsAny<WindowOwnerInfo>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<INotificationService>())).Returns((ProcessInfo?)null);

        var flow = new DragBridgeCopyFlow(
            launcher.Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggingService>().Object,
            new Mock<ICapturedFileStore>().Object,
            pipeConnectTimeoutMs: 50);

        await flow.RunBridgeAsync(owner, [], new Point(0, 0), null, false, 0, CancellationToken.None);

        launcher.Verify(l => l.CreatePipeServer(It.IsAny<string>(), TestSid, null, expectedAllowLowIntegrityClient), Times.Once);
    }

    [Fact]
    public async Task RunBridgeAsync_AppContainerOwner_PassesContainerSidToPipeServer()
    {
        const string containerSid = "S-1-15-2-42";
        var pipeName = $"RunFenceTest_AppContainerPipe_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024);

        var owner = new WindowOwnerInfo(TestSid, NativeTokenHelper.MandatoryLevelMedium, false, new SecurityIdentifier(containerSid));
        var launcher = new Mock<IDragBridgePipeLauncher>();
        launcher.Setup(l => l.CreatePipeServer(It.IsAny<string>(), TestSid, It.Is<SecurityIdentifier?>(sid => sid != null && sid.Value == containerSid), false))
            .Returns(server);
        launcher.Setup(l => l.LaunchForSid(It.IsAny<WindowOwnerInfo>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<INotificationService>())).Returns((ProcessInfo?)null);
        launcher.Setup(l => l.KillProcess(null));

        var flow = new DragBridgeCopyFlow(
            launcher.Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggingService>().Object,
            new Mock<ICapturedFileStore>().Object,
            pipeConnectTimeoutMs: 50);

        await flow.RunBridgeAsync(owner, [], new Point(0, 0), null, false, 0, CancellationToken.None);

        launcher.Verify(l => l.CreatePipeServer(
            It.IsAny<string>(),
            TestSid,
            It.Is<SecurityIdentifier?>(sid => sid != null && sid.Value == containerSid),
            false), Times.Once);
    }

    [Fact]
    public async Task RunBridgeAsync_ClientVerificationFails_ReturnsWithoutCapture()
    {
        // Arrange: client connects but VerifyClientProcess returns false → flow returns early.
        var (server, client) = CreatePipePair();
        await using var _1 = server;
        await using var _2 = client;

        var launcher = CreateLauncher(server, verifyResult: false);
        var notifications = new Mock<INotificationService>();
        var log = new Mock<ILoggingService>();
        var capturedFileStore = new Mock<ICapturedFileStore>();

        var flow = new DragBridgeCopyFlow(
            launcher.Object, notifications.Object, log.Object, capturedFileStore.Object);

        // Connect the client concurrently so WaitForConnectionAsync returns
        var connectTask = Task.Run(() => client.ConnectAsync(5000));

        // Act
        await flow.RunBridgeAsync(TestOwner, [], new Point(0, 0), null, false, 0, CancellationToken.None);
        await connectTask;

        // Assert: no files captured and the bridge never signals ready
        capturedFileStore.Verify(s => s.SetCapturedFiles(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        launcher.Verify(l => l.SignalReady(It.IsAny<NamedPipeServerStream>()), Times.Never);
    }

    [Fact]
    public async Task RunBridgeAsync_ResolveRequest_InvokesResolveDelegateAndWritesResult()
    {
        // Arrange: client sends a ResolveRequest message; the resolve delegate is called and
        // the resolved paths are written back on the pipe.
        var (server, client) = CreatePipePair();
        await using var _1 = server;
        await using var _2 = client;

        var resolvedPaths = new List<string> { @"C:\resolved\file.txt" };
        Func<CancellationToken, Task<List<string>?>> resolveDelegate =
            _ => Task.FromResult<List<string>?>(resolvedPaths);

        var launcher = CreateLauncher(server, verifyResult: true);
        var notifications = new Mock<INotificationService>();
        var log = new Mock<ILoggingService>();
        var capturedFileStore = new Mock<ICapturedFileStore>();

        var flow = new DragBridgeCopyFlow(
            launcher.Object, notifications.Object, log.Object, capturedFileStore.Object);

        // Simulate client: connect, consume initial FileList, send ResolveRequest, read response.
        var clientTask = Task.Run(async () =>
        {
            await client.ConnectAsync(5000);

            // Read initial FileList written by the flow after verification
            var initial = await DragBridgeProtocol.ReadAsync(client);
            Assert.NotNull(initial);
            Assert.Equal(DragBridgeMessageType.FileList, initial!.MessageType);

            // Send a ResolveRequest
            await DragBridgeProtocol.WriteAsync(client,
                new DragBridgeData { MessageType = DragBridgeMessageType.ResolveRequest });

            // Read the resolved paths sent back by the flow
            var response = await DragBridgeProtocol.ReadAsync(client);
            Assert.NotNull(response);
            Assert.Equal(resolvedPaths, response!.FilePaths);

            // Close client stream to end the bridge read loop
            client.Close();
        });

        // Act
        await flow.RunBridgeAsync(TestOwner, [], new Point(0, 0), resolveDelegate, false, 0, CancellationToken.None);
        await clientTask;

        // Assert: resolve delegate was invoked (verified by client receiving resolved paths)
        capturedFileStore.Verify(s => s.SetCapturedFiles(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task RunBridgeAsync_FileListMessage_StoresCapturedFiles()
    {
        // Arrange: client sends a FileList drop message; files are stored in the captured file store.
        var (server, client) = CreatePipePair();
        await using var _1 = server;
        await using var _2 = client;

        var droppedFiles = new List<string> { @"C:\user\file1.txt", @"C:\user\file2.txt" };

        var launcher = CreateLauncher(server, verifyResult: true);
        var notifications = new Mock<INotificationService>();
        var log = new Mock<ILoggingService>();
        var capturedFileStore = new Mock<ICapturedFileStore>();

        var flow = new DragBridgeCopyFlow(
            launcher.Object, notifications.Object, log.Object, capturedFileStore.Object);

        var clientTask = Task.Run(async () =>
        {
            await client.ConnectAsync(5000);

            // Consume initial FileList written by the flow
            await DragBridgeProtocol.ReadAsync(client);

            // Send a drop (FileList) with actual file paths
            await DragBridgeProtocol.WriteAsync(client,
                new DragBridgeData { FilePaths = droppedFiles });

            // Close client to end the bridge read loop
            client.Close();
        });

        // Act
        await flow.RunBridgeAsync(TestOwner, [], new Point(0, 0), null, false, 0, CancellationToken.None);
        await clientTask;

        // Assert: dropped files captured with the owner SID
        capturedFileStore.Verify(
            s => s.SetCapturedFiles(
                It.Is<IReadOnlyList<string>>(files => files.SequenceEqual(droppedFiles)),
                TestSid.Value,
                null),
            Times.Once);
    }

    [Fact]
    public async Task RunBridgeAsync_FileListMessage_AppContainerOwner_StoresCapturedSourceContainerSid()
    {
        var (server, client) = CreatePipePair();
        await using var _1 = server;
        await using var _2 = client;
        var containerSid = new SecurityIdentifier("S-1-15-2-42");
        var owner = new WindowOwnerInfo(TestSid, NativeTokenHelper.MandatoryLevelMedium, false, containerSid);
        var droppedFiles = new List<string> { @"C:\user\file1.txt" };

        var launcher = new Mock<IDragBridgePipeLauncher>();
        launcher.Setup(l => l.CreatePipeServer(
                It.IsAny<string>(),
                TestSid,
                It.Is<SecurityIdentifier?>(sid => sid != null && sid.Value == containerSid.Value),
                false))
            .Returns(server);
        launcher.Setup(l => l.LaunchForSid(It.IsAny<WindowOwnerInfo>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<INotificationService>())).Returns((ProcessInfo?)null);
        launcher.Setup(l => l.VerifyClientProcess(It.IsAny<NamedPipeServerStream>(), null)).Returns(true);
        var capturedFileStore = new Mock<ICapturedFileStore>();
        var flow = new DragBridgeCopyFlow(
            launcher.Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggingService>().Object,
            capturedFileStore.Object);

        var clientTask = Task.Run(async () =>
        {
            await client.ConnectAsync(5000);
            await DragBridgeProtocol.ReadAsync(client);
            await DragBridgeProtocol.WriteAsync(client, new DragBridgeData { FilePaths = droppedFiles });
            client.Close();
        });

        await flow.RunBridgeAsync(owner, [], new Point(0, 0), null, false, 0, CancellationToken.None);
        await clientTask;

        capturedFileStore.Verify(
            store => store.SetCapturedFiles(
                It.Is<IReadOnlyList<string>>(files => files.SequenceEqual(droppedFiles)),
                TestSid.Value,
                containerSid.Value),
            Times.Once);
    }
}
