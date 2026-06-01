using Moq;
using RunFence.Acl;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclConfigValidatorTests
{
    [Fact]
    public void ValidateState_ExactPathConflict_SetsInvalidConflictMessage()
    {
        var context = new ValidatorTestContext();
        var exePath = @"C:\Apps\Tool.exe";
        var existingApps = new List<AppEntry>
        {
            new()
            {
                Id = "other",
                Name = "Other App",
                ExePath = exePath,
                RestrictAcl = true,
                AclMode = AclMode.Allow,
                AclTarget = AclTarget.File
            }
        };

        var state = context.Validator.ValidateState(
            exePath,
            isFolder: false,
            restrictAcl: true,
            isAllowMode: false,
            aclTarget: AclTarget.File,
            depth: 0,
            allowEntryCount: 0,
            existingApps,
            currentAppId: null);

        Assert.False(state.IsValid);
        Assert.Equal("Another app (Other App) already manages ACLs on this path.", state.ConflictMessage);
        Assert.Equal(Path.GetFullPath(exePath), state.TargetPath);
    }

    [Fact]
    public void ValidateState_OverlapWarning_ReturnsWarningWithoutInvalidatingState()
    {
        var context = new ValidatorTestContext();
        var exePath = @"C:\Apps\Parent\Child\Tool.exe";
        var existingApps = new List<AppEntry>
        {
            new()
            {
                Id = "other",
                Name = "Parent App",
                ExePath = @"C:\Apps\Parent\Launcher.exe",
                RestrictAcl = true,
                AclMode = AclMode.Deny,
                AclTarget = AclTarget.Folder,
                FolderAclDepth = 0
            }
        };

        var state = context.Validator.ValidateState(
            exePath,
            isFolder: false,
            restrictAcl: true,
            isAllowMode: true,
            aclTarget: AclTarget.File,
            depth: 0,
            allowEntryCount: 1,
            existingApps,
            currentAppId: null);

        Assert.True(state.IsValid);
        Assert.Null(state.ConflictMessage);
        Assert.Equal(
            "Warning: Parent App's Deny path is a parent of this app's Allow path. The Deny rule will take precedence and make the Allow ineffective.",
            state.OverlapWarning);
        Assert.Equal(Path.GetFullPath(exePath), state.TargetPath);
    }

    [Fact]
    public void ValidateState_NoOverlap_ReturnsNoWarning()
    {
        var context = new ValidatorTestContext();
        var existingApps = new List<AppEntry>
        {
            new()
            {
                Id = "other",
                Name = "Other App",
                ExePath = @"C:\Other\Elsewhere.exe",
                RestrictAcl = true,
                AclMode = AclMode.Deny,
                AclTarget = AclTarget.File
            }
        };

        var state = context.Validator.ValidateState(
            @"C:\Apps\Tool.exe",
            isFolder: false,
            restrictAcl: true,
            isAllowMode: false,
            aclTarget: AclTarget.File,
            depth: 0,
            allowEntryCount: 0,
            existingApps,
            currentAppId: null);

        Assert.True(state.IsValid);
        Assert.Null(state.ConflictMessage);
        Assert.Null(state.OverlapWarning);
    }

    [Fact]
    public void ValidateState_UrlPath_DisablesRestrictionAndPreservesTargetPath()
    {
        var context = new ValidatorTestContext();

        var state = context.Validator.ValidateState(
            "steam://run/123",
            isFolder: false,
            restrictAcl: true,
            isAllowMode: false,
            aclTarget: AclTarget.Folder,
            depth: 2,
            allowEntryCount: 0,
            [],
            currentAppId: null);

        Assert.True(state.IsValid);
        Assert.False(state.RestrictAcl);
        Assert.Equal(AclTarget.Folder, state.SelectedAclTarget);
        Assert.Equal(2, state.FolderAclDepth);
        Assert.Equal("steam://run/123", state.TargetPath);
    }

    [Fact]
    public void ValidateState_BlockedPath_SetsInvalidBlockedMessage()
    {
        var exePath = @"C:\Apps\Tool.exe";
        var blockedTarget = Path.GetFullPath(exePath);
        var context = new ValidatorTestContext(blockedTarget);

        var state = context.Validator.ValidateState(
            exePath,
            isFolder: false,
            restrictAcl: true,
            isAllowMode: false,
            aclTarget: AclTarget.File,
            depth: 0,
            allowEntryCount: 0,
            [],
            currentAppId: null);

        Assert.False(state.IsValid);
        Assert.Equal($"Cannot restrict access on: {blockedTarget}", state.ConflictMessage);
        Assert.Equal(blockedTarget, state.TargetPath);
    }

    [Fact]
    public void ValidateState_AllowModeWithoutEntries_SetsInvalidMessage()
    {
        var context = new ValidatorTestContext();

        var state = context.Validator.ValidateState(
            @"C:\Apps\Tool.exe",
            isFolder: false,
            restrictAcl: true,
            isAllowMode: true,
            aclTarget: AclTarget.File,
            depth: 0,
            allowEntryCount: 0,
            [],
            currentAppId: null);

        Assert.False(state.IsValid);
        Assert.Equal("Allow mode requires at least one entry.", state.ConflictMessage);
    }

    [Fact]
    public void ValidateState_NormalizesTargetAndResolvesFolderDepthTargetPath()
    {
        var context = new ValidatorTestContext();

        var state = context.Validator.ValidateState(
            @"C:\Apps\Child\Tool.exe",
            isFolder: false,
            restrictAcl: true,
            isAllowMode: false,
            aclTarget: AclTarget.Folder,
            depth: 1,
            allowEntryCount: 0,
            [],
            currentAppId: null);

        Assert.True(state.IsValid);
        Assert.Equal(AclTarget.Folder, state.SelectedAclTarget);
        Assert.Equal(1, state.FolderAclDepth);
        Assert.Equal(@"C:\Apps", state.TargetPath);
    }

    private sealed class ValidatorTestContext
    {
        public ValidatorTestContext(params string[] blockedPaths)
        {
            var blockedSet = new HashSet<string>(
                blockedPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            var aclService = new Mock<IAclService>();
            aclService.Setup(service => service.ResolveAclTargetPath(It.IsAny<AppEntry>()))
                .Returns<AppEntry>(ResolveTargetPath);
            aclService.Setup(service => service.IsBlockedPath(It.IsAny<string>()))
                .Returns<string>(path => blockedSet.Contains(Path.GetFullPath(path)));

            Validator = new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>());
        }

        public AclConfigValidator Validator { get; }

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
}
