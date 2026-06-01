namespace RunFence.Apps.Shortcuts;

internal sealed class ShortcutPublishFailureException(
    string message,
    Exception innerException) : Exception(message, innerException);
