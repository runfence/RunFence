namespace RunFence.Startup;

public interface IAutoStartService
{
    bool IsAutoStartEnabled();
    void EnableAutoStart();
    void DisableAutoStart();
}