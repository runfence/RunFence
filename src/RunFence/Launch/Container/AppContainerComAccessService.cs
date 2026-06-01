using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch.Container;

/// <summary>
/// Manages COM activation and access permissions for AppContainer SIDs.
/// Modifies HKCR\AppID\{clsid} LaunchPermission and AccessPermission registry values.
/// </summary>
public class AppContainerComAccessService(
    ILoggingService log,
    IAppContainerComRegistryRoots registryRoots)
    : IAppContainerComAccessService
{
    // COM rights: Execute(1) | ExecuteLocal(2) | ActivateLocal(8) = 11
    private const int ComRightsLocal = 11;
    private readonly IRegistryKey _appIdRoot = registryRoots.AppIdRoot;
    private readonly IRegistryKey _machineRoot = registryRoots.MachineRoot;

    public AppContainerComAccessResult GrantComAccess(string containerSid, string clsid)
        => ModifyComPermissions(containerSid, clsid, grant: true);

    public AppContainerComAccessResult RevokeComAccess(string containerSid, string clsid)
        => ModifyComPermissions(containerSid, clsid, grant: false);

    private AppContainerComAccessResult ModifyComPermissions(string containerSid, string clsid, bool grant)
    {
        var createdValues = new List<string>();
        try
        {
            var sid = new SecurityIdentifier(containerSid);
            using IRegistryKey? existingAppIdKey = _appIdRoot.OpenSubKey($@"AppID\{clsid}", writable: true);
            if (!grant && existingAppIdKey == null)
                return AppContainerComAccessResult.Success();

            using IRegistryKey appIdKey = existingAppIdKey
                ?? _appIdRoot.CreateSubKey($@"AppID\{clsid}")
                ?? throw new InvalidOperationException($"Unable to open AppID registry key for '{clsid}'.");

            foreach (var valueName in new[] { "LaunchPermission", "AccessPermission" })
            {
                var writeResult = SetComPermissionEntry(appIdKey, valueName, sid, grant);
                if (!writeResult.Succeeded)
                {
                    RollBackCreatedValues(appIdKey, createdValues, clsid);
                    return AppContainerComAccessResult.Failure(writeResult.ErrorMessage!);
                }

                if (writeResult.ValueCreated)
                    createdValues.Add(valueName);
            }

            return AppContainerComAccessResult.Success();
        }
        catch (Exception ex)
        {
            return AppContainerComAccessResult.Failure(
                $"{(grant ? "Grant" : "Revoke")}ComAccess for CLSID '{clsid}' failed: {ex.Message}");
        }
    }

    private PermissionWriteResult SetComPermissionEntry(
        IRegistryKey key,
        string valueName,
        SecurityIdentifier sid,
        bool grant)
    {
        var existingBytes = key.GetValue(valueName) as byte[];
        var hadExistingValue = existingBytes is { Length: > 0 };
        if (!grant && !hadExistingValue)
            return PermissionWriteResult.Success(valueCreated: false);

        var descriptorBytes = hadExistingValue
            ? existingBytes
            : GetMachineDefaultPermission(valueName);
        if (descriptorBytes == null)
        {
            return PermissionWriteResult.Failure(
                $"Machine default COM permission '{valueName}' is missing or empty.");
        }

        RawSecurityDescriptor sd;
        try
        {
            sd = new RawSecurityDescriptor(descriptorBytes, 0);
        }
        catch (Exception ex)
        {
            return PermissionWriteResult.Failure(
                $"Failed to parse COM permission descriptor '{valueName}': {ex.Message}");
        }

        var dacl = sd.DiscretionaryAcl ?? new RawAcl(GenericAcl.AclRevision, 1);
        var newDacl = new RawAcl(GenericAcl.AclRevision, dacl.Count + 1);
        foreach (GenericAce ace in dacl)
        {
            if (ace is CommonAce commonAce && commonAce.SecurityIdentifier == sid)
                continue;

            newDacl.InsertAce(newDacl.Count, ace);
        }

        if (grant)
        {
            newDacl.InsertAce(newDacl.Count, new CommonAce(
                AceFlags.None,
                AceQualifier.AccessAllowed,
                ComRightsLocal,
                sid,
                isCallback: false,
                opaque: null));
        }

        sd.DiscretionaryAcl = newDacl;
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        try
        {
            key.SetValue(valueName, bytes, RegistryValueKind.Binary);
            return PermissionWriteResult.Success(valueCreated: !hadExistingValue);
        }
        catch (Exception ex)
        {
            return PermissionWriteResult.Failure(
                $"Failed to write COM permission '{valueName}': {ex.Message}");
        }
    }

    private byte[]? GetMachineDefaultPermission(string valueName)
    {
        var defaultValueName = valueName switch
        {
            "LaunchPermission" => "DefaultLaunchPermission",
            "AccessPermission" => "DefaultAccessPermission",
            _ => throw new ArgumentOutOfRangeException(nameof(valueName), valueName, null)
        };

        using var oleKey = _machineRoot.OpenSubKey(@"SOFTWARE\Microsoft\Ole");
        return oleKey?.GetValue(defaultValueName) as byte[];
    }

    private void RollBackCreatedValues(IRegistryKey appIdKey, IEnumerable<string> createdValues, string clsid)
    {
        foreach (var valueName in createdValues)
        {
            try
            {
                appIdKey.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to roll back COM permission '{valueName}' for CLSID '{clsid}': {ex.Message}");
            }
        }
    }

    private sealed record PermissionWriteResult(bool Succeeded, bool ValueCreated, string? ErrorMessage)
    {
        public static PermissionWriteResult Success(bool valueCreated) => new(true, valueCreated, null);

        public static PermissionWriteResult Failure(string errorMessage) => new(false, false, errorMessage);
    }
}
