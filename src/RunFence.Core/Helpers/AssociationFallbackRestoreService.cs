namespace RunFence.Core.Helpers;

public class AssociationFallbackRestoreService(IAssociationFallbackRegistry registry)
{
    public AssociationFallbackRestoreResult RestoreFromFallback(
        string association,
        FallbackCleanupMode cleanupMode,
        string? targetSid = null)
    {
        if (cleanupMode == FallbackCleanupMode.NoRegistryCleanup)
            return new AssociationFallbackRestoreResult(
                AssociationFallbackRegistryStatus.NoFallbackRecorded,
                AssociationFallbackShellNotifyStatus.NotRequired,
                null,
                null,
                null);

        try
        {
            using var root = registry.OpenUserClassesRoot(targetSid);
            if (root == null)
                return new AssociationFallbackRestoreResult(
                    AssociationFallbackRegistryStatus.NoFallbackRecorded,
                    AssociationFallbackShellNotifyStatus.NotRequired,
                    null,
                    null,
                    null);

            var fallbackValue = registry.ReadFallbackCommand(root, association);
            if (fallbackValue == null)
                return new AssociationFallbackRestoreResult(
                    AssociationFallbackRegistryStatus.NoFallbackRecorded,
                    AssociationFallbackShellNotifyStatus.NotRequired,
                    null,
                    null,
                    null);

            registry.WriteDefaultCommand(root, association, fallbackValue);
            if (association.StartsWith('.'))
                registry.DeleteExtensionCommandSubkeys(root, association);
            registry.DeleteFallbackValue(root, association);

            try
            {
                registry.NotifyShellChanged();
                return new AssociationFallbackRestoreResult(
                    AssociationFallbackRegistryStatus.Succeeded,
                    AssociationFallbackShellNotifyStatus.Succeeded,
                    fallbackValue,
                    null,
                    null);
            }
            catch (Exception notifyEx)
            {
                return new AssociationFallbackRestoreResult(
                    AssociationFallbackRegistryStatus.Succeeded,
                    AssociationFallbackShellNotifyStatus.Failed,
                    fallbackValue,
                    notifyEx.Message,
                    null);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return new AssociationFallbackRestoreResult(
                AssociationFallbackRegistryStatus.AccessDenied,
                AssociationFallbackShellNotifyStatus.NotRequired,
                null,
                null,
                ex.Message);
        }
        catch (Exception ex)
        {
            return new AssociationFallbackRestoreResult(
                AssociationFallbackRegistryStatus.Failed,
                AssociationFallbackShellNotifyStatus.NotRequired,
                null,
                null,
                ex.Message);
        }
    }
}
