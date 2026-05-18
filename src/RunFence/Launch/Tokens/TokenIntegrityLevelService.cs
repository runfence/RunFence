using System.ComponentModel;
using RunFence.Core;

namespace RunFence.Launch.Tokens;

public class TokenIntegrityLevelService(
    ILoggingService log,
    ISystemPrivilegeRunner systemPrivilegeRunner)
    : ITokenIntegrityLevelService
{
    public void SetLowIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => SetIntegrityWithSystemFallback(hToken, "low", ApplyLowIntegrityDirect, out pSid, out tmlBuffer);

    public void SetMediumIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => SetIntegrityWithSystemFallback(hToken, "medium", ApplyMediumIntegrityDirect, out pSid, out tmlBuffer);

    private void SetIntegrityWithSystemFallback(
        IntPtr hToken,
        string integrityLabel,
        IntegritySetter setter,
        out IntPtr pSid,
        out IntPtr tmlBuffer)
    {
        try
        {
            setter(hToken, out pSid, out tmlBuffer);
            return;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorPrivilegeNotHeld)
        {
            log.Info($"Set {integrityLabel} integrity on token requires SeRelabelPrivilege; retrying under SYSTEM impersonation.");
        }

        IntPtr retrySid = IntPtr.Zero;
        IntPtr retryBuffer = IntPtr.Zero;
        systemPrivilegeRunner.RunWithPrivileges([TokenPrivilegeHelper.SeRelabelPrivilege], () =>
        {
            setter(hToken, out retrySid, out retryBuffer);
        });

        pSid = retrySid;
        tmlBuffer = retryBuffer;
    }

    protected virtual void ApplyLowIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => NativeTokenAcquisition.SetLowIntegrityOnToken(hToken, out pSid, out tmlBuffer);

    protected virtual void ApplyMediumIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => NativeTokenAcquisition.SetMediumIntegrityOnToken(hToken, out pSid, out tmlBuffer);

    private delegate void IntegritySetter(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer);

    private const int ErrorPrivilegeNotHeld = 1314;
}
