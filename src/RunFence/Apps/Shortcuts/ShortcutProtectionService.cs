using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;

namespace RunFence.Apps.Shortcuts;

public class ShortcutProtectionService(
    ILoggingService log,
    IAclAccessor aclAccessor,
    IShortcutProtectionStateStore stateStore) : IShortcutProtectionService
{
    private const FileSystemRights ManagedDenyRights = FileSystemRights.Delete |
                                                       FileSystemRights.Write |
                                                       FileSystemRights.WriteAttributes |
                                                       FileSystemRights.AppendData;

    public void ProtectShortcut(string shortcutPath, bool allowAdministratorsDelete = false)
    {
        if (!File.Exists(shortcutPath))
            return;

        var existingState = TryLoadExistingStateForProtect(shortcutPath);
        var attrs = File.GetAttributes(shortcutPath);
        var wasReadOnlyBeforeProtection = (attrs & FileAttributes.ReadOnly) != 0;
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var hasManagedDenyAce = allowAdministratorsDelete
            ? false
            : TryReadHasManagedDenyAce(shortcutPath, everyoneSid);
        var protectionState = new ShortcutProtectionState(
            shortcutPath,
            allowAdministratorsDelete
                ? false
                : existingState?.ManagedDenyAceApplied == true || !hasManagedDenyAce,
            existingState?.WasReadOnlyBeforeProtection ?? wasReadOnlyBeforeProtection,
            existingState?.ReadOnlySetByRunFence == true || !wasReadOnlyBeforeProtection);

        PersistProtectionStateForAdd(shortcutPath, protectionState);

        var readOnlyAppliedBeforeAcl = false;
        if (!allowAdministratorsDelete && !wasReadOnlyBeforeProtection)
        {
            protectionState = TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                existingState,
                protectionState,
                "Failed to mark shortcut as read-only");
            readOnlyAppliedBeforeAcl = (File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0;
        }

        try
        {
            if (allowAdministratorsDelete)
            {
                aclAccessor.ModifyAclWithFallback(shortcutPath, isFolder: false, security =>
                {
                    if (existingState?.ManagedDenyAceApplied != true)
                        return false;

                    var rule = FindManagedEveryoneDenyRule(security, everyoneSid);
                    if (rule == null)
                        return false;

                    security.RemoveAccessRuleSpecific(rule);
                    return true;
                });
            }
            else
            {
                aclAccessor.ModifyAclWithFallback(shortcutPath, isFolder: false, security =>
                {
                    if (HasManagedEveryoneDenyAce(security, everyoneSid))
                        return false;

                    security.AddAccessRule(new FileSystemAccessRule(everyoneSid, ManagedDenyRights, AccessControlType.Deny));
                    return true;
                });
            }
        }
        catch (Exception ex)
        {
            var failure = TryRollbackReadOnlyAfterAclFailure(shortcutPath, attrs, readOnlyAppliedBeforeAcl, ex);
            RestorePriorStateAfterOsFailure(shortcutPath, existingState, failure, "apply");
            throw new ShortcutProtectionException(shortcutPath, "apply", failure);
        }

        if (allowAdministratorsDelete && !wasReadOnlyBeforeProtection)
            TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                existingState,
                protectionState,
                "Failed to mark shortcut as read-only");
    }

    public void UnprotectShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return;

        ShortcutProtectionState? state;
        try
        {
            state = stateStore.Load(shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "load", ex);
        }
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

        if (state?.ManagedDenyAceApplied == true)
        {
            try
            {
                aclAccessor.ModifyAclWithFallback(shortcutPath, isFolder: false, security =>
                {
                    var rule = FindManagedEveryoneDenyRule(security, everyoneSid);
                    if (rule == null)
                        return false;

                    security.RemoveAccessRuleSpecific(rule);
                    return true;
                });
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
                log.Error($"Failed to remove shortcut read-only attribute: {shortcutPath}", ex);
            }
        }

        try
        {
            stateStore.Delete(shortcutPath);
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
    public void ProtectInternalShortcut(string shortcutPath, string accountSid)
    {
        if (!File.Exists(shortcutPath))
            return;

        var existingState = TryLoadExistingStateForProtect(shortcutPath);
        var attrs = File.GetAttributes(shortcutPath);
        var wasReadOnlyBeforeProtection = (attrs & FileAttributes.ReadOnly) != 0;
        var protectionState = new ShortcutProtectionState(
            shortcutPath,
            ManagedDenyAceApplied: false,
            existingState?.WasReadOnlyBeforeProtection ?? wasReadOnlyBeforeProtection,
            existingState?.ReadOnlySetByRunFence == true || !wasReadOnlyBeforeProtection);

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var accountIdentity = new SecurityIdentifier(accountSid);
        // Include BuiltinUsersSid so any legacy ACEs from the old code get detected and removed.
        var legacyUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var managedSids = new HashSet<SecurityIdentifier> { systemSid, adminsSid, accountIdentity, legacyUsersSid, everyoneSid };
        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
            managedSids.Add(currentMockSid);

        var desiredSet = new HashSet<(string sid, FileSystemRights rights, AccessControlType type)>
        {
            (systemSid.Value, FileSystemRights.FullControl, AccessControlType.Allow),
            (adminsSid.Value, FileSystemRights.FullControl, AccessControlType.Allow),
            (accountIdentity.Value, FileSystemRights.ReadAndExecute, AccessControlType.Allow),
        };
        if (currentMockSid != null)
            desiredSet.Add((currentMockSid.Value, FileSystemRights.FullControl, AccessControlType.Allow));

        PersistProtectionStateForAdd(shortcutPath, protectionState);

        var readOnlyAppliedBeforeAcl = false;
        if (!wasReadOnlyBeforeProtection)
        {
            protectionState = TryApplyReadOnlyAttribute(
                shortcutPath,
                attrs,
                existingState,
                protectionState,
                "Failed to mark internal shortcut as read-only");
            readOnlyAppliedBeforeAcl = (File.GetAttributes(shortcutPath) & FileAttributes.ReadOnly) != 0;
        }

        try
        {
            aclAccessor.ModifyAclWithFallback(shortcutPath, isFolder: false, security =>
            {
                var inheritanceBroken = security.AreAccessRulesProtected;
                var existingSet = new HashSet<(string sid, FileSystemRights rights, AccessControlType type)>();
                var existingManaged = new List<FileSystemAccessRule>();
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                {
                    if (rule.IdentityReference is not SecurityIdentifier sid || !managedSids.Contains(sid))
                        continue;

                    existingManaged.Add(rule);
                    // Strip Synchronize from allow ACEs: Windows may auto-add it on read-back,
                    // so ignore it to avoid false "changed" detection.
                    var rights = rule.AccessControlType == AccessControlType.Allow
                        ? rule.FileSystemRights & ~FileSystemRights.Synchronize
                        : rule.FileSystemRights;
                    existingSet.Add((sid.Value, rights, rule.AccessControlType));
                }

                var aclChanged = !inheritanceBroken || !desiredSet.SetEquals(existingSet);
                if (!aclChanged)
                    return false;

                if (!inheritanceBroken)
                    security.SetAccessRuleProtection(true, false);

                if (!desiredSet.SetEquals(existingSet))
                {
                    foreach (var rule in existingManaged)
                        security.RemoveAccessRuleSpecific(rule);
                    security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
                    security.AddAccessRule(new FileSystemAccessRule(adminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
                    security.AddAccessRule(new FileSystemAccessRule(accountIdentity, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                    if (currentMockSid != null)
                        security.AddAccessRule(new FileSystemAccessRule(currentMockSid, FileSystemRights.FullControl, AccessControlType.Allow));
                }

                return true;
            });
        }
        catch (Exception ex)
        {
            var failure = TryRollbackReadOnlyAfterAclFailure(shortcutPath, attrs, readOnlyAppliedBeforeAcl, ex);
            RestorePriorStateAfterOsFailure(shortcutPath, existingState, failure, "apply");
            throw new ShortcutProtectionException(shortcutPath, "apply", failure);
        }
    }

    private static bool HasManagedEveryoneDenyAce(FileSystemSecurity security, SecurityIdentifier everyoneSid)
        => FindManagedEveryoneDenyRule(security, everyoneSid) != null;

    private static FileSystemAccessRule? FindManagedEveryoneDenyRule(FileSystemSecurity security, SecurityIdentifier everyoneSid)
    {
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Deny ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !sid.Equals(everyoneSid))
            {
                continue;
            }

            var normalizedRights = rule.FileSystemRights & ~FileSystemRights.Synchronize;
            if (normalizedRights == ManagedDenyRights)
                return rule;
        }

        return null;
    }

    private void SaveOrDeleteProtectionState(ShortcutProtectionState state)
    {
        if (!state.ManagedDenyAceApplied && !state.ReadOnlySetByRunFence)
        {
            stateStore.Delete(state.ShortcutPath);
            return;
        }

        stateStore.Save(state);
    }

    private ShortcutProtectionState? TryLoadExistingStateForProtect(string shortcutPath)
    {
        try
        {
            return stateStore.Load(shortcutPath);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "load", ex);
        }
    }

    private bool TryReadHasManagedDenyAce(string shortcutPath, SecurityIdentifier everyoneSid)
    {
        try
        {
            return HasManagedEveryoneDenyAce(new FileInfo(shortcutPath).GetAccessControl(), everyoneSid);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "inspect", ex);
        }
    }

    private void PersistProtectionStateForAdd(string shortcutPath, ShortcutProtectionState newState)
    {
        try
        {
            SaveOrDeleteProtectionState(newState);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "persist", ex);
        }
    }

    private void PersistStateAfterReadOnlyFailure(string shortcutPath, ShortcutProtectionState correctedState)
    {
        try
        {
            SaveOrDeleteProtectionState(correctedState);
        }
        catch (Exception ex)
        {
            throw new ShortcutProtectionException(shortcutPath, "persist", ex);
        }
    }

    private ShortcutProtectionState TryApplyReadOnlyAttribute(
        string shortcutPath,
        FileAttributes originalAttributes,
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
            PersistStateAfterReadOnlyFailure(shortcutPath, correctedState);
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
        string shortcutPath,
        ShortcutProtectionState? existingState,
        Exception originalException,
        string operation)
    {
        try
        {
            if (existingState == null)
                stateStore.Delete(shortcutPath);
            else
                stateStore.Save(existingState);
        }
        catch (Exception restoreEx)
        {
            throw new ShortcutProtectionException(
                shortcutPath,
                operation,
                new AggregateException(originalException, restoreEx));
        }
    }

}
