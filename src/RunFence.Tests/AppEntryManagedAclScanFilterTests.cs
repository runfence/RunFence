using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppEntryManagedAclScanFilterTests
{
    private const string ManagedSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ManualSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private static readonly string SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
    private static readonly string AdminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
    private static readonly AppEntryAclTargetResolver AclTargetResolver = new();
    private static readonly AppEntryAllowAclRuleProvider AllowAclRuleProvider = new(AclTargetResolver);

    public static TheoryData<string, bool> ManagedTargetCases =>
        new()
        {
            { "file", false },
            { "exe-parent-folder", false },
            { "folder-app", false },
            { "non-zero-folder-depth", false },
            { "clamped-folder-depth", false },
            { "file", true },
            { "exe-parent-folder", true },
            { "folder-app", true },
            { "non-zero-folder-depth", true },
            { "clamped-folder-depth", true }
        };

    [Theory]
    [MemberData(nameof(ManagedTargetCases))]
    public void Create_ManagedRuleParityAcrossTargetResolutionCases_FiltersOnlyManagedRule(string caseName, bool isDeny)
    {
        var app = CreateApp(caseName, isDeny);
        var targetPath = AclTargetResolver.ResolveTargetPath(app);
        var isDirectory = app.AclTarget == AclTarget.Folder;
        var denyModeService = new Mock<IAclDenyModeService>(MockBehavior.Strict);
        denyModeService
            .Setup(service => service.GetDeniedRightsPerSid(
                targetPath,
                It.Is<IReadOnlyList<AppEntry>>(apps => apps.Count == 1 && ReferenceEquals(apps[0], app)),
                isDirectory))
            .Returns(new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase)
            {
                [ManagedSid] = DeniedRights.Execute
            });

        var filter = new AppEntryManagedAclScanFilter(AllowAclRuleProvider, denyModeService.Object).Create(
            targetPath,
            isDirectory,
            [app]);

        var managedRule = CreateRule(
            ManagedSid,
            isDirectory,
            isDeny ? AccessControlType.Deny : AccessControlType.Allow,
            isDeny
                ? AclRightsHelper.MapDeniedRights(DeniedRights.Execute)
                : FileSystemRights.Read | FileSystemRights.Synchronize);
        var manualRule = CreateRule(
            ManualSid,
            isDirectory,
            isDeny ? AccessControlType.Deny : AccessControlType.Allow,
            isDeny
                ? AclRightsHelper.MapDeniedRights(DeniedRights.Execute)
                : FileSystemRights.Read | FileSystemRights.Synchronize);

        Assert.True(filter(managedRule));
        Assert.False(filter(manualRule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_AllowModeParity_IgnoresRulesOwnedByAllowModeApplicationAndRevert(bool isDirectory)
    {
        var denyModeService = CreateDenyModeServiceReturningNoRules();
        var app = CreateAllowAppForTarget(isDirectory);
        var targetPath = AclTargetResolver.ResolveTargetPath(app);
        var filter = new AppEntryManagedAclScanFilter(AllowAclRuleProvider, denyModeService.Object).Create(
            targetPath,
            isDirectory,
            [app]);
        var ruleSet = AllowAclRuleProvider.BuildAllowModeRuleSet(app, isDirectory);

        foreach (var rule in ruleSet.Rules)
            Assert.True(filter(rule), $"Expected managed allow rule for {rule.IdentityReference.Value} to be filtered.");

        var unmanagedAllowedSidRule = CreateRule(
            ManagedSid,
            isDirectory,
            AccessControlType.Allow,
            FileSystemRights.ChangePermissions);
        Assert.False(filter(unmanagedAllowedSidRule));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_AllowModeAppWithoutAllowedEntries_DoesNotIgnoreBaselineAllowRules(bool isDirectory)
    {
        var denyModeService = CreateDenyModeServiceReturningNoRules();
        var app = CreateAllowAppForTarget(isDirectory);
        app.AllowedAclEntries = [];
        var targetPath = AclTargetResolver.ResolveTargetPath(app);
        var filter = new AppEntryManagedAclScanFilter(AllowAclRuleProvider, denyModeService.Object).Create(
            targetPath,
            isDirectory,
            [app]);

        var systemRule = CreateRule(SystemSid, isDirectory, AccessControlType.Allow, FileSystemRights.FullControl);
        var adminsRule = CreateRule(
            AdminsSid,
            isDirectory,
            AccessControlType.Allow,
            FileSystemRights.ChangePermissions | FileSystemRights.ReadPermissions |
            FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes);

        Assert.False(filter(systemRule));
        Assert.False(filter(adminsRule));
    }

    private static AppEntry CreateApp(string caseName, bool isDeny)
    {
        var app = new AppEntry
        {
            Id = caseName,
            Name = caseName,
            RestrictAcl = true,
            AclMode = isDeny ? AclMode.Deny : AclMode.Allow,
            AclTarget = caseName == "file" ? AclTarget.File : AclTarget.Folder,
            AllowedAclEntries = isDeny ? null : [new AllowAclEntry { Sid = ManagedSid }],
            DeniedRights = DeniedRights.Execute
        };

        switch (caseName)
        {
            case "file":
                app.ExePath = @"C:\Apps\Tool.exe";
                break;
            case "exe-parent-folder":
                app.ExePath = @"C:\Apps\Suite\Tool.exe";
                break;
            case "folder-app":
                app.ExePath = @"C:\Apps\FolderApp";
                app.IsFolder = true;
                break;
            case "non-zero-folder-depth":
                app.ExePath = @"C:\Apps\Suite\Nested\Tool.exe";
                app.FolderAclDepth = 1;
                break;
            case "clamped-folder-depth":
                app.ExePath = @"C:\A\B\C\D\E\F\Tool.exe";
                app.FolderAclDepth = PathConstants.MaxFolderAclDepth + 5;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
        }

        return app;
    }

    private static AppEntry CreateAllowAppForTarget(bool isDirectory) =>
        new()
        {
            Id = isDirectory ? "dir-app" : "file-app",
            Name = isDirectory ? "dir-app" : "file-app",
            RestrictAcl = true,
            AclMode = AclMode.Allow,
            AclTarget = isDirectory ? AclTarget.Folder : AclTarget.File,
            ExePath = isDirectory ? @"C:\Apps\Suite\Tool.exe" : @"C:\Apps\Suite\Tool.exe",
            AllowedAclEntries =
            [
                new AllowAclEntry
                {
                    Sid = ManagedSid,
                    AllowExecute = true,
                    AllowWrite = true
                }
            ]
        };

    private static Mock<IAclDenyModeService> CreateDenyModeServiceReturningNoRules()
    {
        var denyModeService = new Mock<IAclDenyModeService>(MockBehavior.Strict);
        denyModeService
            .Setup(service => service.GetDeniedRightsPerSid(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AppEntry>>(),
                It.IsAny<bool>()))
            .Returns(new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase));
        return denyModeService;
    }

    private static FileSystemAccessRule CreateRule(
        string sid,
        bool isDirectory,
        AccessControlType type,
        FileSystemRights rights)
        => new(
            new SecurityIdentifier(sid),
            rights,
            AclHelper.InheritanceFlagsFor(isDirectory),
            PropagationFlags.None,
            type);
}
