namespace RunFence.Account;

public interface IProfileRegistryRepairer
{
    bool Repair(CorruptedProfile profile);
}
