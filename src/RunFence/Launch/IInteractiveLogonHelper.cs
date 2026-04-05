namespace RunFence.Launch;

public interface IInteractiveLogonHelper
{
    T RunWithLogonRetry<T>(string? domain, string? username, Func<T> action);
}