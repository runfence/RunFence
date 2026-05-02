namespace RunFence.Startup;

public interface IAutoStartService
{
    Task<bool> IsAutoStartEnabled();
    Task EnableAutoStart();
    Task DisableAutoStart();
}
