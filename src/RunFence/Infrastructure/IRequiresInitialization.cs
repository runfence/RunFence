namespace RunFence.Infrastructure;

/// <summary>Services that must have Initialize() called once after the DI container is built.</summary>
public interface IRequiresInitialization
{
    void Initialize();
}