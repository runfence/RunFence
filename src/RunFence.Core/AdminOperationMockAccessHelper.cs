using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Core;

public static class AdminOperationMockAccessHelper
{
    public static SecurityIdentifier? GetCurrentProcessSidWhenUsingMocks()
        => DebugHelper.UseAdminOperationMocks ? WindowsIdentity.GetCurrent().User : null;

    public static string AppendCurrentProcessGenericAllAce(string sddl)
    {
        var currentSid = GetCurrentProcessSidWhenUsingMocks();
        if (currentSid == null)
            return sddl;

        return sddl + $"(A;;GA;;;{currentSid.Value})";
    }

    public static void AddCurrentProcessFileSystemAccess(
        FileSystemSecurity security,
        FileSystemRights rights,
        InheritanceFlags inheritanceFlags,
        PropagationFlags propagationFlags)
    {
        var currentSid = GetCurrentProcessSidWhenUsingMocks();
        if (currentSid == null)
            return;

        security.AddAccessRule(new FileSystemAccessRule(
            currentSid,
            rights,
            inheritanceFlags,
            propagationFlags,
            AccessControlType.Allow));
    }

    public static void AddCurrentProcessFileSystemAccess(
        FileSystemSecurity security,
        FileSystemRights rights)
    {
        var currentSid = GetCurrentProcessSidWhenUsingMocks();
        if (currentSid == null)
            return;

        security.AddAccessRule(new FileSystemAccessRule(
            currentSid,
            rights,
            AccessControlType.Allow));
    }

    public static void AddCurrentProcessPipeAccess(PipeSecurity security, PipeAccessRights rights)
    {
        var currentSid = GetCurrentProcessSidWhenUsingMocks();
        if (currentSid == null)
            return;

        security.AddAccessRule(new PipeAccessRule(
            currentSid,
            rights,
            AccessControlType.Allow));
    }

    public static void AddCurrentProcessMutexAccess(MutexSecurity security, MutexRights rights)
    {
        var currentSid = GetCurrentProcessSidWhenUsingMocks();
        if (currentSid == null)
            return;

        security.AddAccessRule(new MutexAccessRule(
            currentSid,
            rights,
            AccessControlType.Allow));
    }
}
