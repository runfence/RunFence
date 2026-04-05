using System.ComponentModel;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Account;

/// <summary>
/// Detects and repairs Windows user profile corruption caused by the User Profile Service
/// creating temporary profiles (TEMP.MACHINENAME.NNN) when it fails to load the original
/// profile during <c>CreateProcessWithLogonW</c> with <c>LOGON_WITH_PROFILE</c>.
/// When this happens, the original registry key is moved to a <c>.bak</c> suffix and a new
/// key pointing to the temporary profile is created.
/// </summary>
public class ProfileRepairHelper(IProfileRepairPrompt prompt, ILoggingService log) : IProfileRepairHelper
{
    private record CorruptedProfile(string Sid, string OriginalPath, string TempPath);

    /// <summary>
    /// Wraps a launch action with automatic profile corruption detection and repair
    /// for the specified account SID.
    /// On launch failure: checks if the profile for <paramref name="accountSid"/> was corrupted,
    /// prompts the user to repair, and optionally retries the launch.
    /// If no corruption is detected or <paramref name="accountSid"/> is null,
    /// the original exception is rethrown for normal handling.
    /// </summary>
    public void ExecuteWithProfileRepair(Action launchAction, string? accountSid)
    {
        try
        {
            launchAction();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            throw;
        }
        catch
        {
            if (accountSid == null)
                throw;

            var corrupted = DetectCorruptedProfile(accountSid);
            if (corrupted == null)
                throw;

            var accountName = TryResolveAccountName(corrupted.Sid) ?? corrupted.Sid;
            log.Warn($"Profile corruption detected for '{accountName}' (SID={corrupted.Sid}): " +
                     $"original='{corrupted.OriginalPath}', temp='{corrupted.TempPath}'");

            if (!prompt.ConfirmRepair(accountName))
            {
                log.Warn($"User declined profile repair for: {accountName}");
                throw;
            }

            if (!RepairProfile(corrupted))
            {
                prompt.NotifyRepairFailed();
                throw;
            }

            if (prompt.ConfirmRetry())
            {
                launchAction();
            }

            // User declined retry — swallow the original exception since the repair succeeded
        }
    }

    /// <summary>
    /// Checks whether the profile for the specified SID has been corrupted by Windows
    /// (registry key moved to <c>.bak</c>, active key pointing to a TEMP profile path).
    /// </summary>
    private CorruptedProfile? DetectCorruptedProfile(string sid)
    {
        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(Constants.ProfileListRegistryKey);
            if (profileList == null)
                return null;

            var bakKeyName = sid + ".bak";
            using var bakKey = profileList.OpenSubKey(bakKeyName);
            if (bakKey == null)
                return null;

            using var activeKey = profileList.OpenSubKey(sid);

            var originalPath = bakKey.GetValue("ProfileImagePath") as string;
            var tempPath = activeKey?.GetValue("ProfileImagePath") as string;

            if (string.IsNullOrEmpty(originalPath))
                return null;

            // Verify the active key points to a TEMP profile (not just any profile)
            if (string.IsNullOrEmpty(tempPath) || !IsTempProfilePath(tempPath))
                return null;

            // Verify the original path looks like a real profile (not itself a TEMP)
            if (IsTempProfilePath(originalPath))
                return null;

            // Verify the original profile directory still exists on disk
            if (!Directory.Exists(originalPath))
                return null;

            return new CorruptedProfile(sid, originalPath, tempPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Repairs a corrupted profile by renaming registry keys (preserving ACLs) and cleaning up
    /// temporary profile directories.
    /// </summary>
    private bool RepairProfile(CorruptedProfile profile)
    {
        try
        {
            var profileListPath = @"HKEY_LOCAL_MACHINE\" + Constants.ProfileListRegistryKey;

            // Step 1: Rename the TEMP key to .deleted (preserves ACLs)
            var deletedKeyName = FindAvailableDeletedKeyName(profile.Sid);
            log.Info($"ProfileRepair: Renaming TEMP key '{profile.Sid}' → '{deletedKeyName}'");
            RenameRegistryKey(profileListPath + @"\" + profile.Sid, deletedKeyName);

            // Step 2: Rename .bak key back to the original SID name (preserves ACLs)
            var bakKeyName = profile.Sid + ".bak";
            log.Info($"ProfileRepair: Renaming '{bakKeyName}' → '{profile.Sid}'");
            try
            {
                RenameRegistryKey(profileListPath + @"\" + bakKeyName, profile.Sid);
            }
            catch
            {
                // Rollback step 1: rename .deleted back to the SID key
                log.Warn($"ProfileRepair: Step 2 failed, rolling back '{deletedKeyName}' → '{profile.Sid}'");
                try
                {
                    RenameRegistryKey(profileListPath + @"\" + deletedKeyName, profile.Sid);
                }
                catch (Exception rollbackEx)
                {
                    log.Error($"ProfileRepair: Rollback failed: {rollbackEx.Message}");
                }

                throw;
            }

            // Step 3: Delete the .deleted key and reset State on the restored key
            log.Info($"ProfileRepair: Deleting '{deletedKeyName}' and resetting State to 0");
            using (var profileList = Registry.LocalMachine.OpenSubKey(
                       Constants.ProfileListRegistryKey, writable: true))
            {
                profileList?.DeleteSubKeyTree(deletedKeyName, throwOnMissingSubKey: false);

                using var restoredKey = profileList?.OpenSubKey(profile.Sid, writable: true);
                restoredKey?.SetValue("State", 0, RegistryValueKind.DWord);
            }

            log.Info($"ProfileRepair: Registry restored for SID={profile.Sid}, " +
                     $"profile='{profile.OriginalPath}' (was '{profile.TempPath}')");

            // Step 4: Clean up TEMP profile directories
            CleanupTempDirectories(profile.TempPath);

            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to repair profile for SID {profile.Sid}: {ex.Message}");
            return false;
        }
    }

    private string FindAvailableDeletedKeyName(string sid)
    {
        using var profileList = Registry.LocalMachine.OpenSubKey(Constants.ProfileListRegistryKey);
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

    /// <summary>
    /// Renames a registry key using <c>NtRenameKey</c>, preserving ACLs and all key data.
    /// </summary>
    private void RenameRegistryKey(string fullKeyPath, string newName)
    {
        using var key = OpenRegistryKeyByPath(fullKeyPath);
        if (key == null)
            throw new InvalidOperationException($"Registry key not found: {fullKeyPath}");

        var unicodeName = new ProfileRepairNative.UNICODE_STRING
        {
            Buffer = newName,
            Length = (ushort)(newName.Length * 2),
            MaximumLength = (ushort)(newName.Length * 2 + 2)
        };

        var status = ProfileRepairNative.NtRenameKey(key.Handle.DangerousGetHandle(), ref unicodeName);
        if (status != 0)
            throw new InvalidOperationException(
                $"NtRenameKey failed for '{fullKeyPath}' → '{newName}': NTSTATUS 0x{status:X8}");
    }

    private RegistryKey? OpenRegistryKeyByPath(string fullPath)
    {
        // Parse "HKEY_LOCAL_MACHINE\path\to\key"
        var separatorIndex = fullPath.IndexOf('\\');
        if (separatorIndex < 0)
            return null;

        var rootName = fullPath[..separatorIndex];
        var subPath = fullPath[(separatorIndex + 1)..];

        var root = rootName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_USERS" => Registry.Users,
            _ => null
        };

        return root?.OpenSubKey(subPath, writable: true);
    }

    /// <summary>
    /// Checks if a profile path matches the Windows temporary profile naming pattern:
    /// TEMP, TEMP.MACHINENAME, or TEMP.MACHINENAME.NNN (where NNN is a 3-digit number).
    /// </summary>
    private bool IsTempProfilePath(string profilePath)
    {
        var folderName = Path.GetFileName(profilePath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName))
            return false;

        var machineName = Environment.MachineName;

        // Exact "TEMP"
        if (string.Equals(folderName, "TEMP", StringComparison.OrdinalIgnoreCase))
            return true;

        // "TEMP.MACHINENAME"
        if (string.Equals(folderName, $"TEMP.{machineName}", StringComparison.OrdinalIgnoreCase))
            return true;

        // "TEMP.MACHINENAME.NNN" where NNN is digits
        var prefix = $"TEMP.{machineName}.";
        if (folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = folderName[prefix.Length..];
            return suffix.Length > 0 && suffix.All(char.IsDigit);
        }

        return false;
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

    private string? TryResolveAccountName(string sid)
    {
        try
        {
            var secId = new SecurityIdentifier(sid);
            var ntAccount = (NTAccount)secId.Translate(
                typeof(NTAccount));
            return ntAccount.Value;
        }
        catch
        {
            return null;
        }
    }
}