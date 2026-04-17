using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;

namespace RunFence.Launch;

/// <summary>
/// Core P/Invoke declarations, structs, and constants shared by launchers.
/// Token acquisition helpers live in <see cref="NativeTokenAcquisition"/>.
/// Environment block helpers live in <see cref="NativeEnvironmentBlock"/>.
/// </summary>
public static class ProcessLaunchNative
{
    public const uint MAXIMUM_ALLOWED = 0x02000000;
    public const uint TOKEN_DUPLICATE = 0x0002;
    public const uint TOKEN_IMPERSONATE = 0x0004;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint STARTF_USESHOWWINDOW = 0x00000001;
    public const ushort SW_SHOWNORMAL = 1;
    public const int TOKEN_LINKED_TOKEN = 19; // TokenLinkedToken
    public const int TOKEN_VIRTUALIZATION_ALLOWED = 23; // TokenVirtualizationAllowed
    public const int TOKEN_VIRTUALIZATION_ENABLED = 24; // TokenVirtualizationEnabled
    public const int HrAlreadyExists = unchecked((int)0x800700B7); // HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)

    public const uint LOGON32_LOGON_INTERACTIVE = 2;
    public const uint LOGON32_PROVIDER_DEFAULT = 0;
    public const uint LOGON_WITH_PROFILE = 0x00000001;
    public const uint SE_GROUP_INTEGRITY = 0x00000020;
    public const int TOKEN_INTEGRITY_LEVEL = 25; // TokenIntegrityLevel
    public const int TOKEN_DEFAULT_DACL = 6; // TokenDefaultDacl
    public const string MediumIntegritySid = "S-1-16-8192";
    public const string LowIntegritySid = "S-1-16-4096";

    // Win32 logon error codes
    public const int Win32ErrorLogonFailure = 1326; // ERROR_LOGON_FAILURE
    public const int Win32ErrorLogonTypeNotGranted = 1385; // ERROR_LOGON_TYPE_NOT_GRANTED

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize;
        public uint dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    public enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    public enum SecurityImpersonationLevel
    {
        SecurityImpersonation = 2
    }

    public const uint ASFW_ANY = 0xFFFFFFFF;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr hToken, uint dwLogonFlags,
        string? lpApplicationName, [In, Out] StringBuilder lpCommandLine,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LoadUserProfile(IntPtr hToken, ref PROFILEINFO lpProfileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool UnloadUserProfile(IntPtr hToken, IntPtr hProfile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROFILEINFO
    {
        public int dwSize;
        public int dwFlags;
        public string? lpUserName;
        public string? lpProfilePath;
        public string? lpDefaultPath;
        public string? lpServerName;
        public string? lpPolicyPath;
        public IntPtr hProfile;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUser(string lpszUsername, string? lpszDomain,
        IntPtr lpszPassword, uint dwLogonType, uint dwLogonProvider, out IntPtr phToken);

    [DllImport("advapi32.dll")]
    public static extern int GetLengthSid(IntPtr pSid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetTokenInformation(IntPtr TokenHandle,
        int TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetTokenInformation(IntPtr TokenHandle,
        int TokenInformationClass, ref uint TokenInformation, uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
        IntPtr lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CreateRestrictedToken(
        IntPtr ExistingTokenHandle,
        uint Flags,
        uint DisableSidCount,
        [MarshalAs(UnmanagedType.LPArray)] SID_AND_ATTRIBUTES[]? SidsToDisable,
        uint DeletePrivilegeCount,
        IntPtr PrivilegesToDelete,
        uint RestrictedSidCount,
        [MarshalAs(UnmanagedType.LPArray)] SID_AND_ATTRIBUTES[]? SidsToRestrict,
        out IntPtr NewTokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr sd, out uint size);

    /// <summary>
    /// Launches a process using <paramref name="hToken"/> via CreateProcessWithTokenW and returns
    /// the resulting <see cref="PROCESS_INFORMATION"/>. The caller is responsible for closing both
    /// handles (<c>hProcess</c> and <c>hThread</c>) after performing any post-launch inspection.
    /// </summary>
    /// <param name="hToken">The primary token to launch under.</param>
    /// <param name="psi">Process start information (file name, arguments, working directory).</param>
    /// <param name="pEnvironment">
    /// Environment block pointer, or <see cref="IntPtr.Zero"/> to inherit. Ownership is NOT
    /// transferred — the caller remains responsible for freeing it.
    /// </param>
    /// <param name="lpDesktop">
    /// Desktop/window-station string (e.g. <c>"WinSta0\Default"</c>), or null to use the default
    /// interactive desktop. Must be set explicitly: when null, CreateProcessWithTokenW (which runs
    /// via seclogon) inherits seclogon's non-interactive desktop, causing STATUS_DLL_INIT_FAILED
    /// (0xC0000142) for .NET apps whose CLR initialization requires an interactive desktop context.
    /// </param>
    /// <param name="hideWindow">
    /// When true, launches the process hidden (SW_HIDE via STARTF_USESHOWWINDOW).
    /// Note: CREATE_NO_WINDOW is intentionally NOT used — when the calling process has no console,
    /// CREATE_NO_WINDOW prevents console subsystem initialization in the child, causing
    /// STATUS_DLL_INIT_FAILED (0xC0000142) for console applications. SW_HIDE alone hides the
    /// window without disrupting the console subsystem.
    /// </param>
    public const string DefaultInteractiveDesktop = "WinSta0\\Default";

    // CreateProcessWithToken and CreateProcessWithLogon are tightly coupled native interop logic —
    // they directly orchestrate P/Invoke calls, handle structs, and manage unmanaged resources.
    // They are not candidates for extraction as business logic.
    public static PROCESS_INFORMATION CreateProcessWithToken(IntPtr hToken,
        ProcessLaunchTarget psi, IntPtr pEnvironment, ILoggingService log, string? lpDesktop = DefaultInteractiveDesktop)
    {
        var creationFlags = pEnvironment != IntPtr.Zero ? CREATE_UNICODE_ENVIRONMENT : 0u;

        if (pEnvironment == IntPtr.Zero && psi.EnvironmentVariables?.Count > 0)
            throw new ArgumentException("Environment variables are specified but EnvironmentBlock was not created");
        
        var cmdLine = ProcessLaunchHelper.BuildCommandLine(psi);
        var cmdLineSb = new StringBuilder(cmdLine, cmdLine.Length + 1);
        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            lpDesktop = lpDesktop,
            dwFlags = STARTF_USESHOWWINDOW,
            wShowWindow = psi.HideWindow ? (ushort)0 : SW_SHOWNORMAL
        };
        var workDir = string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory;

        log.Info($"CreateProcessWithTokenW, workDir = {workDir}, cmdLine = {cmdLine}, lpDesktop = {lpDesktop}");
        if (!CreateProcessWithTokenW(hToken, LOGON_WITH_PROFILE, null, cmdLineSb,
                creationFlags, pEnvironment, workDir, ref si, out var pi))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return pi;
    }
    
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithLogonW(
        string lpUsername,
        string? lpDomain,
        IntPtr lpPassword,
        uint dwLogonFlags,
        string? lpApplicationName,
        [In, Out] StringBuilder lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    public static PROCESS_INFORMATION CreateProcessWithLogon(ProcessLaunchTarget psi, LaunchCredentials credentials, ILoggingService log)
    {
        if (psi.EnvironmentVariables?.Count > 0)
            throw new ArgumentException("Environment variables are not supported, use token launch instead");
        
        IntPtr passwordPtr = IntPtr.Zero;

        try
        {
            passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(credentials.Password!);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                // DO NOT assign desktop here - it will not work
                //lpDesktop = ProcessLaunchNative.DefaultInteractiveDesktop,
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = psi.HideWindow ? (ushort)0 : SW_SHOWNORMAL
            };

            var cmdLine = new StringBuilder(ProcessLaunchHelper.BuildCommandLine(psi));

            var workDir = string.IsNullOrEmpty(psi.WorkingDirectory) ? null : psi.WorkingDirectory;

            log.Info("CreateProcessWithLogonW, workDir = " + workDir + ", cmdLine = " + cmdLine + ", user = " + credentials.Username);

            if (!CreateProcessWithLogonW(
                    credentials.Username!,
                    credentials.Domain,
                    passwordPtr,
                    LOGON_WITH_PROFILE,
                    null,
                    cmdLine,
                    0u,
                    IntPtr.Zero,
                    workDir,
                    ref si,
                    out var pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return pi;
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
            }
        }
    }
}