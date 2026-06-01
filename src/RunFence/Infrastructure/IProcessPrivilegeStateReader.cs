namespace RunFence.Infrastructure;

public interface IProcessPrivilegeStateReader
{
    bool TryGetProcessElevation(uint processId, out bool isElevated);
    bool TryGetProcessIntegrityLevel(uint processId, out int integrityLevel);
}
