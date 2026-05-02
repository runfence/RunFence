namespace RunFence.Infrastructure;

public interface IStartupUnlockGrant
{
    void Grant();
    bool TryConsume();
}
