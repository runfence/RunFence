using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsPermissionPrompterTests
{
    [Fact]
    public void PromptForGrant_FolderApp_ReturnsSaveWithoutGrant()
    {
        var prompter = CreatePrompter();

        var result = prompter.PromptForGrant(
            owner: null!,
            new AppEntry { IsFolder = true, ExePath = @"C:\Folder", AccountSid = "S-1-5-21-1-2-3-1001" });

        Assert.Equal(AppEntryPermissionPromptResult.SaveWithoutGrant, result.Result);
        Assert.Null(result.GrantRequest);
    }

    [Fact]
    public void PromptForGrant_MissingContainerEntry_ReturnsSaveWithoutGrant()
    {
        var database = new AppDatabase();
        var prompter = CreatePrompter(database: database);

        var result = prompter.PromptForGrant(
            owner: null!,
            new AppEntry
            {
                AppContainerName = "missing",
                ExePath = @"C:\Apps\Test.exe"
            });

        Assert.Equal(AppEntryPermissionPromptResult.SaveWithoutGrant, result.Result);
        Assert.Null(result.GrantRequest);
    }

    [Fact]
    public void TryApplyGrant_AclAppliedForRegularAccount_PinsFolder()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.EnsureAccess(
                "S-1-5-21-1-2-3-1001",
                @"C:\Apps",
                FileSystemRights.ReadAndExecute,
                null))
            .Returns(new GrantApplyResult(GrantApplied: true, TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var prompter = CreatePrompter(pathGrantService: pathGrantService.Object, quickAccessPinService: quickAccessPinService.Object);

        var warning = prompter.TryApplyGrant(new AppEntryPermissionGrantRequest(
            "S-1-5-21-1-2-3-1001",
            @"C:\Apps",
            FileSystemRights.ReadAndExecute,
            PinFolderAfterGrant: true));

        Assert.Null(warning);
        quickAccessPinService.Verify(s => s.PinFolders("S-1-5-21-1-2-3-1001", It.Is<IReadOnlyList<string>>(paths => paths.Single() == @"C:\Apps")), Times.Once);
    }

    [Fact]
    public void TryApplyGrant_SaveFailure_ReturnsWarning()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.EnsureAccess(
                "S-1-5-21-1-2-3-1001",
                @"C:\Apps",
                FileSystemRights.ReadAndExecute,
                null))
            .Throws(new GrantOperationException(
                GrantApplyFailureStep.GrantIntentSave,
                @"C:\Apps",
                null,
                new InvalidOperationException("save failed")));
        var prompter = CreatePrompter(pathGrantService: pathGrantService.Object);

        var warning = prompter.TryApplyGrant(new AppEntryPermissionGrantRequest(
            "S-1-5-21-1-2-3-1001",
            @"C:\Apps",
            FileSystemRights.ReadAndExecute,
            PinFolderAfterGrant: false));

        Assert.Equal("RunFence could not save the permission grant for 'C:\\Apps': save failed", warning);
    }

    private static AppEntryPermissionPrompter CreatePrompter(
        IAclPermissionService? aclPermissionService = null,
        IPathGrantService? pathGrantService = null,
        AppDatabase? database = null,
        IQuickAccessPinService? quickAccessPinService = null)
        => new(
            Mock.Of<ILoggingService>(),
            aclPermissionService ?? Mock.Of<IAclPermissionService>(),
            pathGrantService ?? Mock.Of<IPathGrantService>(),
            new LambdaDatabaseProvider(() => database ?? new AppDatabase()),
            quickAccessPinService ?? Mock.Of<IQuickAccessPinService>());
}
