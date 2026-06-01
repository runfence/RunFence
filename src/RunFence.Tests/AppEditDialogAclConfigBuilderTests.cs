using Moq;
using RunFence.Acl;
using RunFence.Acl.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppEditDialogAclConfigBuilderTests
{
    [Fact]
    public void Build_FolderApp_NormalizesSelectedTargetToFolder()
    {
        var builder = CreateBuilder();
        var snapshot = CreateSnapshot(
            new AclConfigSectionSnapshot(
                RestrictAcl: true,
                AclMode: AclMode.Deny,
                SelectedAclTarget: AclTarget.File,
                FolderAclDepth: 2,
                DeniedRights: DeniedRights.Execute,
                AllowedEntries: []));

        var result = builder.Build(snapshot, @"C:\Apps\FolderApp", isFolder: true);

        Assert.NotNull(result.Result);
        Assert.Equal(AclTarget.Folder, result.Result!.Value.AclTarget);
        Assert.Equal(2, result.Result!.Value.Depth);
    }

    [Fact]
    public void Build_FileTarget_NormalizesFolderDepthToZero()
    {
        var builder = CreateBuilder();
        var snapshot = CreateSnapshot(
            new AclConfigSectionSnapshot(
                RestrictAcl: true,
                AclMode: AclMode.Deny,
                SelectedAclTarget: AclTarget.File,
                FolderAclDepth: 3,
                DeniedRights: DeniedRights.Execute,
                AllowedEntries: []));

        var result = builder.Build(snapshot, @"C:\Apps\Tool.exe", isFolder: false);

        Assert.NotNull(result.Result);
        Assert.Equal(AclTarget.File, result.Result!.Value.AclTarget);
        Assert.Equal(0, result.Result!.Value.Depth);
    }

    [Fact]
    public void Build_UrlPath_UsesValidationStateToDisableRestriction()
    {
        var builder = CreateBuilder();
        var snapshot = CreateSnapshot(
            new AclConfigSectionSnapshot(
                RestrictAcl: true,
                AclMode: AclMode.Allow,
                SelectedAclTarget: AclTarget.Folder,
                FolderAclDepth: 1,
                DeniedRights: DeniedRights.ExecuteWrite,
                AllowedEntries:
                [
                    new AllowAclEntry
                    {
                        Sid = "S-1-5-21-1",
                        AllowExecute = true,
                        AllowWrite = true
                    }
                ]));

        var result = builder.Build(snapshot, "steam://run/123", isFolder: false);

        Assert.NotNull(result.Result);
        Assert.False(result.Result!.Value.RestrictAcl);
        Assert.Null(result.Result!.Value.AllowedEntries);
    }

    [Fact]
    public void Build_BlockedResolvedTarget_ReturnsValidationErrorForResolvedTargetPath()
    {
        var builder = CreateBuilder(@"C:\Apps");
        var snapshot = CreateSnapshot(
            new AclConfigSectionSnapshot(
                RestrictAcl: true,
                AclMode: AclMode.Deny,
                SelectedAclTarget: AclTarget.Folder,
                FolderAclDepth: 1,
                DeniedRights: DeniedRights.Execute,
                AllowedEntries: []));

        var result = builder.Build(snapshot, @"C:\Apps\Child\Tool.exe", isFolder: false);

        Assert.Null(result.Result);
        Assert.Equal(@"Cannot restrict access on: C:\Apps", result.ValidationError);
    }

    private static AppEditDialogAclConfigBuilder CreateBuilder(params string[] blockedPaths)
    {
        var blockedSet = new HashSet<string>(
            blockedPaths.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var aclService = new Mock<IAclService>();
        aclService.Setup(service => service.ResolveAclTargetPath(It.IsAny<AppEntry>()))
            .Returns<AppEntry>(ResolveTargetPath);
        aclService.Setup(service => service.IsBlockedPath(It.IsAny<string>()))
            .Returns<string>(path => blockedSet.Contains(Path.GetFullPath(path)));

        return new AppEditDialogAclConfigBuilder(
            new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>()));
    }

    private static AppEditDialogInputSnapshot CreateSnapshot(AclConfigSectionSnapshot aclConfig)
        => new(
            NameText: "App",
            FilePathText: @"C:\Apps\Tool.exe",
            IsFolder: false,
            SelectedAccountSid: "S-1-5-21-1",
            SelectedAppContainerName: null,
            ManageShortcuts: false,
            SelectedPrivilegeLevel: null,
            PersistedPrivilegeLevel: null,
            OverrideIpcCallers: false,
            DefaultArgsText: string.Empty,
            AllowPassArgs: false,
            WorkingDirText: string.Empty,
            AllowPassWorkDir: false,
            ExistingApps: [],
            ExistingApp: null,
            PreGeneratedId: null,
            ArgumentsTemplateText: null,
            AppPathPrefixes: null,
            DuplicateEnvironmentVariableName: null,
            EnvironmentVariables: null,
            IpcCallers: [],
            AclConfig: aclConfig,
            HandlerMappings: null,
            IsUrlScheme: false,
            AclTarget: aclConfig.SelectedAclTarget,
            AclMode: aclConfig.AclMode,
            RestrictAppEntryAcl: aclConfig.RestrictAcl,
            ReplacePrefixes: false);

    private static string ResolveTargetPath(AppEntry app)
    {
        if (app.AclTarget == AclTarget.File)
            return Path.GetFullPath(app.ExePath);

        var folder = app.IsFolder
            ? Path.GetFullPath(app.ExePath)
            : Path.GetDirectoryName(Path.GetFullPath(app.ExePath))!;

        for (var i = 0; i < app.FolderAclDepth; i++)
        {
            var parent = Path.GetDirectoryName(folder);
            if (parent == null)
                break;

            folder = parent;
        }

        return folder;
    }
}
