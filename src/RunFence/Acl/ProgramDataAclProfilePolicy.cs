using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Core;

namespace RunFence.Acl;

public class ProgramDataAclProfilePolicy
{
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier WorldSid = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier UsersSid = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly FileSystemRights PublicIconReadRights = FileSystemRights.Read;
    private static readonly FileSystemRights AllowedPublicIconReadRights =
        FileSystemRights.Read |
        FileSystemRights.ReadData |
        FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes |
        FileSystemRights.ReadPermissions |
        FileSystemRights.Synchronize;
    private static readonly FileSystemRights SharedExecutableReadRights = FileSystemRights.ReadAndExecute;
    private static readonly FileSystemRights AllowedSharedExecutableReadRights =
        SharedExecutableReadRights |
        FileSystemRights.Synchronize;
    private static readonly FileSystemRights DangerousFileRights =
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership |
        FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.CreateFiles |
        FileSystemRights.CreateDirectories;

    public IReadOnlyList<ExpectedRule> GetExpectedDirectoryRules(ProgramDataDirectoryAclProfile profile)
    {
        var rules = CreateFullControlRules(
            includeCurrentUser: profile == ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);

        if (profile == ProgramDataDirectoryAclProfile.PublicReadTrustedWrite)
        {
            rules.Add(new ExpectedRule(
                WorldSid,
                FileSystemRights.ReadAndExecute,
                FileSystemRights.ReadAndExecute,
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None));
        }
        else if (profile == ProgramDataDirectoryAclProfile.SharedExecutableReadExecute)
        {
            rules.Add(new ExpectedRule(
                UsersSid,
                SharedExecutableReadRights,
                SharedExecutableReadRights,
                AllowedSharedExecutableReadRights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None));
        }

        return rules;
    }

    public IReadOnlyList<ExpectedRule> GetExpectedFileRules(ProgramDataFileAclProfile profile)
    {
        var rules = CreateFullControlRules(
            includeCurrentUser: profile == ProgramDataFileAclProfile.CurrentProcessUserFullControl,
            InheritanceFlags.None);

        if (profile == ProgramDataFileAclProfile.PublicIconRead)
        {
            rules.Add(new ExpectedRule(
                WorldSid,
                PublicIconReadRights,
                FileSystemRights.ReadData,
                AllowedPublicIconReadRights,
                InheritanceFlags.None,
                PropagationFlags.None));
        }
        else if (profile == ProgramDataFileAclProfile.SharedExecutableReadExecute)
        {
            rules.Add(new ExpectedRule(
                UsersSid,
                SharedExecutableReadRights,
                SharedExecutableReadRights,
                AllowedSharedExecutableReadRights,
                InheritanceFlags.None,
                PropagationFlags.None));
        }

        return rules;
    }

    public bool HasDangerousFileRights(FileSystemRights rights)
        => (rights & DangerousFileRights) != 0;

    private List<ExpectedRule> CreateFullControlRules(bool includeCurrentUser, InheritanceFlags inheritanceFlags)
    {
        var rules = new List<ExpectedRule>
        {
            CreateFullControlRule(SystemSid, inheritanceFlags),
            CreateFullControlRule(AdministratorsSid, inheritanceFlags)
        };

        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
        {
            rules.Add(CreateFullControlRule(currentMockSid, inheritanceFlags));
        }

        if (includeCurrentUser)
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            if (currentIdentity.User != null)
            {
                rules.Add(CreateFullControlRule(currentIdentity.User, inheritanceFlags));
            }
        }

        return rules;
    }

    private static ExpectedRule CreateFullControlRule(SecurityIdentifier sid, InheritanceFlags inheritanceFlags)
        => new(
            sid,
            FileSystemRights.FullControl,
            FileSystemRights.FullControl,
            FileSystemRights.FullControl,
            inheritanceFlags,
            PropagationFlags.None);

    public readonly record struct ExpectedRule(
        SecurityIdentifier Principal,
        FileSystemRights BuildRights,
        FileSystemRights RequiredRights,
        FileSystemRights AllowedRights,
        InheritanceFlags InheritanceFlags,
        PropagationFlags PropagationFlags)
    {
        public FileSystemAccessRule CreateAccessRule()
            => new(
                Principal,
                BuildRights,
                InheritanceFlags,
                PropagationFlags,
                AccessControlType.Allow);

        public bool Matches(FileSystemAccessRule rule)
            => rule.AccessControlType == AccessControlType.Allow &&
               rule.IdentityReference is SecurityIdentifier sid &&
               sid.Equals(Principal) &&
               (rule.FileSystemRights & RequiredRights) == RequiredRights &&
               (rule.FileSystemRights & ~AllowedRights) == 0 &&
               rule.InheritanceFlags == InheritanceFlags &&
               rule.PropagationFlags == PropagationFlags;
    }
}
