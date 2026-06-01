using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

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
        if (contentMode == ShortcutContentMode.PreserveExisting &&
            TryBuildPreparedShortcutFromExisting(sourceShortcutPath, tempShortcutPath, mutation))
        {
            return;
        }

        ApplyShortcutMutation(tempShortcutPath, mutation);
    }

    private void EnsureTempShortcutDeletedForCanonicalFallback(
        string tempShortcutPath,
        Exception cause)
    {
        if (TryDeleteTempShortcut(tempShortcutPath))
            return;

        throw new IOException(
            $"Failed to delete invalid trusted temp shortcut '{tempShortcutPath}' before canonical rewrite.",
            cause);
    }

    private bool TryDeleteTempShortcut(string tempShortcutPath)
    {
        try
        {
            var attributes = File.GetAttributes(tempShortcutPath);
            File.SetAttributes(tempShortcutPath, attributes & ~FileAttributes.ReadOnly);
            File.Delete(tempShortcutPath);
            return !File.Exists(tempShortcutPath) && !Directory.Exists(tempShortcutPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PublishPreparedShortcut(
        string shortcutPath,
        string tempShortcutPath,
        ShortcutDestinationMetadataMode metadataMode)
    {
        var existingMetadata = metadataMode == ShortcutDestinationMetadataMode.PreserveExisting
            ? native.TryCaptureExistingMetadata(shortcutPath)
            : null;
        try
        {
            native.DeleteExistingDestination(shortcutPath);
            native.PublishPreparedShortcut(shortcutPath, tempShortcutPath, existingMetadata);
        }
        catch (ShortcutPublishFailureException ex)
        {
            TryCleanupFailedDestination(shortcutPath);
            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            throw;
        }
    }

    private void ApplyShortcutMutation(string shortcutPath, ShortcutMutation mutation)
    {
        shortcutHelper.WithShortcut(shortcutPath, shortcut =>
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

    private bool TryBuildPreparedShortcutFromExisting(
        string sourceShortcutPath,
        string tempShortcutPath,
        ShortcutMutation mutation)
    {
        if (!File.Exists(sourceShortcutPath))
            return false;

        try
        {
            File.Copy(sourceShortcutPath, tempShortcutPath, overwrite: true);
            File.SetAttributes(tempShortcutPath, File.GetAttributes(tempShortcutPath) & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (IsPreserveExistingCopyFailure(ex))
        {
            EnsureTempShortcutDeletedForCanonicalFallback(tempShortcutPath, ex);
            return false;
        }

        try
        {
            ApplyShortcutMutation(tempShortcutPath, mutation);
            return true;
        }
        catch (Exception ex) when (IsPreserveExistingShortcutEditFailure(ex))
        {
            EnsureTempShortcutDeletedForCanonicalFallback(tempShortcutPath, ex);
            return false;
        }
    }

    private static bool IsPreserveExistingCopyFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException;

    private static bool IsPreserveExistingShortcutEditFailure(Exception ex)
        => ex is InvalidDataException or COMException or IOException or UnauthorizedAccessException;


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
