namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutFilePersistenceService(
    IShortcutComHelper shortcutHelper,
    IShortcutFilePersistenceNative native,
    string trustedTempRootPath) : IShortcutFilePersistenceService
{
    public void PersistShortcut(
        string shortcutPath,
        ShortcutMutation mutation,
        ShortcutDestinationMetadataMode metadataMode,
        ShortcutContentMode contentMode)
    {
        Directory.CreateDirectory(trustedTempRootPath);
        var tempShortcutPath = Path.Combine(trustedTempRootPath, $"{Guid.NewGuid():N}.lnk");
        try
        {
            BuildPreparedShortcut(shortcutPath, tempShortcutPath, mutation, contentMode);
            PublishPreparedShortcut(shortcutPath, tempShortcutPath, metadataMode);
        }
        finally
        {
            TryDeleteTempShortcut(tempShortcutPath);
        }
    }

    private void BuildPreparedShortcut(
        string sourceShortcutPath,
        string tempShortcutPath,
        ShortcutMutation mutation,
        ShortcutContentMode contentMode)
    {
        if (contentMode == ShortcutContentMode.PreserveExisting && File.Exists(sourceShortcutPath))
        {
            File.Copy(sourceShortcutPath, tempShortcutPath, overwrite: true);
            File.SetAttributes(tempShortcutPath, File.GetAttributes(tempShortcutPath) & ~FileAttributes.ReadOnly);
        }

        shortcutHelper.WithShortcut(tempShortcutPath, shortcut =>
        {
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = mutation.TargetPath;
            dynamicShortcut.Arguments = mutation.Arguments;
            dynamicShortcut.WorkingDirectory = mutation.WorkingDirectory;
            dynamicShortcut.Description = mutation.Description;
            dynamicShortcut.Hotkey = mutation.Hotkey;
            dynamicShortcut.WindowStyle = mutation.WindowStyle;

            if (mutation.IconUpdateMode == ShortcutIconUpdateMode.Set)
            {
                dynamicShortcut.IconLocation = mutation.IconLocation;
            }
            else if (mutation.IconUpdateMode == ShortcutIconUpdateMode.ClearBestEffort)
            {
                try
                {
                    dynamicShortcut.IconLocation = "";
                }
                catch
                {
                }
            }
            else if (!string.IsNullOrWhiteSpace(mutation.IconLocation))
            {
                dynamicShortcut.IconLocation = mutation.IconLocation;
            }

            dynamicShortcut.Save();
        });
    }

    private static void TryDeleteTempShortcut(string tempShortcutPath)
    {
        if (!File.Exists(tempShortcutPath))
            return;

        try
        {
            File.SetAttributes(tempShortcutPath, File.GetAttributes(tempShortcutPath) & ~FileAttributes.ReadOnly);
            File.Delete(tempShortcutPath);
        }
        catch
        {
        }
    }

    private void PublishPreparedShortcut(
        string shortcutPath,
        string tempShortcutPath,
        ShortcutDestinationMetadataMode metadataMode)
    {
        var destinationExisted = File.Exists(shortcutPath);
        var existingMetadata = metadataMode == ShortcutDestinationMetadataMode.PreserveExisting && destinationExisted
            ? native.TryCaptureExistingMetadata(shortcutPath)
            : null;
        Exception? lastFailure = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (destinationExisted)
                    native.DeleteExistingDestination(shortcutPath);

                native.PublishPreparedShortcut(shortcutPath, tempShortcutPath, existingMetadata);
                return;
            }
            catch (ShortcutPublishRetryableException ex) when (attempt == 0)
            {
                lastFailure = ex.InnerException ?? ex;
                TryCleanupFailedDestination(shortcutPath);
            }
        }

        if (lastFailure != null)
            throw lastFailure;

        throw new InvalidOperationException("Shortcut persistence failed without an exception.");
    }

    private void TryCleanupFailedDestination(string shortcutPath)
    {
        try
        {
            native.DeleteExistingDestination(shortcutPath);
        }
        catch
        {
        }
    }
}
