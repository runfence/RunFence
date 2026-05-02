using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AccountPickerStep.Validate"/> and <see cref="AccountPickerStep.Collect"/>.
/// Focused on the BHV-15 guard: <c>Collect</c> does not invoke <c>_setSelection</c>
/// when the selected item has a null SID and is not a "Create new account" item.
/// </summary>
public class AccountPickerStepTests
{
    private readonly Mock<ILocalGroupMembershipService> _groupMembership = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly CredentialFilterHelper _credentialFilterHelper;

    public AccountPickerStepTests()
    {
        _credentialFilterHelper = new CredentialFilterHelper(_sidResolver.Object);
        _groupMembership.Setup(g => g.GetMembersOfGroup(It.IsAny<string>())).Returns([]);
        _localUserProvider.Setup(l => l.GetLocalUserAccounts()).Returns([]);
    }

    private AccountPickerStep CreateStep(Action<string?, bool> setSelection)
    {
        var options = new AccountPickerStepOptions(
            Credentials: [],
            SidNames: new Dictionary<string, string>(),
            GroupSid: "S-1-5-32-544",
            StepTitle: "Pick account",
            InfoText: "Select an account.");

        return new AccountPickerStep(
            setSelection,
            _groupMembership.Object,
            _localUserProvider.Object,
            _sidResolver.Object,
            _profilePathResolver.Object,
            _credentialFilterHelper,
            options);
    }

    private static ListBox FindListBox(AccountPickerStep step)
    {
        return step.Controls.OfType<ListBox>().First();
    }

    [Fact]
    public void Validate_WhenNoItemSelected_ReturnsError()
    {
        // Arrange
        bool setSelectionCalled = false;
        var step = CreateStep((_, _) => setSelectionCalled = true);

        // Act — no selection made
        var error = step.Validate();

        // Assert
        Assert.NotNull(error);
        Assert.False(setSelectionCalled);
    }

    [Fact]
    public void Validate_WhenCredentialDisplayItemWithNullSid_ReturnsError()
    {
        // Arrange: a CredentialDisplayItem whose Credential.Sid is null/empty.
        // This simulates a transient/loading placeholder item that slipped through.
        bool setSelectionCalled = false;
        var step = CreateStep((_, _) => setSelectionCalled = true);
        var listBox = FindListBox(step);

        var emptyCredential = new CredentialEntry { Id = Guid.NewGuid(), Sid = "" };
        var displayItem = new CredentialDisplayItem(emptyCredential, _sidResolver.Object, _profilePathResolver.Object, hasStoredCredential: false);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;

        // Act
        var error = step.Validate();

        // Assert: BHV-15 fix — Validate catches the null-SID case and returns an error
        Assert.NotNull(error);
        Assert.Contains("valid", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(setSelectionCalled);
    }

    [Fact]
    public void Collect_WhenCredentialDisplayItemWithNullSid_DoesNotInvokeSetSelection()
    {
        // Arrange: same scenario as the Validate test — CredentialDisplayItem with empty Sid.
        // Collect() must not call _setSelection (BHV-15 last-resort guard).
        bool setSelectionCalled = false;
        var step = CreateStep((_, _) => setSelectionCalled = true);
        var listBox = FindListBox(step);

        var emptyCredential = new CredentialEntry { Id = Guid.NewGuid(), Sid = "" };
        var displayItem = new CredentialDisplayItem(emptyCredential, _sidResolver.Object, _profilePathResolver.Object, hasStoredCredential: false);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;

        // Act — call Collect directly (wizard normally calls Validate first, but we verify the guard)
        step.Collect();

        // Assert: setSelection must NOT be called
        Assert.False(setSelectionCalled);
    }

    [Fact]
    public void Collect_WhenCreateAccountItemSelected_InvokesSetSelectionWithNullSidAndIsCreateTrue()
    {
        // Arrange
        string? capturedSid = "unset";
        bool capturedIsCreate = false;
        var step = CreateStep((sid, isCreate) =>
        {
            capturedSid = sid;
            capturedIsCreate = isCreate;
        });
        var listBox = FindListBox(step);

        listBox.Items.Add(new CreateAccountItem());
        listBox.SelectedIndex = 0;

        // Act
        step.Collect();

        // Assert: isCreate=true path — setSelection called with null sid
        Assert.Null(capturedSid);
        Assert.True(capturedIsCreate);
    }

    [Fact]
    public void Collect_WhenValidCredentialSelected_InvokesSetSelectionWithSid()
    {
        // Arrange
        const string sid = "S-1-5-21-100-200-300-1001";
        string? capturedSid = null;
        bool capturedIsCreate = true;
        var step = CreateStep((s, c) =>
        {
            capturedSid = s;
            capturedIsCreate = c;
        });
        var listBox = FindListBox(step);

        var credential = new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };
        var displayItem = new CredentialDisplayItem(credential, _sidResolver.Object, _profilePathResolver.Object);
        listBox.Items.Add(displayItem);
        listBox.SelectedIndex = 0;

        // Act
        step.Collect();

        // Assert
        Assert.Equal(sid, capturedSid);
        Assert.False(capturedIsCreate);
    }
}
