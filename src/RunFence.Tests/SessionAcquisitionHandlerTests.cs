using System.Diagnostics;
using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Persistence;
using RunFence.Startup;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class SessionAcquisitionHandlerTests : IDisposable
{
    private readonly Mock<IStartupUI> _ui = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<IIpcClient> _ipcClient = new();
    private readonly Mock<ISingleInstanceService> _singleInstance = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IRunningInstanceSidProvider> _sidProvider = new();

    private readonly string _startKeyPath;
    private readonly string _credentialsPath;

    private static readonly string CurrentSid = SidResolutionHelper.GetCurrentUserSid();
    private static readonly int CurrentSessionId = Process.GetCurrentProcess().SessionId;
    private const string DifferentSid = "S-1-5-21-999999999-888888888-777777777-1001";
    private const int DifferentSessionId = 99;

    public SessionAcquisitionHandlerTests()
    {
        _startKeyPath = Path.Combine(Path.GetTempPath(), $"startkey_{Guid.NewGuid():N}.dat");
        _credentialsPath = Path.Combine(Path.GetTempPath(), $"credentials_{Guid.NewGuid():N}.dat");

        _configPaths.Setup(p => p.RememberPinFilePath).Returns(_startKeyPath);
        _configPaths.Setup(p => p.CredentialsFilePath).Returns(_credentialsPath);

        _singleInstance.Setup(s => s.TryAcquire()).Returns(false);
    }

    public void Dispose()
    {
        if (File.Exists(_startKeyPath))
            File.Delete(_startKeyPath);
        if (File.Exists(_credentialsPath))
            File.Delete(_credentialsPath);
    }

    private SessionAcquisitionHandler BuildHandler() =>
        new(_ui.Object, _configPaths.Object, _ipcClient.Object, _sidProvider.Object);

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyExists_SameSid_IpcSucceeds_ReturnsFalse()
    {
        File.WriteAllBytes(_startKeyPath, []);
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(CurrentSid, CurrentSessionId));
        _ipcClient.Setup(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)))
            .Returns(new IpcResponse { Success = true });

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Once);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void AcquireMutexOrTakeover_UnlockRequested_SameSid_IpcSucceeds_WithoutStartKey_ReturnsFalse()
    {
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(CurrentSid, CurrentSessionId));
        _ipcClient.Setup(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)))
            .Returns(new IpcResponse { Success = true });

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Once);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyExists_SameSid_IpcFails_FallsThroughToTakeover()
    {
        File.WriteAllBytes(_startKeyPath, []);
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(CurrentSid, CurrentSessionId));
        _ipcClient.Setup(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)))
            .Throws(new InvalidOperationException("pipe not available"));
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(false);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Once);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyExists_DifferentSid_FallsThroughToTakeover()
    {
        File.WriteAllBytes(_startKeyPath, []);
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(DifferentSid, CurrentSessionId));
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(false);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Never);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyExists_SameSid_DifferentSession_FallsThroughToTakeover()
    {
        File.WriteAllBytes(_startKeyPath, []);
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(CurrentSid, DifferentSessionId));
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(false);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Never);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyDoesNotExist_FallsThroughToTakeover()
    {
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(false);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _sidProvider.Verify(p => p.GetRunningInstanceInfo(), Times.Once);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Never);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AcquireMutexOrTakeover_StartKeyExists_NoRunningInstance_FallsThroughToTakeover()
    {
        File.WriteAllBytes(_startKeyPath, []);
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns((RunningInstanceInfo?)null);
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(false);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.False(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Never);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void AcquireMutexOrTakeover_MutexAcquired_ReturnsTrueImmediately()
    {
        _singleInstance.Setup(s => s.TryAcquire()).Returns(true);

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.True(result);
        _ipcClient.Verify(c => c.SendMessage(It.IsAny<IpcMessage>()), Times.Never);
        _ui.Verify(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void AcquireMutexOrTakeover_ConfirmTakeover_SendsShutdown_AcquiresOnSecondTry_ReturnsTrue()
    {
        _ui.Setup(u => u.ConfirmTakeover(It.IsAny<bool>(), It.IsAny<bool>())).Returns(true);
        _singleInstance.SetupSequence(s => s.TryAcquire())
            .Returns(false)
            .Returns(true);
        _ipcClient.Setup(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Shutdown)))
            .Returns(new IpcResponse { Success = true });

        var handler = BuildHandler();

        var result = handler.AcquireMutexOrTakeover(_singleInstance.Object, false, _log.Object);

        Assert.True(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Shutdown)), Times.Once);
        _singleInstance.Verify(s => s.TryAcquire(), Times.Exactly(2));
    }

    [Fact]
    public void UnlockExistingInstance_OperationCommand_SendsUnlockOperation()
    {
        _sidProvider.Setup(p => p.GetRunningInstanceInfo()).Returns(new RunningInstanceInfo(CurrentSid, CurrentSessionId));
        _ipcClient.Setup(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.UnlockOperation)))
            .Returns(new IpcResponse { Success = true });

        var handler = BuildHandler();

        var result = handler.UnlockExistingInstance(_log.Object, IpcCommands.UnlockOperation);

        Assert.True(result);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.UnlockOperation)), Times.Once);
        _ipcClient.Verify(c => c.SendMessage(It.Is<IpcMessage>(m => m.Command == IpcCommands.Unlock)), Times.Never);
    }

}
