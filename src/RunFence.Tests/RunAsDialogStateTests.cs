using System.Security.AccessControl;
using System.Windows.Forms;
using Moq;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsDialogStateTests
{
    private const string FilePath = @"C:\Apps\tool.exe";
    private const string CredentialSid = "S-1-5-21-100-200-300-1001";
    private const string ContainerSid = "S-1-15-2-100-200-300-400";

    [Fact]
    public void ResolveSelectionState_WhenCredentialIsCurrentAccount_BypassesPermissionPrompt()
    {
        var aclPermission = new Mock<IAclPermissionService>(MockBehavior.Strict);
        var permissionPrompter = new Mock<IRunAsAncestorPermissionPrompter>(MockBehavior.Strict);
        var state = CreateState(
            aclPermission.Object,
            permissionPrompter.Object,
            sidsNeedingPermission: [CredentialSid]);
        var credential = new CredentialEntry { Sid = CredentialSid };

        var resolved = state.ResolveSelectionState(
            new CredentialRunAsOption(
                credential,
                CredentialSid,
                "Current user",
                IsCurrentAccount: true,
                IsSelectable: true,
                PrivilegeLevel.Isolated,
                ExistingAppForSelection: null,
                SuggestsBasicPrivilegeLevel: false),
            dialogOwner: null,
            currentExistingApp: null,
            selectedPrivilegeLevel: PrivilegeLevel.Isolated,
            updateShortcutChecked: false,
            out _,
            out _,
            out _);

        Assert.True(resolved);
        Assert.Same(credential, state.SelectedCredential);
        Assert.Null(state.PermissionGrant);
    }

    [Fact]
    public void ResolveSelectionState_WhenPermissionGrantAccepted_StoresGrant()
    {
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission
            .Setup(service => service.GetGrantableAncestors(FilePath))
            .Returns([@"C:\Apps", @"C:\"]);
        var expectedGrant = new AncestorPermissionResult(@"C:\Apps", FileSystemRights.ReadAndExecute);
        var permissionPrompter = new Mock<IRunAsAncestorPermissionPrompter>();
        Form? capturedOwner = null;
        IReadOnlyList<string>? capturedAncestors = null;
        permissionPrompter
            .Setup(service => service.Prompt(It.IsAny<Form?>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns<Form?, IReadOnlyList<string>>((owner, ancestors) =>
            {
                capturedOwner = owner;
                capturedAncestors = ancestors;
                return expectedGrant;
            });
        var state = CreateState(
            aclPermission.Object,
            permissionPrompter.Object,
            sidsNeedingPermission: [ContainerSid]);
        var container = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };

        var resolved = state.ResolveSelectionState(
            new AppContainerRunAsOption(
                container,
                ContainerSid,
                container.Name,
                container.DisplayName,
                IsSelectable: true,
                PrivilegeLevel.LowIntegrity,
                ExistingAppForSelection: null,
                SuggestsBasicPrivilegeLevel: false),
            dialogOwner: null,
            currentExistingApp: null,
            selectedPrivilegeLevel: PrivilegeLevel.LowIntegrity,
            updateShortcutChecked: false,
            out _,
            out _,
            out _);

        Assert.True(resolved);
        Assert.Same(expectedGrant, state.PermissionGrant);
        Assert.Null(capturedOwner);
        Assert.NotNull(capturedAncestors);
        Assert.Equal([@"C:\Apps", @"C:\"], capturedAncestors);
    }

    [Fact]
    public void ResolveSelectionState_WhenPermissionGrantCanceled_ClearsSelectionAndReturnsFalse()
    {
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission
            .Setup(service => service.GetGrantableAncestors(FilePath))
            .Returns([@"C:\Apps"]);
        var permissionPrompter = new Mock<IRunAsAncestorPermissionPrompter>();
        permissionPrompter
            .Setup(service => service.Prompt(It.IsAny<Form?>(), It.IsAny<IReadOnlyList<string>>()))
            .Throws(new OperationCanceledException());
        var state = CreateState(
            aclPermission.Object,
            permissionPrompter.Object,
            sidsNeedingPermission: [ContainerSid]);
        var container = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };

        var resolved = state.ResolveSelectionState(
            new AppContainerRunAsOption(
                container,
                ContainerSid,
                container.Name,
                container.DisplayName,
                IsSelectable: true,
                PrivilegeLevel.LowIntegrity,
                ExistingAppForSelection: null,
                SuggestsBasicPrivilegeLevel: false),
            dialogOwner: null,
            currentExistingApp: null,
            selectedPrivilegeLevel: PrivilegeLevel.LowIntegrity,
            updateShortcutChecked: false,
            out _,
            out _,
            out _);

        Assert.False(resolved);
        Assert.Null(state.SelectedCredential);
        Assert.Null(state.SelectedContainer);
        Assert.Null(state.PermissionGrant);
    }

    private static RunAsDialogState CreateState(
        IAclPermissionService aclPermission,
        IRunAsAncestorPermissionPrompter permissionPrompter,
        HashSet<string>? sidsNeedingPermission = null)
        => new(
            FilePath,
            sidsNeedingPermission,
            aclPermission,
            permissionPrompter);
}
