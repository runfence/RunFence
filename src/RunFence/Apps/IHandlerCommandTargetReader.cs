namespace RunFence.Apps;

public interface IHandlerCommandTargetReader
{
    IReadOnlyList<HandlerCommandTarget> ReadTargets(string? targetAccountSid);
}
