using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Low-level ACL operations for container traverse ACEs. Uses SetFileSecurity instead of
/// SetNamedSecurityInfo to avoid the O(n) NTFS auto-inheritance tree walk that can take
/// minutes on large directory trees.
/// </summary>
public static class TraverseAclNative
{
    private const uint DACL_SECURITY_INFORMATION = 0x00000004;
    private const int SE_FILE_OBJECT = 1;
    private const int GRANT_ACCESS = 1;
    private const uint NO_INHERITANCE = 0;
    private const int TRUSTEE_IS_SID = 0;
    private const int SECURITY_DESCRIPTOR_REVISION = 1;

    /// <summary>
    /// Adds a non-inheritable Allow ACE for the given SID with traverse rights
    /// (Traverse | ReadAttributes | Synchronize).
    /// </summary>
    public static void AddAllowAce(string path, SecurityIdentifier sid)
    {
        ApplyAclChange(path, sid, (uint)TraverseRightsHelper.TraverseRights, GRANT_ACCESS);
    }

    /// <summary>
    /// Returns true if <paramref name="dirPath"/> exists and has an explicit non-inheritable Allow ACE
    /// for <paramref name="sid"/> that grants exactly <see cref="TraverseRightsHelper.TraverseRights"/>.
    /// Returns false on any error.
    /// </summary>
    public static bool HasExplicitTraverseAce(string dirPath, SecurityIdentifier sid)
    {
        if (!Directory.Exists(dirPath))
            return false;
        try
        {
            var security = new DirectorySecurity(dirPath, AccessControlSections.Access);
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
            return rules.Cast<FileSystemAccessRule>().Any(rule =>
                rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(sid) && rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                rule.InheritanceFlags == InheritanceFlags.None);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes only explicit Allow ACEs for <paramref name="sid"/> on <paramref name="path"/>
    /// where the ACE grants exactly <see cref="TraverseRightsHelper.TraverseRights"/> (no more, no less)
    /// and has no inheritance flags. ACEs with broader rights (e.g. ReadAndExecute) are left intact.
    /// Uses SetFileSecurity to avoid triggering NTFS auto-inheritance propagation.
    /// </summary>
    public static void RemoveTraverseOnlyAce(string path, SecurityIdentifier sid)
    {
        if (!Directory.Exists(path))
            return;

        var dirInfo = new DirectoryInfo(path);
        var security = dirInfo.GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        bool changed = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier ruleSid &&
                ruleSid.Equals(sid) &&
                rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                rule.InheritanceFlags == InheritanceFlags.None)
            {
                security.RemoveAccessRuleSpecific(rule);
                changed = true;
            }
        }

        if (!changed)
            return;

        // Write back via SetFileSecurity to avoid auto-inheritance propagation on large trees
        var sdBytes = security.GetSecurityDescriptorBinaryForm();
        var sdHandle = GCHandle.Alloc(sdBytes, GCHandleType.Pinned);
        try
        {
            if (!SetFileSecurity(path, DACL_SECURITY_INFORMATION, sdHandle.AddrOfPinnedObject()))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"SetFileSecurity failed on '{path}'");
        }
        finally
        {
            sdHandle.Free();
        }
    }

    private static void ApplyAclChange(string path, SecurityIdentifier sid, uint accessMask, int accessMode)
    {
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        var sidHandle = GCHandle.Alloc(sidBytes, GCHandleType.Pinned);
        try
        {
            var sidPtr = sidHandle.AddrOfPinnedObject();

            // Get existing DACL
            int result = GetNamedSecurityInfo(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
                out _, out _, out var dacl, out _, out var sd);
            if (result != 0)
                throw new Win32Exception(result, $"GetNamedSecurityInfo failed on '{path}'");

            try
            {
                // Merge/revoke ACE in existing DACL
                var ea = new EXPLICIT_ACCESS
                {
                    grfAccessPermissions = accessMask,
                    grfAccessMode = accessMode,
                    grfInheritance = NO_INHERITANCE,
                    Trustee = new TRUSTEE
                    {
                        TrusteeForm = TRUSTEE_IS_SID,
                        ptstrName = sidPtr
                    }
                };

                result = SetEntriesInAcl(1, ref ea, dacl, out var newDacl);
                if (result != 0)
                    throw new Win32Exception(result, $"SetEntriesInAcl failed on '{path}'");

                try
                {
                    // Build a minimal absolute SD with just the DACL.
                    // InitializeSecurityDescriptor creates an absolute-format SD whose size
                    // depends on pointer width (20 bytes on x86, 40 on x64).
                    int sdSize = Marshal.SizeOf<SECURITY_DESCRIPTOR>();
                    var newSd = Marshal.AllocHGlobal(sdSize);
                    try
                    {
                        if (!InitializeSecurityDescriptor(newSd, SECURITY_DESCRIPTOR_REVISION) || !SetSecurityDescriptorDacl(newSd, true, newDacl, false))
                            throw new Win32Exception(Marshal.GetLastWin32Error());

                        // SetFileSecurity does NOT trigger auto-inheritance propagation —
                        // it doesn't set SE_DACL_AUTO_INHERIT_REQ on the SD control flags,
                        // so NTFS skips the child tree walk that can take minutes on large trees.
                        if (!SetFileSecurity(path, DACL_SECURITY_INFORMATION, newSd))
                            throw new Win32Exception(Marshal.GetLastWin32Error(),
                                $"SetFileSecurity failed on '{path}'");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(newSd);
                    }
                }
                finally
                {
                    NativeMethods.LocalFree(newDacl);
                }
            }
            finally
            {
                NativeMethods.LocalFree(sd);
            }
        }
        finally
        {
            sidHandle.Free();
        }
    }

    #region P/Invoke

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetNamedSecurityInfo(
        string pObjectName, int objectType, uint securityInfo,
        out IntPtr pSidOwner, out IntPtr pSidGroup,
        out IntPtr pDacl, out IntPtr pSacl, out IntPtr pSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetEntriesInAcl(
        int cCountOfExplicitEntries, ref EXPLICIT_ACCESS pListOfExplicitEntries,
        IntPtr oldAcl, out IntPtr newAcl);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetFileSecurity(
        string lpFileName, uint securityInformation, IntPtr pSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool InitializeSecurityDescriptor(IntPtr pSecurityDescriptor, int dwRevision);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetSecurityDescriptorDacl(
        IntPtr pSecurityDescriptor, bool bDaclPresent, IntPtr pDacl, bool bDaclDefaulted);

    /// <summary>
    /// Absolute-format security descriptor. Used only for Marshal.SizeOf to get the
    /// correct allocation size (20 bytes on x86, 40 on x64).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_DESCRIPTOR
    {
        public byte Revision;
        public byte Sbz1;
        public ushort Control;
        public IntPtr Owner;
        public IntPtr Group;
        public IntPtr Sacl;
        public IntPtr Dacl;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXPLICIT_ACCESS
    {
        public uint grfAccessPermissions;
        public int grfAccessMode;
        public uint grfInheritance;
        public TRUSTEE Trustee;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRUSTEE
    {
        public IntPtr pMultipleTrustee;
        public int MultipleTrusteeOperation;
        public int TrusteeForm;
        public int TrusteeType;
        public IntPtr ptstrName;
    }

    #endregion
}