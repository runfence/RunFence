using System.Diagnostics;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Ipc;
using RunFence.Launcher;
using Xunit;

namespace RunFence.Tests;

public class LauncherAssociationFallbackServiceTests
{
    [Fact]
    public void LaunchFallback_UsesStoredFallbackWithoutRegistryCleanup()
    {
        var lookup = new TrackingFallbackLookup
        {
            StoredFallback = @"""C:\Browser\fallback.exe"" %1"
        };
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreRegistry = new TrackingRestoreRegistry();
        var restoreService = new AssociationFallbackRestoreService(restoreRegistry);
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".pdf", "https://example.com/document");

        Assert.Equal(0, exitCode);
        Assert.NotNull(starter.LastStartInfo);
        Assert.Equal(@"C:\Browser\fallback.exe", starter.LastStartInfo!.FileName);
        Assert.Contains("https://example.com/document", starter.LastStartInfo.Arguments);
        Assert.Empty(notifier.Messages);
        Assert.Equal(0, restoreRegistry.OpenCount);
    }

    [Fact]
    public void CleanupAndLaunchFallback_RestoresFallbackThenLaunches()
    {
        var lookup = new TrackingFallbackLookup();
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreRegistry = new TrackingRestoreRegistry
        {
            StoredFallback = @"""C:\Browser\restored.exe"" %1"
        };
        var restoreService = new AssociationFallbackRestoreService(restoreRegistry);
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.CleanupAndLaunchFallback(".txt", @"C:\docs\file.txt");

        Assert.Equal(0, exitCode);
        Assert.NotNull(starter.LastStartInfo);
        Assert.Equal(@"C:\Browser\restored.exe", starter.LastStartInfo!.FileName);
        Assert.Equal(1, restoreRegistry.OpenCount);
        Assert.Equal(1, restoreRegistry.ReadFallbackCount);
        Assert.Empty(notifier.Messages);
    }

    [Fact]
    public void LaunchFallback_FallsBackToHklmThenLaunches()
    {
        var lookup = new TrackingFallbackLookup
        {
            HklmFallbackByAssociation = { [".url"] = @"""C:\Browser\hklm.exe"" ""%1""" }
        };
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreService = new AssociationFallbackRestoreService(new TrackingRestoreRegistry());
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".url", @"https://example.com/page");

        Assert.Equal(0, exitCode);
        Assert.NotNull(starter.LastStartInfo);
        Assert.Equal(@"C:\Browser\hklm.exe", starter.LastStartInfo!.FileName);
        Assert.Contains("https://example.com/page", starter.LastStartInfo.Arguments);
    }

    [Fact]
    public void LaunchFallback_UnsupportedFallbackShowsErrorAndReturnsFailure()
    {
        var lookup = new TrackingFallbackLookup
        {
            StoredFallback = @"""C:\Browser\fallback.exe"" %2"
        };
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreService = new AssociationFallbackRestoreService(new TrackingRestoreRegistry());
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".pdf", "https://example.com/document");

        Assert.Equal(1, exitCode);
        Assert.Null(starter.LastStartInfo);
        Assert.Contains("unsupported association placeholder '%2'", notifier.Messages[0]);
    }

    [Fact]
    public void LaunchFallback_ProcessStartErrorShowsErrorAndReturnsFailure()
    {
        var lookup = new TrackingFallbackLookup
        {
            StoredFallback = @"""C:\Browser\fallback.exe"" %1"
        };
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreService = new AssociationFallbackRestoreService(new TrackingRestoreRegistry());
        var starter = new TrackingProcessStarter { ThrowOnStart = true };
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".pdf", "https://example.com/document");

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to launch fallback handler", notifier.Messages[0]);
    }

    [Fact]
    public void LaunchFallback_MissingFallbackShowsErrorAndReturnsFailure()
    {
        var lookup = new TrackingFallbackLookup();
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreService = new AssociationFallbackRestoreService(new TrackingRestoreRegistry());
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".missing", null);

        Assert.Equal(1, exitCode);
        Assert.Null(starter.LastStartInfo);
        Assert.Equal("No fallback handler found for '.missing'.", notifier.Messages[0]);
    }

    [Fact]
    public void LaunchFallback_RunFenceProgIdFallbackShowsErrorAndReturnsFailure()
    {
        var lookup = new TrackingFallbackLookup
        {
            StoredFallback = PathConstants.HandlerProgIdPrefix + "pdf"
        };
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var restoreService = new AssociationFallbackRestoreService(new TrackingRestoreRegistry());
        var starter = new TrackingProcessStarter();
        var notifier = new RecordingNotifier();
        var service = new LauncherAssociationFallbackService(
            restoreService,
            resolver,
            starter,
            notifier);

        var exitCode = service.LaunchFallback(".pdf", "https://example.com/document");

        Assert.Equal(1, exitCode);
        Assert.Null(starter.LastStartInfo);
        Assert.Equal("No fallback handler found for '.pdf'.", notifier.Messages[0]);
    }
}

public class LauncherAssociationFallbackCommandResolverTests
{
    [Fact]
    public void ResolveStoredFallbackCommand_ReturnsDirectFallbackWhenNoLookupMatch()
    {
        var lookup = new TrackingFallbackLookup();
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);

        var command = resolver.ResolveStoredFallbackCommand(@"""C:\Direct\fallback.exe"" %1");

        Assert.Equal(@"""C:\Direct\fallback.exe"" %1", command);
    }

    [Fact]
    public void ResolveStoredFallbackCommand_RejectsRunFenceLauncherCommandDirectly()
    {
        var lookup = new TrackingFallbackLookup();
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);

        var command = resolver.ResolveStoredFallbackCommand(@"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --resolve .pdf ""%1""");

        Assert.Null(command);
    }

    [Fact]
    public void ResolveStoredFallbackCommand_RejectsRunFenceProgIdDirectly()
    {
        var lookup = new TrackingFallbackLookup();
        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);

        var command = resolver.ResolveStoredFallbackCommand(PathConstants.HandlerProgIdPrefix + "pdf");

        Assert.Null(command);
    }

    [Fact]
    public void ResolveStoredFallbackCommand_RejectsRunFenceLauncherCommand()
    {
        var lookup = new TrackingFallbackLookup();
        lookup.MergedLookupByProgId[PathConstants.HandlerProgIdPrefix + "pdf"] =
            LauncherFallbackCommandLookupResult.Resolved(@"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --resolve .pdf ""%1""");

        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);

        var command = resolver.ResolveStoredFallbackCommand(PathConstants.HandlerProgIdPrefix + "pdf");

        Assert.Null(command);
    }

    [Fact]
    public void ResolveHklmFallbackCommand_ReturnsLookupValue()
    {
        var lookup = new TrackingFallbackLookup();
        lookup.HklmFallbackByAssociation[".url"] = @"""C:\Browser\hklm.exe"" %1";

        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var command = resolver.ResolveHklmFallbackCommand(".url");

        Assert.Equal(@"""C:\Browser\hklm.exe"" %1", command);
    }

    [Fact]
    public void ResolveHklmFallbackCommand_RejectsLookupRunFenceLauncherCommand()
    {
        var lookup = new TrackingFallbackLookup();
        lookup.HklmFallbackByAssociation[".url"] =
            @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --resolve .url ""%1""";

        var resolver = new LauncherAssociationFallbackCommandResolver(lookup);
        var command = resolver.ResolveHklmFallbackCommand(".url");

        Assert.Null(command);
    }
}

public class AssociationHandlerTests
{
    [Fact]
    public void Handle_SendsHandleAssociationMessageAndReturnsSuccess()
    {
        var sender = new RecordingLauncherIpcCommandSender
        {
            Response = new IpcResponse { Success = true }
        };
        var fallbackService = new TrackingAssociationFallbackService();
        var notifier = new RecordingNotifier();
        var handler = new AssociationHandler(sender, fallbackService, notifier);

        var exitCode = handler.Handle(".pdf", @"C:\docs\sample.pdf");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, sender.SendCallCount);
        Assert.Equal(IpcCommands.HandleAssociation, sender.LastMessage!.Command);
        Assert.Equal(".pdf", sender.LastMessage.Association);
        Assert.Equal(@"C:\docs\sample.pdf", sender.LastMessage.Arguments);
        Assert.Equal(0, fallbackService.CleanupCallCount);
        Assert.Equal(0, fallbackService.LaunchCallCount);
    }

    [Theory]
    [InlineData(IpcErrorCode.AccessDenied, true, false, 5)]
    [InlineData(IpcErrorCode.UnknownAssociation, true, false, 5)]
    [InlineData(IpcErrorCode.AppNotFound, true, false, 5)]
    [InlineData(IpcErrorCode.PathPrefixMismatch, false, true, 6)]
    public void HandleResponse_RoutesToExpectedFallbackServiceMethod(
        IpcErrorCode code,
        bool expectCleanup,
        bool expectLaunch,
        int fallbackExitCode)
    {
        var sender = new RecordingLauncherIpcCommandSender();
        var fallbackService = new TrackingAssociationFallbackService
        {
            CleanupExitCode = 5,
            LaunchExitCode = 6
        };
        var notifier = new RecordingNotifier();
        var handler = new AssociationHandler(sender, fallbackService, notifier);

        var exitCode = handler.HandleResponse(
            ".pdf",
            @"C:\docs\sample.pdf",
            new IpcResponse { Success = false, ErrorCode = code, ErrorMessage = "test error" });

        Assert.Equal(fallbackExitCode, exitCode);
        Assert.Equal(expectCleanup ? 1 : 0, fallbackService.CleanupCallCount);
        Assert.Equal(expectLaunch ? 1 : 0, fallbackService.LaunchCallCount);
    }

    [Fact]
    public void HandleResponse_DefaultFailureShowsErrorAndReturnsFailure()
    {
        var sender = new RecordingLauncherIpcCommandSender();
        var fallbackService = new TrackingAssociationFallbackService();
        var notifier = new RecordingNotifier();
        var handler = new AssociationHandler(sender, fallbackService, notifier);

        var exitCode = handler.HandleResponse(
            ".pdf",
            @"C:\docs\sample.pdf",
            new IpcResponse { Success = false, ErrorCode = (IpcErrorCode)999, ErrorMessage = "unknown code" });

        Assert.Equal(1, exitCode);
        Assert.Equal("unknown code", notifier.Messages[0]);
        Assert.Equal(0, fallbackService.CleanupCallCount);
        Assert.Equal(0, fallbackService.LaunchCallCount);
    }

    [Fact]
    public void HandleResponse_SuccessWithWarning_ShowsWarningAndReturnsSuccess()
    {
        var sender = new RecordingLauncherIpcCommandSender();
        var fallbackService = new TrackingAssociationFallbackService();
        var notifier = new RecordingNotifier();
        var handler = new AssociationHandler(sender, fallbackService, notifier);

        var exitCode = handler.HandleResponse(
            ".pdf",
            @"C:\docs\sample.pdf",
            new IpcResponse { Success = true, WarningMessage = "started with maintenance warnings" });

        Assert.Equal(0, exitCode);
        Assert.Equal(["started with maintenance warnings"], notifier.WarningMessages);
        Assert.Empty(notifier.Messages);
        Assert.Equal(0, fallbackService.CleanupCallCount);
        Assert.Equal(0, fallbackService.LaunchCallCount);
    }
}

public class LauncherOpenFolderHandlerTests
{
    [Fact]
    public void Handle_UsesIpcPath_WhenNotAdmin()
    {
        var sender = new RecordingLauncherIpcCommandSender
        {
            Response = new IpcResponse { Success = true }
        };
        var starter = new TrackingProcessStarter();
        var handler = new TestOpenFolderHandler(sender, starter) { CurrentProcessAdmin = false, OwnExplorerRunning = false };

        var exitCode = handler.Handle(@"C:\tmp\sample");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, sender.SendCallCount);
        Assert.Equal(IpcCommands.OpenFolder, sender.LastMessage!.Command);
        Assert.Equal(@"C:\tmp\sample", sender.LastMessage.Arguments);
        Assert.Null(starter.LastStartInfo);
    }

    [Fact]
    public void Handle_UsesDirectExplorer_WhenAdmin()
    {
        var sender = new RecordingLauncherIpcCommandSender();
        var starter = new TrackingProcessStarter();
        var handler = new TestOpenFolderHandler(sender, starter) { CurrentProcessAdmin = true };

        var exitCode = handler.Handle(@"C:\tmp\sample");

        Assert.Equal(0, exitCode);
        Assert.Equal(@"C:\tmp\sample", starter.LastStartInfo!.FileName);
        Assert.Null(sender.LastMessage);
        Assert.Equal(1, starter.StartCallCount);
    }

    [Fact]
    public void IsOwnExplorerRunning_IgnoresExplorerFromOtherSession()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var ownSid = identity.User ?? throw new InvalidOperationException("Current SID unavailable.");
        var handler = new SessionAwareTestOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            7,
            [new SessionAwareTestOpenFolderHandler.ExplorerOwnerInfo(101u, 8, ownSid)]);

        var result = handler.CheckIsOwnExplorerRunning();

        Assert.False(result);
    }

    [Fact]
    public void IsOwnExplorerRunning_ReturnsTrueForOwnExplorerInCurrentSession()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var ownSid = identity.User ?? throw new InvalidOperationException("Current SID unavailable.");
        var handler = new SessionAwareTestOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            7,
            [
                new SessionAwareTestOpenFolderHandler.ExplorerOwnerInfo(101u, 8, ownSid),
                new SessionAwareTestOpenFolderHandler.ExplorerOwnerInfo(102u, 7, ownSid)
            ]);

        var result = handler.CheckIsOwnExplorerRunning();

        Assert.True(result);
    }
}

public class TrackingAssociationFallbackService : ILauncherAssociationFallbackService
{
    public int CleanupCallCount { get; private set; }
    public int LaunchCallCount { get; private set; }
    public int CleanupExitCode { get; init; } = 1;
    public int LaunchExitCode { get; init; } = 1;

    public int LaunchFallback(string association, string? rawArguments)
    {
        LaunchCallCount++;
        return LaunchExitCode;
    }

    public int CleanupAndLaunchFallback(string association, string? rawArguments)
    {
        CleanupCallCount++;
        return CleanupExitCode;
    }
}

public class TrackingProcessStarter : ILauncherProcessStarter
{
    public ProcessStartInfo? LastStartInfo { get; private set; }
    public int StartCallCount { get; private set; }
    public bool ThrowOnStart { get; init; }

    public void Start(ProcessStartInfo startInfo)
    {
        StartCallCount++;
        LastStartInfo = startInfo;
        if (ThrowOnStart)
            throw new InvalidOperationException("launcher test failure");
    }
}

public class TrackingRestoreRegistry : IAssociationFallbackRegistry
{
    public int OpenCount { get; private set; }
    public int ReadFallbackCount { get; private set; }
    public int WriteDefaultCommandCount { get; private set; }
    public int DeleteFallbackValueCount { get; private set; }
    public int DeleteExtensionCommandSubkeyCount { get; private set; }
    public int NotifyShellChangedCount { get; private set; }
    public string? StoredFallback { get; set; }

    public IOwnedAssociationRegistryRoot? OpenUserClassesRoot(string? targetSid = null)
    {
        OpenCount++;
        return new DummyOwnedRoot();
    }

    public string? ReadFallbackCommand(IOwnedAssociationRegistryRoot root, string association) { ReadFallbackCount++; return StoredFallback; }

    public void WriteDefaultCommand(IOwnedAssociationRegistryRoot root, string association, string fallbackValue)
        => WriteDefaultCommandCount++;

    public void DeleteFallbackValue(IOwnedAssociationRegistryRoot root, string association)
        => DeleteFallbackValueCount++;

    public void DeleteExtensionCommandSubkeys(IOwnedAssociationRegistryRoot root, string association)
        => DeleteExtensionCommandSubkeyCount++;

    public void NotifyShellChanged()
        => NotifyShellChangedCount++;

    private sealed class DummyOwnedRoot : IOwnedAssociationRegistryRoot
    {
        public void Dispose()
        {
        }
    }
}

public class TrackingFallbackLookup : ILauncherAssociationFallbackLookup
{
    public string? StoredFallback { get; set; }
    public Dictionary<string, LauncherFallbackCommandLookupResult> MergedLookupByProgId { get; } = [];
    public Dictionary<string, string> HklmFallbackByAssociation { get; } = [];

    public string? ReadFallbackValue(string association) => StoredFallback;

    public LauncherFallbackCommandLookupResult ResolveMergedProgIdCommand(string progId)
        => MergedLookupByProgId.TryGetValue(progId, out var result) ? result : LauncherFallbackCommandLookupResult.NotFound();

    public string? ResolveHklmAssociationCommand(string association)
        => HklmFallbackByAssociation.TryGetValue(association, out var result) ? result : null;
}

public class RecordingLauncherIpcCommandSender : ILauncherIpcCommandSender
{
    public IpcResponse? Response { get; set; }
    public int SendCallCount { get; private set; }
    public IpcMessage? LastMessage { get; private set; }

    public IpcResponse? SendWithAutoStart(IpcMessage message)
    {
        SendCallCount++;
        LastMessage = message;
        return Response;
    }
}

public sealed class RecordingNotifier : ILauncherUserNotifier
{
    public List<string> Messages { get; } = [];
    public List<string> WarningMessages { get; } = [];

    public void ShowError(string message) => Messages.Add(message);
    public void ShowWarning(string message) => WarningMessages.Add(message);
}

public sealed class TestOpenFolderHandler : OpenFolderHandler
{
    private readonly ILauncherProcessStarter _starter;

    public TestOpenFolderHandler(ILauncherIpcCommandSender sender, ILauncherProcessStarter starter)
        : base(sender, starter)
    {
        _starter = starter;
    }

    public bool CurrentProcessAdmin { get; set; }
    public bool OwnExplorerRunning { get; set; }

    protected override bool IsCurrentProcessAdmin() => CurrentProcessAdmin;
    protected override bool IsOwnExplorerRunning() => OwnExplorerRunning;

    protected override void UnregisterOwnHandler()
    {
    }

    protected override void LaunchExplorerDirect(string folderPath)
    {
        _starter.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            Arguments = string.Empty,
            UseShellExecute = false
        });
    }
}

public sealed class SessionAwareTestOpenFolderHandler : OpenFolderHandler
{
    private readonly int _currentSessionId;
    private readonly IReadOnlyList<ExplorerOwnerInfo> _explorers;

    public SessionAwareTestOpenFolderHandler(
        ILauncherIpcCommandSender sender,
        ILauncherProcessStarter starter,
        int currentSessionId,
        IReadOnlyList<ExplorerOwnerInfo> explorers)
        : base(sender, starter)
    {
        _currentSessionId = currentSessionId;
        _explorers = explorers;
    }

    public bool CheckIsOwnExplorerRunning() => IsOwnExplorerRunning();

    protected override int GetCurrentSessionId() => _currentSessionId;

    protected override IEnumerable<ExplorerProcessInfo> GetExplorerProcesses()
        => _explorers.Select(x => new ExplorerProcessInfo(x.ProcessId, x.SessionId));

    protected override SecurityIdentifier? TryGetProcessOwnerSid(uint processId)
        => _explorers.FirstOrDefault(x => x.ProcessId == processId).OwnerSid;

    protected override void UnregisterOwnHandler()
    {
    }

    public readonly record struct ExplorerOwnerInfo(uint ProcessId, int SessionId, SecurityIdentifier OwnerSid);
}
