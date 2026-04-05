namespace RunFence.Infrastructure;

/// <summary>Services that must have Start() called once after initialization to begin background work.</summary>
public interface IBackgroundService
{
    void Start();
}