namespace RunFence.Licensing;

public interface IMachineIdentityReader
{
    string? ReadSmbiosUuid();
    string? ReadWindowsMachineGuid();
}
