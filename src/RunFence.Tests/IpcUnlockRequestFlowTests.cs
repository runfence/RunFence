using Moq;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Infrastructure;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

public class IpcUnlockRequestFlowTests
{
    [Fact]
    public void HandleUnlockAppRequest_NonAdmin_ReturnsAccessDenied()
    {
        var flow = CreateFlow();

        var response = flow.HandleUnlockApp(
            new IpcOperationRequest(@"DOMAIN\User", null, false),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("Access denied. Admin required.", response.ErrorMessage);
    }

    [Fact]
    public void HandleUnlockAppRequest_CurrentAdmin_UsesDirectUnlockTask()
    {
        var elevatedUnlock = new Mock<IElevatedUnlockRequestHandler>();
        elevatedUnlock.Setup(x => x.HandleElevatedUnlockRequestAsync()).ReturnsAsync(true);
        var flow = CreateFlow(new ImmediateIpcUiInvoker(), elevatedUnlock: elevatedUnlock.Object);

        var response = flow.HandleUnlockApp(
            new IpcOperationRequest(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true),
            CancellationToken.None);

        Assert.True(response.Success);
        elevatedUnlock.Verify(x => x.HandleElevatedUnlockRequestAsync(), Times.Once);
    }

    [Fact]
    public void HandleUnlockOperationAsync_DifferentAdmin_UsesRequestOperationUnlock()
    {
        var operationUnlock = new Mock<IOperationUnlockRequestHandler>();
        var flow = CreateFlow(new ImmediateIpcUiInvoker(), operationUnlock: operationUnlock.Object);

        var response = flow.HandleUnlockOperation(
            new IpcOperationRequest(@"DOMAIN\Admin", null, true),
            CancellationToken.None);

        Assert.True(response.Success);
        operationUnlock.Verify(x => x.RequestOperationUnlock(), Times.Once);
        operationUnlock.Verify(x => x.HandleOperationUnlockRequestAsync(), Times.Never);
    }

    [Fact]
    public void HandleUnlockOperationAsync_CurrentAdmin_WhenUnlockTaskReturnsFalse_PreservesMessage()
    {
        var operationUnlock = new Mock<IOperationUnlockRequestHandler>();
        operationUnlock.Setup(x => x.HandleOperationUnlockRequestAsync()).ReturnsAsync(false);
        var flow = CreateFlow(new ImmediateIpcUiInvoker(), operationUnlock: operationUnlock.Object);

        var response = flow.HandleUnlockOperation(
            new IpcOperationRequest(@"DOMAIN\Admin", SidResolutionHelper.GetCurrentUserSid(), true),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("No pending operation unlock.", response.ErrorMessage);
    }

    private static IpcUnlockRequestFlow CreateFlow(
        IIpcUiInvoker? ipcUiInvoker = null,
        IElevatedUnlockRequestHandler? elevatedUnlock = null,
        IOperationUnlockRequestHandler? operationUnlock = null,
        IShowWindowRequestHandler? showWindow = null)
    {
        return new IpcUnlockRequestFlow(
            ipcUiInvoker ?? Mock.Of<IIpcUiInvoker>(),
            elevatedUnlock ?? Mock.Of<IElevatedUnlockRequestHandler>(),
            operationUnlock ?? Mock.Of<IOperationUnlockRequestHandler>(),
            showWindow ?? Mock.Of<IShowWindowRequestHandler>(),
            Mock.Of<ILoggingService>());
    }

    private sealed class ImmediateIpcUiInvoker : IIpcUiInvoker
    {
        public bool TryInvoke(Action action, out IpcResponse? shuttingDownResponse)
        {
            shuttingDownResponse = null;
            action();
            return true;
        }

        public bool TryBeginInvoke(Action action)
        {
            action();
            return true;
        }

        public bool IsShuttingDown(out IpcResponse? response)
        {
            response = null;
            return false;
        }
    }
}
