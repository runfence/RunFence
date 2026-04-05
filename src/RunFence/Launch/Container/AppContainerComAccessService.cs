using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch.Container;

/// <summary>
/// Manages COM activation and access permissions for AppContainer SIDs.
/// Modifies HKCR\AppID\{clsid} LaunchPermission and AccessPermission registry values.
/// </summary>
public class AppContainerComAccessService(ILoggingService log)
{
    // COM rights: Execute(1) | ExecuteLocal(2) | ActivateLocal(8) = 11
    private const int ComRightsLocal = 11;

    public void GrantComAccess(string containerSid, string clsid)
        => ModifyComPermissions(containerSid, clsid, grant: true);

    public void RevokeComAccess(string containerSid, string clsid)
        => ModifyComPermissions(containerSid, clsid, grant: false);

    private void ModifyComPermissions(string containerSid, string clsid, bool grant)
    {
        try
        {
            var sid = new SecurityIdentifier(containerSid);
            // HKCR\AppID\{clsid} — machine-wide COM activation/access permissions.
            // Registry.ClassesRoot maps to HKLM\SOFTWARE\Classes in elevated processes.
            using var appIdKey = Registry.ClassesRoot
                                     .OpenSubKey($@"AppID\{clsid}", writable: true)
                                 ?? Registry.ClassesRoot.CreateSubKey($@"AppID\{clsid}");
            if (appIdKey == null)
            {
                log.Warn($"Could not open/create HKCR\\AppID\\{clsid}");
                return;
            }

            foreach (var valueName in new[] { "LaunchPermission", "AccessPermission" })
                SetComPermissionEntry(appIdKey, valueName, sid, grant);
        }
        catch (Exception ex)
        {
            log.Warn($"{(grant ? "Grant" : "Revoke")}ComAccess for CLSID '{clsid}' failed: {ex.Message}");
        }
    }

    private static void SetComPermissionEntry(
        RegistryKey key, string valueName, SecurityIdentifier sid, bool grant)
    {
        RawSecurityDescriptor sd;
        if (key.GetValue(valueName) is byte[] { Length: > 0 } existingBytes)
        {
            sd = new RawSecurityDescriptor(existingBytes, 0);
        }
        else
        {
            // No existing descriptor: start with an empty DACL. The caller's ACE for the container
            // SID will be appended below. We do NOT pre-populate Everyone — that would grant all
            // processes local COM execution rights on this CLSID even after revocation.
            var emptyDacl = new RawAcl(GenericAcl.AclRevision, 1);
            sd = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative,
                null, null, null, emptyDacl);
        }

        var dacl = sd.DiscretionaryAcl ?? new RawAcl(GenericAcl.AclRevision, 1);
        var newDacl = new RawAcl(GenericAcl.AclRevision, dacl.Count + 1);
        // Remove any existing ACE for this SID (de-duplicate before re-adding)
        foreach (GenericAce ace in dacl)
        {
            if (ace is CommonAce ca && ca.SecurityIdentifier == sid)
                continue;
            newDacl.InsertAce(newDacl.Count, ace);
        }

        if (grant)
        {
            newDacl.InsertAce(newDacl.Count, new CommonAce(
                AceFlags.None, AceQualifier.AccessAllowed, ComRightsLocal, sid, false, null));
        }

        sd.DiscretionaryAcl = newDacl;
        var newBytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(newBytes, 0);
        key.SetValue(valueName, newBytes, RegistryValueKind.Binary);
    }
}