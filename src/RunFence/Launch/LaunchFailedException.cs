namespace RunFence.Launch;

public sealed class LaunchFailedException : Exception
{
    public LaunchFailedException(string message) : base(message)
    {
    }

    public LaunchFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
