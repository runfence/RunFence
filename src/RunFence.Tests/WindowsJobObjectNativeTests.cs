using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsJobObjectNativeTests
{
    [Fact]
    public void CreateOpenDuplicateCompare_CreatesNamedJobAndDuplicateHandleReferencesSameObject()
    {
        var native = new WindowsJobObjectNative();
        var jobName = $"RunFence_JobObjectNative_{Guid.NewGuid():N}";

        IntPtr createdJobHandle = IntPtr.Zero;
        IntPtr openedJobHandle = IntPtr.Zero;
        IntPtr duplicatedHandle = IntPtr.Zero;

        try
        {
            createdJobHandle = native.CreateJobObject(jobName);
            Assert.NotEqual(IntPtr.Zero, createdJobHandle);

            openedJobHandle = native.OpenJobObject(ProcessJobManager.JobObjectReconnectAccess, false, jobName);
            Assert.NotEqual(IntPtr.Zero, openedJobHandle);
            Assert.True(native.CompareObjectHandles(createdJobHandle, openedJobHandle));

            Assert.True(
                native.DuplicateHandleToProcess(
                    ProcessNative.GetCurrentProcess(),
                    createdJobHandle,
                    ProcessNative.GetCurrentProcess(),
                    ProcessJobManager.JobObjectReconnectAccess,
                    out duplicatedHandle));
            Assert.True(native.CompareObjectHandles(createdJobHandle, duplicatedHandle));
        }
        finally
        {
            CloseHandle(native, duplicatedHandle);
            CloseHandle(native, openedJobHandle);
            CloseHandle(native, createdJobHandle);
        }
    }

    [Fact]
    public void SetInformation_QueryInformation_RoundTripsUiRestrictionsFlag()
    {
        const int JobObjectBasicUIRestrictions = 4;
        const uint expectedUiRestrictions = 0x20u;

        var native = new WindowsJobObjectNative();
        IntPtr jobHandle = IntPtr.Zero;
        IntPtr setBuffer = IntPtr.Zero;
        IntPtr queryBuffer = IntPtr.Zero;

        try
        {
            jobHandle = native.CreateJobObject($@"RunFence_JobObjectNative_Ui_{Guid.NewGuid():N}");
            Assert.NotEqual(IntPtr.Zero, jobHandle);

            setBuffer = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(setBuffer, unchecked((int)expectedUiRestrictions));
            Assert.True(native.SetInformationJobObject(jobHandle, JobObjectBasicUIRestrictions, setBuffer, 4));

            queryBuffer = Marshal.AllocHGlobal(4);
            Assert.True(native.QueryInformationJobObject(jobHandle, JobObjectBasicUIRestrictions, queryBuffer, 4, out var returnLength));
            Assert.Equal(4u, returnLength);
            Assert.Equal(expectedUiRestrictions, unchecked((uint)Marshal.ReadInt32(queryBuffer)));
        }
        finally
        {
            if (queryBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(queryBuffer);
            if (setBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(setBuffer);
            CloseHandle(native, jobHandle);
        }
    }

    [Fact]
    public void GetSecuritySnapshot_ReturnsCurrentUserOwnerAndAllowAce()
    {
        var native = new WindowsJobObjectNative();
        var api = new WindowsJobObjectApi(native);
        IntPtr jobHandle = IntPtr.Zero;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User ?? throw new InvalidOperationException("Current SID unavailable.");

            var sddl = $"O:{userSid.Value}D:P(A;;GA;;;{userSid.Value})";
            var jobName = $@"RunFence_JobObjectNative_Security_{Guid.NewGuid():N}";
            jobHandle = native.CreateJobObjectWithSecurityDescriptor(jobName, sddl);
            Assert.NotEqual(IntPtr.Zero, jobHandle);

            var snapshot = api.GetSecuritySnapshot(jobHandle);
            Assert.NotNull(snapshot);
            Assert.Equal(userSid, snapshot!.Owner);
            Assert.Contains(snapshot.AccessEntries, entry => entry.Identity.Value == userSid.Value && entry.IsAllow);
        }
        finally
        {
            CloseHandle(native, jobHandle);
        }
    }

    [SkipWhenJobAssignmentUnavailableFact]
    public void AssignProcessToJobObject_AssignsResponsivePowerShellProcess()
    {
        var native = new WindowsJobObjectNative();
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
        IntPtr jobHandle = IntPtr.Zero;
        try
        {
            var jobName = $@"RunFence_JobObjectNative_Assign_{Guid.NewGuid():N}";
            jobHandle = native.CreateJobObject(jobName);
            Assert.NotEqual(IntPtr.Zero, jobHandle);

            child = Process.Start(startInfo);
            Assert.NotNull(child);
            childInput = child.StandardInput;

            if (native.IsProcessInJob(child.Handle, IntPtr.Zero, out var alreadyInJob) && alreadyInJob)
            {
                throw new InvalidOperationException("Job assignment probe did not detect that the child process is already assigned to a job.");
            }

            var assigned = native.AssignProcessToJobObject(jobHandle, child.Handle);
            Assert.True(assigned);

            Assert.True(native.IsProcessInJob(child.Handle, jobHandle, out var isInAssignedJob));
            Assert.True(isInAssignedJob);
        }
        finally
        {
            if (childInput != null)
            {
                try
                {
                    childInput.Dispose();
                }
                catch (System.IO.IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (child != null)
            {
                if (!child.HasExited)
                {
                    try
                    {
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
                    if (!child.WaitForExit(5000) && !child.HasExited)
                    {
                        try
                        {
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
                        child.WaitForExit(1000);
                    }
                }

                child.Dispose();
            }

            CloseHandle(native, jobHandle);
        }
    }

    private static void CloseHandle(WindowsJobObjectNative native, IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            native.CloseHandle(handle);
    }
}
