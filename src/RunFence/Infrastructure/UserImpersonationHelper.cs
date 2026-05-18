using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Infrastructure;

public class UserImpersonationHelper : IUserImpersonationHelper
{
    private readonly ISidResolver _sidResolver;
    private readonly IProfilePathResolver _profilePathResolver;
    private readonly ILoggingService _log;

    public UserImpersonationHelper(ISidResolver sidResolver, IProfilePathResolver profilePathResolver, ILoggingService log)
    {
        _sidResolver = sidResolver;
        _profilePathResolver = profilePathResolver;
        _log = log;
    }

    public (string profilePath, T result) RunImpersonated<T>(
        string targetSid, ProtectedString password, Func<T> action)
    {
        var fullName = _sidResolver.TryResolveName(targetSid);
        string domain, username;
        if (fullName != null && fullName.Contains('\\'))
        {
            var idx = fullName.IndexOf('\\');
            domain = fullName[..idx];
            username = fullName[(idx + 1)..];
        }
        else
        {
            domain = Environment.MachineName;
            username = fullName ?? targetSid;
        }

        IntPtr hToken = IntPtr.Zero;
        password.UseUnicodeSnapshot(snapshot =>
        {
            if (!ProcessLaunchNative.LogonUser(
                    username,
                    domain,
                    snapshot.DangerousGetIntPtr(),
                    ProcessLaunchNative.LOGON32_LOGON_INTERACTIVE,
                    ProcessLaunchNative.LOGON32_PROVIDER_DEFAULT,
                    out hToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        });

        using var safeToken = new SafeAccessTokenHandle(hToken);

        var profileInfo = new ProcessLaunchNative.PROFILEINFO
        {
            dwSize = Marshal.SizeOf<ProcessLaunchNative.PROFILEINFO>(),
            dwFlags = ProcessLaunchNative.PI_NOUI,
            lpUserName = username
        };
        bool profileLoaded = ProcessLaunchNative.LoadUserProfile(
            safeToken.DangerousGetHandle(), ref profileInfo);
        if (!profileLoaded)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LoadUserProfile failed for {targetSid}.");

        try
        {
            var profilePath = _profilePathResolver.TryGetProfilePath(targetSid)
                ?? throw new InvalidOperationException(
                    $"Profile path not found for {targetSid} even after LoadUserProfile.");
            var result = WindowsIdentity.RunImpersonated(safeToken, () =>
            {
                var impersonatedSid = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.Equals(impersonatedSid, targetSid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Impersonation did not switch to the target SID. Expected {targetSid}, got {impersonatedSid ?? "<null>"}.");
                }

                // Keep the impersonated SecurityContext from flowing into later work.
                if (ExecutionContext.IsFlowSuppressed())
                    return action();

                using var _ = ExecutionContext.SuppressFlow();
                return action();
            });
            return (profilePath, result);
        }
        finally
        {
            if (profileLoaded && profileInfo.hProfile != IntPtr.Zero)
            {
                if (!ProcessLaunchNative.UnloadUserProfile(
                        safeToken.DangerousGetHandle(), profileInfo.hProfile))
                    _log.Warn($"UnloadUserProfile returned false for {targetSid}");
            }
        }
    }
}
