namespace RunFence.Apps.Shortcuts;

internal sealed class ShortcutPostWriteException(ShortcutWriteResult result, Exception innerException)
    : Exception(innerException.Message, innerException)
{
    public ShortcutWriteResult Result { get; } = result;
}
