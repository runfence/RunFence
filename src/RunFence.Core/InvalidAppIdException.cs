namespace RunFence.Core;

public sealed class InvalidAppIdException(string? appId, string message) : Exception(message)
{
    public string? AppId { get; } = appId;
}
