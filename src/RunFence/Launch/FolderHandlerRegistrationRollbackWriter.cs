using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationRollbackWriter
{
    public void RollbackRegistrationChanges(IRegistryKey usersRoot, string accountSid, FolderHandlerRegistrationChangeSet changeSet)
    {
        var createdKeyPaths = changeSet.CreatedKeyPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length)
            .ToList();
        var createdKeySet = new HashSet<string>(createdKeyPaths, StringComparer.OrdinalIgnoreCase);
        var createdValueSet = changeSet.ValueSnapshots
            .Where(snapshot => !snapshot.Existed)
            .Select(snapshot => FolderHandlerRegistryPathMapper.BuildValueIdentifier(snapshot.SubKeyPath, snapshot.ValueName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in changeSet.ValueSnapshots.Reverse())
            RestoreValue(usersRoot, accountSid, snapshot);

        foreach (var createdKeyPath in createdKeyPaths)
        {
            if (CanDeleteCreatedKey(usersRoot, accountSid, createdKeyPath, createdKeySet, createdValueSet))
                usersRoot.DeleteSubKeyTree(FolderHandlerRegistryPathMapper.BuildFullPath(accountSid, createdKeyPath), throwOnMissingSubKey: false);
        }
    }

    private static void RestoreValue(IRegistryKey usersRoot, string accountSid, FolderHandlerRegistryValueSnapshot snapshot)
    {
        var fullPath = FolderHandlerRegistryPathMapper.BuildFullPath(accountSid, snapshot.SubKeyPath);
        using var key = usersRoot.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        if (!snapshot.Existed)
        {
            key.DeleteValue(FolderHandlerRegistryPathMapper.NormalizeValueName(snapshot.ValueName), throwOnMissingValue: false);
            key.Flush();
            return;
        }

        key.SetValue(
            FolderHandlerRegistryPathMapper.NormalizeValueName(snapshot.ValueName),
            snapshot.PreviousValue ?? string.Empty,
            snapshot.PreviousKind ?? RegistryValueKind.String);
        key.Flush();
    }

    private static bool CanDeleteCreatedKey(
        IRegistryKey usersRoot,
        string accountSid,
        string subKeyPath,
        IReadOnlySet<string> createdKeyPaths,
        IReadOnlySet<string> createdValueIdentifiers)
    {
        using var key = usersRoot.OpenSubKey(FolderHandlerRegistryPathMapper.BuildFullPath(accountSid, subKeyPath));
        if (key == null)
            return false;

        foreach (var valueName in key.GetValueNames())
        {
            if (!createdValueIdentifiers.Contains(FolderHandlerRegistryPathMapper.BuildValueIdentifier(subKeyPath, valueName)))
                return false;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            var childSubKeyPath = $@"{subKeyPath}\{childName}";
            if (!createdKeyPaths.Contains(childSubKeyPath))
                return false;
        }

        return true;
    }
}
