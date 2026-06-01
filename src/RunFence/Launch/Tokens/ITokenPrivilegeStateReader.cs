namespace RunFence.Launch.Tokens;

public interface ITokenPrivilegeStateReader
{
    bool IsElevated(IntPtr hToken);

    bool TryGetIntegrityLevel(IntPtr hToken, out int integrityLevel);
}
