using RunFence.Apps.Shortcuts;

namespace RunFence.IntegrationTests;

internal sealed class FailingOnceShortcutFilePersistenceNative(IShortcutFilePersistenceNative inner) : IShortcutFilePersistenceNative
{
    public int PublishAttempts { get; private set; }
    public int DeleteInvocations { get; private set; }
    public int DeleteExistingFileAttempts { get; private set; }
    public int FailedPublishAttempts { get; private set; }

    public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath)
        => inner.TryCaptureExistingMetadata(shortcutPath);

    public void DeleteExistingDestination(string shortcutPath)
    {
        DeleteInvocations++;
        if (File.Exists(shortcutPath))
            DeleteExistingFileAttempts++;

        inner.DeleteExistingDestination(shortcutPath);
    }

    public void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata)
    {
        PublishAttempts++;
        if (PublishAttempts == 2)
        {
            FailedPublishAttempts++;
            throw new ShortcutPublishRetryableException(
                "Injected publish failure after the initial new-shortcut write.",
                new IOException("Injected publish failure after the initial new-shortcut write."));
        }

        inner.PublishPreparedShortcut(shortcutPath, tempShortcutPath, metadata);
    }
}
