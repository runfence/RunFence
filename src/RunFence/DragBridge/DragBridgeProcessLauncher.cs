using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

/// <summary>
/// Manages process launch, pipe server creation, and active operation lifecycle
/// for the DragBridge cross-user bridge.
/// </summary>
public class DragBridgeProcessLauncher(
    IDragBridgeLauncher launcher,
    ILoggingService log,
    IUiThreadInvoker uiThreadInvoker,
    string dragBridgeExePath = "")
    : IDragBridgeProcessLauncher
{
    private readonly string _dragBridgeExePath = dragBridgeExePath.Length > 0
        ? dragBridgeExePath
        : Path.Combine(AppContext.BaseDirectory, Constants.DragBridgeExeName);

    private string? _currentSid;
    private string? _interactiveSid;
    private CredentialStore? _credentialStore;

    // Active operation tracking for concurrent instance guard
    private volatile ProcessInfo? _activeProcess;
    private CancellationTokenSource? _activeCts;

    public void SetData(SessionContext session)
    {
        _credentialStore = session.CredentialStore;
        _currentSid = SidResolutionHelper.GetCurrentUserSid();
        _interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
    }

    public CancellationTokenSource BeginOperation()
    {
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _activeCts, cts);
        old?.Cancel();
        old?.Dispose();
        return cts;
    }

    public void KillActiveOperation()
    {
        var cts = Interlocked.Exchange(ref _activeCts, null);
        cts?.Cancel();
        cts?.Dispose();
        var process = Interlocked.Exchange(ref _activeProcess, null);
        KillProcess(process);
        process?.Dispose();
    }

    public NamedPipeServerStream CreatePipeServer(string pipeName, SecurityIdentifier targetUserSid)
    {
        var pipeSecurity = new PipeSecurity();
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(targetUserSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            256 * 1024, 256 * 1024, pipeSecurity);
    }

    public ProcessInfo? LaunchForSid(WindowOwnerInfo ownerInfo, IReadOnlyList<string> args,
        INotificationService notifications)
    {
        var sid = ownerInfo.Sid;
        ProcessInfo? process = null;

        if (_currentSid != null && string.Equals(sid.Value, _currentSid, StringComparison.OrdinalIgnoreCase))
        {
            var privilegeLevel = ownerInfo.IntegrityLevel switch
            {
                <= NativeTokenHelper.MandatoryLevelLow => PrivilegeLevel.LowIntegrity,
                <= NativeTokenHelper.MandatoryLevelMedium => PrivilegeLevel.Basic,
                _ => PrivilegeLevel.HighestAllowed,
            };
            process = launcher.LaunchDirect(_dragBridgeExePath, args, privilegeLevel);
            if (process == null)
                return null;
        }
        else if (_interactiveSid != null && string.Equals(sid.Value, _interactiveSid, StringComparison.OrdinalIgnoreCase))
        {
            // When target window is elevated (High IL or above), pass HighestAllowed so the bridge
            // process is not restricted below the target window's integrity level.
            var elevated = ownerInfo.IntegrityLevel > NativeTokenHelper.MandatoryLevelMedium;
            process = launcher.LaunchDeElevated(_dragBridgeExePath, args, elevated ? PrivilegeLevel.HighestAllowed : (PrivilegeLevel?)null);
            if (process == null)
                return null;
        }
        else if (_credentialStore != null && _credentialStore.Credentials.Any(c =>
                     string.Equals(c.Sid, sid.Value, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                process = launcher.LaunchManaged(_dragBridgeExePath, sid.Value, args);
            }
            catch (Exception ex)
            {
                log.Error("DragBridgeService: managed launch failed", ex);
                uiThreadInvoker.Invoke(() => notifications.ShowError("Drag Bridge", "Failed to launch drag bridge process."));
                return null;
            }
        }
        else
        {
            uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
                "No credentials for this account — cannot launch drag bridge."));
            return null;
        }

        return process;
    }

    /// <summary>
    /// Sends a ready signal on the pipe. The child waits for this signal before
    /// creating its window, ensuring foreground rights are active via AttachThreadInput.
    /// </summary>
    public void SignalReady(NamedPipeServerStream pipe)
    {
        pipe.WriteByte(0x01);
        pipe.Flush();
    }

    public bool VerifyClientProcess(NamedPipeServerStream pipe, ProcessInfo? expectedProcess)
    {
        try
        {
            ProcessNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientPid);

            if (expectedProcess != null)
                return clientPid == (uint)expectedProcess.Id;

            // For de-elevated/managed launches (no process handle), verify exe path instead.
            // A non-admin user cannot replace the binary at the install directory.
            using var handle = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, clientPid);
            if (handle.IsInvalid)
                return false;

            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!ProcessNative.QueryFullProcessImageName(handle, 0, sb, ref size))
                return false;

            return string.Equals(sb.ToString(), _dragBridgeExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void SetActiveProcess(ProcessInfo? process) => _activeProcess = process;

    public void KillProcess(ProcessInfo? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
        }
    }
}