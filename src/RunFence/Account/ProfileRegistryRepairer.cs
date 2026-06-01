using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Account;

/// <summary>Repairs TEMP profile registry keys and deletes the temporary profile directory.</summary>
public class ProfileRegistryRepairer(ILoggingService log) : IProfileRegistryRepairer
{
    public bool Repair(CorruptedProfile profile)
    {
        try
        {
            var deletedKeyName = FindAvailableDeletedKeyName(profile.Sid);
            log.Info($"ProfileRepair: Renaming TEMP key '{profile.Sid}' -> '{deletedKeyName}'");
            RenameProfileListKey(profile.Sid, deletedKeyName);

            var bakKeyName = profile.Sid + ".bak";
            log.Info($"ProfileRepair: Renaming '{bakKeyName}' -> '{profile.Sid}'");
            try
            {
                RenameProfileListKey(bakKeyName, profile.Sid);
            }
            catch
            {
                log.Warn($"ProfileRepair: Step 2 failed, rolling back '{deletedKeyName}' -> '{profile.Sid}'");
                try
                {
                    RenameProfileListKey(deletedKeyName, profile.Sid);
                }
                catch (Exception rollbackEx)
                {
                    log.Error($"ProfileRepair: Rollback failed: {rollbackEx.Message}");
                }

                throw;
            }

            log.Info($"ProfileRepair: Deleting '{deletedKeyName}' and resetting State to 0");
            using (var profileList = Registry.LocalMachine.OpenSubKey(
                       PathConstants.ProfileListRegistryKey, writable: true))
            {
                profileList?.DeleteSubKeyTree(deletedKeyName, throwOnMissingSubKey: false);

                using var restoredKey = profileList?.OpenSubKey(profile.Sid, writable: true);
                restoredKey?.SetValue("State", 0, RegistryValueKind.DWord);
            }

            log.Info($"ProfileRepair: Registry restored for SID={profile.Sid}, " +
                     $"profile='{profile.OriginalPath}' (was '{profile.TempPath}')");

            CleanupTempDirectories(profile.TempPath);
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to repair profile for SID {profile.Sid}: {ex.Message}");
            return false;
        }
    }

    private static string FindAvailableDeletedKeyName(string sid)
    {
        using var profileList = Registry.LocalMachine.OpenSubKey(PathConstants.ProfileListRegistryKey);
        if (profileList == null)
            return sid + ".deleted";

        var subKeyNames = new HashSet<string>(profileList.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
        var baseName = sid + ".deleted";
        if (!subKeyNames.Contains(baseName))
            return baseName;

        for (int i = 1; i <= 999; i++)
        {
            var candidate = $"{baseName}.{i:D3}";
            if (!subKeyNames.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException($"Too many .deleted keys for SID {sid}");
    }

    private static void RenameProfileListKey(string keyName, string newName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"{PathConstants.ProfileListRegistryKey}\{keyName}",
            writable: true);
        if (key == null)
            throw new InvalidOperationException($"ProfileList registry key not found: {keyName}");

        var unicodeName = new ProfileRepairNative.UNICODE_STRING
        {
            Buffer = newName,
            Length = (ushort)(newName.Length * 2),
            MaximumLength = (ushort)(newName.Length * 2 + 2)
        };

        var status = ProfileRepairNative.NtRenameKey(key.Handle.DangerousGetHandle(), ref unicodeName);
        if (status != 0)
            throw new InvalidOperationException(
                $"NtRenameKey failed for ProfileList key '{keyName}' -> '{newName}': NTSTATUS 0x{status:X8}");
    }

    private void CleanupTempDirectories(string tempProfilePath)
    {
        if (!Directory.Exists(tempProfilePath))
            return;

        try
        {
            Directory.Delete(tempProfilePath, recursive: true);
            log.Info($"Deleted temporary profile directory: {tempProfilePath}");
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to delete temporary profile directory '{tempProfilePath}': {ex.Message}");
        }
    }
}
