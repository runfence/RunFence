using System.Diagnostics;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class SkipWhenJobAssignmentUnavailableFactAttribute : FactAttribute
{
    public SkipWhenJobAssignmentUnavailableFactAttribute()
    {
        Skip = GetSkipReason();
    }

    private static string? GetSkipReason()
    {
        var native = new WindowsJobObjectNative();
        using var current = Process.GetCurrentProcess();
        if (native.IsProcessInJob(current.Handle, IntPtr.Zero, out var currentInJob) && currentInJob)
            return "Current test process is already assigned to a job on this host.";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = @"-NoProfile -Command ""[Console]::OpenStandardInput().ReadByte() | Out-Null""",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? child = null;
        StreamWriter? childInput = null;
        var jobHandle = IntPtr.Zero;
        try
        {
            jobHandle = native.CreateJobObject($@"RunFence_JobObjectNative_Probe_{Guid.NewGuid():N}");
            child = Process.Start(startInfo);
            if (child == null)
                return "PowerShell child process could not be started for job assignment probe.";

            childInput = child.StandardInput;
            if (native.IsProcessInJob(child.Handle, IntPtr.Zero, out var childAlreadyInJob) && childAlreadyInJob)
                return "Child PowerShell process is already assigned to a job on this host.";

            if (native.AssignProcessToJobObject(jobHandle, child.Handle))
                return null;

            return native.GetLastWin32Error() == 5
                ? "AssignProcessToJobObject returned access denied on this host."
                : null;
        }
        finally
        {
            try
            {
                childInput?.Dispose();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            if (child != null)
            {
                try
                {
                    if (!child.HasExited)
                        child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }

                child.Dispose();
            }

            if (jobHandle != IntPtr.Zero)
                native.CloseHandle(jobHandle);
        }
    }
}
