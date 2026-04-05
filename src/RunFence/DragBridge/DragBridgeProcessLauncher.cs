using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Manages process launch, pipe server creation, and active operation lifecycle
/// for the DragBridge cross-user bridge.
/// </summary>
public class DragBridgeProcessLauncher
{
    private readonly IDragBridgeLauncher _launcher;
    private readonly ILoggingService _log;
    private readonly string _dragBridgeExePath;
    private readonly IUiThreadInvoker _uiThreadInvoker;

    private string? _currentSid;
    private string? _interactiveSid;
    private CredentialStore? _credentialStore;

    // Active operation tracking for concurrent instance guard
    private volatile Process? _activeProcess;
    private CancellationTokenSource? _activeCts;

    public DragBridgeProcessLauncher(
        IDragBridgeLauncher launcher,
        ILoggingService log,
        IUiThreadInvoker uiThreadInvoker,
        string dragBridgeExePath = "")
    {
        _launcher = launcher;
        _log = log;
        _uiThreadInvoker = uiThreadInvoker;
        _dragBridgeExePath = dragBridgeExePath.Length > 0
            ? dragBridgeExePath
            : Path.Combine(AppContext.BaseDirectory, Constants.DragBridgeExeName);
    }

    public void SetData(SessionContext session)
    {
        _credentialStore = session.CredentialStore;
        _currentSid = SidResolutionHelper.GetCurrentUserSid();
        _interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid()?.Value;
    }

    public CancellationTokenSource BeginOperation()
    {
        var cts = new CancellationTokenSource();
        _activeCts = cts;
        return cts;
    }

    public void KillActiveOperation()
    {
        var cts = Interlocked.Exchange(ref _activeCts, null);
        cts?.Cancel();
        cts?.Dispose();
        KillProcess(_activeProcess);
        _activeProcess?.Dispose();
        _activeProcess = null;
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

    public Process? LaunchForSid(WindowOwnerInfo ownerInfo, IReadOnlyList<string> args,
        INotificationService notifications)
    {
        var sid = ownerInfo.Sid;
        Process? process = null;

        if (_currentSid != null && string.Equals(sid.Value, _currentSid, StringComparison.OrdinalIgnoreCase))
        {
            var useSplitToken = ownerInfo.IntegrityLevel <= NativeTokenHelper.MandatoryLevelMedium;
            var useLowIntegrity = ownerInfo.IntegrityLevel <= NativeTokenHelper.MandatoryLevelLow;
            process = _launcher.LaunchDirect(_dragBridgeExePath, args, useSplitToken, useLowIntegrity);
        }
        else if (_interactiveSid != null && string.Equals(sid.Value, _interactiveSid, StringComparison.OrdinalIgnoreCase))
        {
            // Skip split-token restriction when the target window is elevated (High IL or above),
            // otherwise the restricted bridge process cannot drag to the unrestricted target window.
            var skipSplitToken = ownerInfo.IntegrityLevel > NativeTokenHelper.MandatoryLevelMedium;
            var pid = _launcher.LaunchDeElevated(_dragBridgeExePath, args, skipSplitToken);
            if (pid == 0)
                return null;
            try
            {
                process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return null;
            } // process already terminated; pipe timeout handles detection
        }
        else if (_credentialStore != null && _credentialStore.Credentials.Any(c =>
                     string.Equals(c.Sid, sid.Value, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pid = _launcher.LaunchManaged(_dragBridgeExePath, sid.Value, args);
                if (pid > 0)
                {
                    try
                    {
                        process = Process.GetProcessById(pid);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("DragBridgeService: managed launch failed", ex);
                _uiThreadInvoker.Invoke(() => notifications.ShowError("Drag Bridge", "Failed to launch drag bridge process."));
                return null;
            }
        }
        else
        {
            _uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
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

    public bool VerifyClientProcess(NamedPipeServerStream pipe, Process? expectedProcess)
    {
        try
        {
            NativeInterop.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientPid);

            if (expectedProcess != null)
                return clientPid == (uint)expectedProcess.Id;

            // For de-elevated/managed launches (no process handle), verify exe path instead.
            // A non-admin user cannot replace the binary at the install directory.
            using var handle = NativeInterop.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, clientPid);
            if (handle.IsInvalid)
                return false;

            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!NativeInterop.QueryFullProcessImageName(handle, 0, sb, ref size))
                return false;

            return string.Equals(sb.ToString(), _dragBridgeExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void SetActiveProcess(Process? process) => _activeProcess = process;

    public void KillProcess(Process? process)
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