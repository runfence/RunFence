namespace RunFence.Apps.Shortcuts;

internal sealed class ShortcutPublishRetryableException(
    string message,
    Exception innerException) : IOException(message, innerException);
