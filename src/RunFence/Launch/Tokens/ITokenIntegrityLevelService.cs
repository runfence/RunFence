namespace RunFence.Launch.Tokens;

public interface ITokenIntegrityLevelService
{
    void SetLowIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer);

    void SetMediumIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer);

    void SetHighIntegrity(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer);
}
