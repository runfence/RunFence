namespace RunFence.Licensing;

public interface IMachineIdProvider
{
    string MachineCode { get; }
    byte[] MachineIdHash { get; }
}
