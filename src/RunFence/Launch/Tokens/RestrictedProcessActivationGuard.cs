using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class RestrictedProcessActivationGuard(IRestrictedProcessControl processControl)
{
    public void ThrowIfAssignmentFailed(
        ref ProcessLaunchNative.PROCESS_INFORMATION processInfo,
        JobAssignmentResult assignment,
        string sid,
        bool isLow)
    {
        if (assignment.Succeeded)
            return;

        TerminateAndClose(ref processInfo);
        throw RestrictedJobAssignmentFailed(sid, isLow, assignment.FailureReason);
    }

    public void ResumeOrTerminate(
        ref ProcessLaunchNative.PROCESS_INFORMATION processInfo,
        string sid,
        bool isLow,
        string role)
    {
        if (processInfo.hThread == IntPtr.Zero)
        {
            TerminateAndClose(ref processInfo);
            throw RestrictedJobAssignmentFailed(sid, isLow, $"Cannot resume {role}: missing suspended thread handle.");
        }

        if (!processControl.ResumeThread(processInfo.hThread, out var error))
        {
            TerminateAndClose(ref processInfo);
            throw RestrictedJobAssignmentFailed(sid, isLow,
                $"Cannot resume {role}: ResumeThread failed with Win32 error {error}.");
        }
    }

    public void TerminateAndClose(ref ProcessLaunchNative.PROCESS_INFORMATION processInfo)
    {
        if (processInfo.hProcess != IntPtr.Zero)
            processControl.TerminateProcess(processInfo.hProcess);
        CloseHandles(ref processInfo);
    }

    public void CloseHandles(ref ProcessLaunchNative.PROCESS_INFORMATION processInfo)
    {
        CloseThreadHandle(ref processInfo);

        if (processInfo.hProcess != IntPtr.Zero)
        {
            processControl.CloseHandle(processInfo.hProcess);
            processInfo.hProcess = IntPtr.Zero;
        }
    }

    public void CloseThreadHandle(ref ProcessLaunchNative.PROCESS_INFORMATION processInfo)
    {
        if (processInfo.hThread == IntPtr.Zero)
            return;

        processControl.CloseHandle(processInfo.hThread);
        processInfo.hThread = IntPtr.Zero;
    }

    public static LaunchFailedException RestrictedJobAssignmentFailed(string sid, bool isLow, string? reason) =>
        new($"Restricted launch failed closed for {sid} (isLow={isLow}): {reason ?? "restricted job assignment failed"}");
}
