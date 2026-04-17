namespace RunFence.Startup;

public interface IAutoStartService
{
    bool IsAutoStartEnabled();
    Task EnableAutoStart();
    void DisableAutoStart();
}