namespace RunFence.Licensing;

public interface IMachineIdProvider
{
    MachineIdentityResult GetMachineIdentity();
    string MachineCode { get; }
    byte[] MachineIdHash { get; }
}
