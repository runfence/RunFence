using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for the IpcOpenFolderHandler logic.
/// Uses mocked IAppStateProvider, IAppLockControl, IUiThreadInvoker, IDirectoryValidator.
/// </summary>
/// <remarks>
/// Authorization bypass is by design: the OpenFolder IPC command carries no per-caller authorization
/// check because the verb handler is registered in the interactive user's own HKU registry hive —
/// only processes running under that account can trigger it. Opening a folder in Explorer grants no
/// elevated access; path safety is enforced by IDirectoryValidator (including TOCTOU protection).
/// See IpcOpenFolderHandler for the in-code comment.
/// </remarks>
public class OpenFolderHandlerTests
{
    private const string CallerSid = "S-1-5-21-100-200-300-1001";
    private const string CallerIdentity = @"DOMAIN\TestUser";
    private const string TestPath = @"C:\Users\TestUser\Downloads";
    private const string CanonicalPath = @"C:\Users\TestUser\Downloads";

    private readonly Mock<IAppStateProvider> _appState;
    private readonly Mock<IAppLockControl> _appLock;
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker;
    private readonly Mock<IDirectoryValidator> _validator;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IShellFolderOpener> _shellFolderOpener;
    private readonly IpcOpenFolderHandler _handler = null!;

    public OpenFolderHandlerTests()
    {
        _appState = new Mock<IAppStateProvider>();
        _appLock = new Mock<IAppLockControl>();
        _uiThreadInvoker = new Mock<IUiThreadInvoker>();
        _validator = new Mock<IDirectoryValidator>();
        _log = new Mock<ILoggingService>();
        _shellFolderOpener = new Mock<IShellFolderOpener>();

        // Default: application is running normally
        _appState.Setup(a => a.IsShuttingDown).Returns(false);
        _appLock.Setup(a => a.IsUnlockPolling).Returns(false);

        _uiThreadInvoker.Setup(a => a.Invoke(It.IsAny<Action>()))
            .Callback<Action>(a => a());

        // Default: validator returns valid handle for the test path
        var validHandle = new DirectoryValidationHandle(null) { IsValid = true, CanonicalPath = CanonicalPath };
        _validator.Setup(v => v.ValidateAndHold(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(validHandle);

        // Default: shell opener succeeds
        string? noError = null;
        _shellFolderOpener.Setup(s => s.TryOpen(It.IsAny<string>(), out noError)).Returns(true);

        _handler = CreateHandler();
    }

    private IpcOpenFolderHandler CreateHandler(bool noValidator = false)
        => new(_appLock.Object, new IpcUiInvoker(_uiThreadInvoker.Object, _appState.Object),
            noValidator ? null : _validator.Object, _log.Object, _shellFolderOpener.Object);

    private static IpcMessage OpenFolderMessage(string? path)
        => new() { Command = IpcCommands.OpenFolder, Arguments = path };

    [Fact]
    public void HandleOpenFolder_RejectsEmptyPath()
    {
        var response = _handler.HandleOpenFolder(OpenFolderMessage(null), CallerIdentity, CallerSid);

        Assert.False(response.Success);
        Assert.Contains("required", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleOpenFolder_RejectsNullCallerSid()
    {
        var response = _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, null);

        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public void HandleOpenFolder_RejectsWhenShuttingDown()
    {
        _appState.Setup(a => a.IsShuttingDown).Returns(true);

        var response = _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        Assert.False(response.Success);
        Assert.Contains("shutting down", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleOpenFolder_RejectsWhenUnlockPolling()
    {
        _appLock.Setup(a => a.IsUnlockPolling).Returns(true);

        var response = _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        Assert.False(response.Success);
        Assert.Equal("Busy.", response.ErrorMessage);
    }

    [Fact]
    public void HandleOpenFolder_RejectsWhenDirectoryValidatorUnavailable()
    {
        var handler = CreateHandler(noValidator: true);

        var response = handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        Assert.False(response.Success);
        Assert.Contains("not available", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleOpenFolder_RejectsInvalidDirectory()
    {
        var invalidHandle = new DirectoryValidationHandle(null)
        {
            IsValid = false,
            Error = "Path is not a directory."
        };
        _validator.Setup(v => v.ValidateAndHold(TestPath, CallerSid)).Returns(invalidHandle);

        var response = _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        Assert.False(response.Success);
        Assert.Equal("Path is not a directory.", response.ErrorMessage);
    }

    [Fact]
    public void HandleOpenFolder_CallsValidatorWithCorrectArguments()
    {
        // Validator returns invalid to short-circuit the shell open call
        var invalidHandle = new DirectoryValidationHandle(null) { IsValid = false, Error = "test" };
        _validator.Setup(v => v.ValidateAndHold(TestPath, CallerSid)).Returns(invalidHandle);

        _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        _validator.Verify(v => v.ValidateAndHold(TestPath, CallerSid), Times.Once);
    }

    [Fact]
    public void HandleOpenFolder_ValidPathAndCaller_InvokesOnUiThreadAndReturnsSuccess()
    {
        var response = _handler.HandleOpenFolder(OpenFolderMessage(TestPath), CallerIdentity, CallerSid);

        _validator.Verify(v => v.ValidateAndHold(TestPath, CallerSid), Times.Once);
        _uiThreadInvoker.Verify(a => a.Invoke(It.IsAny<Action>()), Times.AtLeastOnce);
        _shellFolderOpener.Verify(s => s.TryOpen(CanonicalPath, out It.Ref<string?>.IsAny), Times.Once);
        Assert.True(response.Success);
    }
}