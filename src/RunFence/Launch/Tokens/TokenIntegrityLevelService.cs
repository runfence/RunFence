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

    public void SetHighIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => SetIntegrityWithSystemFallback(hToken, "high", ApplyHighIntegrityDirect, out pSid, out tmlBuffer);

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
            log.Info($"Set {integrityLabel} integrity on token requires more privileges; retrying under SYSTEM impersonation.");
        }

        IntPtr retrySid = IntPtr.Zero;
        IntPtr retryBuffer = IntPtr.Zero;
        RunWithSystemPrivileges(integrityLabel, () =>
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

    protected virtual void ApplyHighIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => NativeTokenAcquisition.SetHighIntegrityOnToken(hToken, out pSid, out tmlBuffer);

    private delegate void IntegritySetter(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer);

    private void RunWithSystemPrivileges(string integrityLabel, Action action)
    {
        try
        {
            systemPrivilegeRunner.RunWithPrivileges([TokenPrivilegeHelper.SeRelabelPrivilege, TokenPrivilegeHelper.SeTcbPrivilege], action);
        }
        catch (Win32Exception ex) when (
            string.Equals(integrityLabel, "high", StringComparison.Ordinal) &&
            ex.NativeErrorCode == ErrorNotAllAssigned)
        {
            log.Info("SYSTEM token does not have SeRelabelPrivilege; retrying high integrity with SeTcbPrivilege only.");
            systemPrivilegeRunner.RunWithPrivileges([TokenPrivilegeHelper.SeTcbPrivilege], action);
        }
    }

    private const int ErrorPrivilegeNotHeld = 1314;
    private const int ErrorNotAllAssigned = 1300;
}
