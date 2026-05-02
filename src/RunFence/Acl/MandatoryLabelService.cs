using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Low-level SACL mandatory-label operations for Low Integrity grants.
/// </summary>
public class MandatoryLabelService(ILoggingService log, IFileSystemPathInfo pathInfo) : IMandatoryLabelService
{
    public void ApplyLowIntegrityLabel(string path) =>
        ApplyMandatoryLabelSddl(path, "S:(ML;;NW;;;LW)", nameof(ApplyLowIntegrityLabel));

    public string? ReadMandatoryLabel(string path)
    {
        if (!pathInfo.FileExists(path) && !pathInfo.DirectoryExists(path)) return null;
        var error = FileSecurityNative.GetNamedSecurityInfo(
            path, FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
            FileSecurityNative.SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
            out _, out _, out _, out _, out var ppSd);
        if (error != 0 || ppSd == IntPtr.Zero) return null;
        try
        {
            bool ok = FileSecurityNative.ConvertSecurityDescriptorToStringSecurityDescriptor(
                ppSd, 1,
                FileSecurityNative.SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
                out var strSd, out _);
            if (!ok || strSd == IntPtr.Zero) return null;
            try
            {
                var result = Marshal.PtrToStringUni(strSd);
                return string.IsNullOrEmpty(result) || result == "S:" ? null : result;
            }
            finally { ProcessNative.LocalFree(strSd); }
        }
        finally { ProcessNative.LocalFree(ppSd); }
    }

    public void RestoreMandatoryLabel(string path, string? previousLabel)
    {
        if (!pathInfo.FileExists(path) && !pathInfo.DirectoryExists(path)) return;
        if (previousLabel == null)
            SetMandatoryLabelSacl(path, IntPtr.Zero, nameof(RestoreMandatoryLabel));
        else
            ApplyMandatoryLabelSddl(path, previousLabel, nameof(RestoreMandatoryLabel));
    }

    private void ApplyMandatoryLabelSddl(string path, string sddl, string caller)
    {
        if (!pathInfo.FileExists(path) && !pathInfo.DirectoryExists(path)) return;
        if (!FileSecurityNative.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl, 1, out var sd, out _))
        {
            log.Warn($"{caller}: failed to parse SDDL for '{path}'");
            return;
        }
        try
        {
            FileSecurityNative.GetSecurityDescriptorSacl(sd, out _, out var sacl, out _);
            SetMandatoryLabelSacl(path, sacl, caller);
        }
        finally { ProcessNative.LocalFree(sd); }
    }

    private void SetMandatoryLabelSacl(string path, IntPtr sacl, string caller)
    {
        var error = FileSecurityNative.SetNamedSecurityInfo(
            path, FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
            FileSecurityNative.SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
        if (error != 5)
        {
            if (error != 0)
                log.Warn($"{caller}: SetNamedSecurityInfo failed on '{path}' (Win32 error {error})");
            return;
        }
        var hFile = FileSecurityNative.CreateFile(path, FileSecurityNative.WRITE_OWNER,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero, FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (hFile == FileSecurityNative.INVALID_HANDLE_VALUE)
        {
            log.Warn($"{caller}: backup CreateFile failed on '{path}' (Win32 error {Marshal.GetLastWin32Error()})");
            return;
        }
        try
        {
            var handleError = FileSecurityNative.SetSecurityInfo(
                hFile, FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
                FileSecurityNative.SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            if (handleError != 0)
                log.Warn($"{caller}: backup SetSecurityInfo failed on '{path}' (Win32 error {handleError})");
        }
        finally { ProcessNative.CloseHandle(hFile); }
    }
}
