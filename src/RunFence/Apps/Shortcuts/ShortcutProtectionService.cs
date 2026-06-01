using System.Security.AccessControl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public class ShortcutProtectionService(
    ILoggingService log,
    IShortcutProtectionStateStore stateStore,
    ShortcutProtectionOwnershipCalculator ownershipCalculator,
    ShortcutManagedDenyAceEditor managedDenyAceEditor,
    InternalShortcutAclEditor internalShortcutAclEditor) : IShortcutProtectionService
{
    public void ProtectShortcut(
        string appId,
        string shortcutPath,
        bool allowAdministratorsDelete = false)
    {
        if (!File.Exists(shortcutPath))
            return;

        var existingState = TryLoadExistingStateForProtect(appId, shortcutPath);
        var attrs = File.GetAttributes(shortcutPath);
        var wasReadOnlyBeforeProtection = (attrs & FileAttributes.ReadOnly) != 0;
        var hasManagedDenyAce = TryReadHasManagedDenyAce(shortcutPath);
        var protectionState = ownershipCalculator.BuildState(
            shortcutPath,
            existingState,
            wasReadOnlyBeforeProtection,
            hasManagedDenyAce,
            allowAdministratorsDelete);

        PersistProtectionStateForAdd(appId, shortcutPath, protectionState);

        var readOnlyAppliedBeforeAcl = false;
        if (!allowAdministratorsDelete && !wasReadOnlyBeforeProtection)
        {
            protectionState = TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                appId,
                existingState,
                protectionState,
                "Failed to mark shortcut as read-only");
            readOnlyAppliedBeforeAcl = (File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0;
        }

        try
        {
            if (allowAdministratorsDelete)
            {
                RemoveManagedDenyAceIfOwned(existingState, shortcutPath);
            }
            else
            {
                managedDenyAceEditor.AddManagedDenyAce(shortcutPath);
            }
        }
        catch (Exception ex)
        {
            var failure = TryRollbackReadOnlyAfterAclFailure(shortcutPath, attrs, readOnlyAppliedBeforeAcl, ex);
            RestorePriorStateAfterOsFailure(appId, shortcutPath, existingState, failure, "apply");
            throw new ShortcutProtectionException(shortcutPath, "apply", failure);
        }

        if (allowAdministratorsDelete && !wasReadOnlyBeforeProtection)
            TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                appId,
                existingState,
                protectionState,
                "Failed to mark shortcut as read-only");
    }

    public void UnprotectShortcut(string appId, string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return;

        ShortcutProtectionState? state;
        try
        {
            state = stateStore.Load(appId, shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "load", ex);
        }

        if (state?.ManagedDenyAceApplied == true)
        {
            try
            {
                managedDenyAceEditor.RemoveManagedDenyAce(shortcutPath);
            }
            catch (Exception ex)
            {
                throw new ShortcutProtectionException(shortcutPath, "remove", ex);
            }
        }

        if (state?.ReadOnlySetByRunFence == true)
        {
            try
            {
                var attrs = File.GetAttributes(shortcutPath);
                File.SetAttributes(shortcutPath, attrs & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                throw new ShortcutProtectionException(shortcutPath, "remove", ex);
            }
        }

        try
        {
            stateStore.Delete(appId, shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "clear persisted", ex);
        }
    }

    /// <summary>
    /// Applies stricter ACL: LocalSystem=FullControl, Administrators=FullControl, accountSid=ReadAndExecute.
    /// In admin-operation mock mode, the current process SID also gets FullControl so the
    /// non-elevated debug process can still maintain the shortcut.
    /// Used for shortcuts created inside managed folder apps. Only the specific account that
    /// owns the app entry gets access — not the entire Users group, since membership is not guaranteed.
    /// </summary>
    public void ProtectInternalShortcut(
        string appId,
        string shortcutPath,
        string accountSid)
    {
        if (!File.Exists(shortcutPath))
            return;

        var existingState = TryLoadExistingStateForProtect(appId, shortcutPath);
        var attrs = File.GetAttributes(shortcutPath);
        var wasReadOnlyBeforeProtection = (attrs & FileAttributes.ReadOnly) != 0;
        var protectionState = ownershipCalculator.BuildState(
            shortcutPath,
            existingState,
            wasReadOnlyBeforeProtection,
            hasOrdinaryManagedDenyAce: false,
            allowAdministratorsDelete: true);

        PersistProtectionStateForAdd(appId, shortcutPath, protectionState);

        var readOnlyAppliedBeforeAcl = false;
        if (!wasReadOnlyBeforeProtection)
        {
            protectionState = TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                appId,
                existingState,
                protectionState,
                "Failed to mark internal shortcut as read-only");
            readOnlyAppliedBeforeAcl = (File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0;
        }

        try
        {
            internalShortcutAclEditor.Protect(shortcutPath, accountSid);
        }
        catch (Exception ex)
        {
            var failure = TryRollbackReadOnlyAfterAclFailure(shortcutPath, attrs, readOnlyAppliedBeforeAcl, ex);
            RestorePriorStateAfterOsFailure(appId, shortcutPath, existingState, failure, "apply");
            throw new ShortcutProtectionException(shortcutPath, "apply", failure);
        }
    }

    private void SaveOrDeleteProtectionState(string appId, ShortcutProtectionState state)
    {
        if (!state.ManagedDenyAceApplied && !state.ReadOnlySetByRunFence)
        {
            stateStore.Delete(appId, state.ShortcutPath);
            return;
        }

        stateStore.Save(appId, state);
    }

    private ShortcutProtectionState? TryLoadExistingStateForProtect(string appId, string shortcutPath)
    {
        try
        {
            return stateStore.Load(appId, shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "load", ex);
        }
    }

    private bool TryReadHasManagedDenyAce(string shortcutPath)
    {
        try
        {
            return managedDenyAceEditor.HasManagedDenyAce(shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "inspect", ex);
        }
    }

    private void PersistProtectionStateForAdd(string appId, string shortcutPath, ShortcutProtectionState newState)
    {
        try
        {
            SaveOrDeleteProtectionState(appId, newState);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "persist", ex);
        }
    }

    private void PersistStateAfterReadOnlyFailure(string appId, string shortcutPath, ShortcutProtectionState correctedState)
    {
        try
        {
            SaveOrDeleteProtectionState(appId, correctedState);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "persist", ex);
        }
    }

    private ShortcutProtectionState TryApplyReadOnlyAttribute(
        string shortcutPath,
        FileAttributes originalAttributes,
        string appId,
        ShortcutProtectionState? existingState,
        ShortcutProtectionState protectionState,
        string logMessage)
    {
        try
        {
            File.SetAttributes(shortcutPath, originalAttributes | FileAttributes.ReadOnly);
            return protectionState;
        }
        catch (Exception ex)
        {
            log.Error($"{logMessage}: {shortcutPath}", ex);
            var correctedState = protectionState with { ReadOnlySetByRunFence = existingState?.ReadOnlySetByRunFence == true };
            PersistStateAfterReadOnlyFailure(appId, shortcutPath, correctedState);
            return correctedState;
        }
    }

    private Exception TryRollbackReadOnlyAfterAclFailure(
        string shortcutPath,
        FileAttributes originalAttributes,
        bool readOnlyAppliedBeforeAcl,
        Exception originalException)
    {
        if (!readOnlyAppliedBeforeAcl)
            return originalException;

        try
        {
            File.SetAttributes(shortcutPath, originalAttributes);
            return originalException;
        }
        catch (Exception rollbackException)
        {
            return new AggregateException(originalException, rollbackException);
        }
    }

    private void RestorePriorStateAfterOsFailure(
        string appId,
        string shortcutPath,
        ShortcutProtectionState? existingState,
        Exception originalException,
        string operation)
    {
        try
        {
            if (existingState == null)
                stateStore.Delete(appId, shortcutPath);
            else
                stateStore.Save(appId, existingState);
        }
        catch (Exception restoreEx)
        {
            throw new ShortcutProtectionException(
                shortcutPath,
                operation,
                new AggregateException(originalException, restoreEx));
        }
    }

    private void RemoveManagedDenyAceIfOwned(
        ShortcutProtectionState? existingState,
        string shortcutPath)
    {
        if (existingState?.ManagedDenyAceApplied == true)
        {
            managedDenyAceEditor.RemoveManagedDenyAce(shortcutPath);
        }
    }
}
