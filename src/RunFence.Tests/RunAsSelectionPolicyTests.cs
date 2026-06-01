using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.RunAs.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsSelectionPolicyTests
{
    [Fact]
    public void ResolveSelection_WhenShortcutAlreadyManaged_PrefersManagedCredential()
    {
        var policy = CreatePolicy();
        IRunAsAccountOption[] options =
        {
            CreateCredentialOption("S-1-5-21-other", "Other"),
            CreateCredentialOption("S-1-5-21-managed", "Managed"),
            CreateContainerOption("rfn_browser", "Browser")
        };
        var shortcutContext = new ShortcutContext(
            OriginalLnkPath: @"C:\Apps\tool.lnk",
            IsAlreadyManaged: true,
            ManagedApp: new AppEntry { AccountSid = "S-1-5-21-managed", ExePath = @"C:\Apps\tool.exe" });

        var result = policy.ResolveSelection(
            options,
            initialAccountSid: null,
            lastUsedAccountSid: "S-1-5-21-other",
            lastUsedContainerName: "rfn_browser",
            currentUserSid: "S-1-5-21-current",
            shortcutContext,
            app: null);

        Assert.Equal(1, result.SelectedIndex);
        Assert.Equal("Add app entry\u2026", result.AddAppButtonText);
    }

    [Fact]
    public void ResolveSelection_WhenLastUsedIsCurrentUser_PrefersLastUsedContainer()
    {
        var policy = CreatePolicy();
        IRunAsAccountOption[] options =
        {
            CreateCredentialOption("S-1-5-21-current", "Current"),
            CreateCredentialOption("S-1-5-21-other", "Other"),
            CreateContainerOption("rfn_browser", "Browser")
        };

        var result = policy.ResolveSelection(
            options,
            initialAccountSid: null,
            lastUsedAccountSid: "S-1-5-21-current",
            lastUsedContainerName: "rfn_browser",
            currentUserSid: "S-1-5-21-current",
            shortcutContext: null,
            app: null);

        Assert.Equal(2, result.SelectedIndex);
        Assert.Equal(PrivilegeLevel.LowIntegrity, result.PrivilegeLevel);
    }

    [Fact]
    public void ResolveSelection_WhenCredentialHasExistingApp_UsesExistingPrivilegeAndDisablesSelection()
    {
        var policy = CreatePolicy();
        var existingApp = new AppEntry
        {
            AccountSid = "S-1-5-21-user",
            ExePath = @"C:\Apps\tool.exe",
            PrivilegeLevel = PrivilegeLevel.HighestAllowed
        };

        var result = policy.ResolveSelection(
            CreateCredentialOption("S-1-5-21-user", "User", existingApp: existingApp));

        Assert.Equal(PrivilegeLevel.HighestAllowed, result.PrivilegeLevel);
        Assert.False(result.PrivilegeSelectionEnabled);
        Assert.Equal("Edit app entry\u2026", result.AddAppButtonText);
        Assert.Same(existingApp, result.ExistingAppForSelection);
    }

    [Fact]
    public void ResolveSelection_WhenCredentialIsIsolatedAndOptionSuggestsBasic_UsesBasic()
    {
        var policy = CreatePolicy();

        var result = policy.ResolveSelection(
            CreateCredentialOption("S-1-5-21-user", "User", suggestsBasicPrivilegeLevel: true));

        Assert.Equal(PrivilegeLevel.Basic, result.PrivilegeLevel);
        Assert.True(result.PrivilegeSelectionEnabled);
    }

    [Fact]
    public void ResolveSelection_WhenCredentialIsHighIntegrityAndOptionSuggestsBasic_KeepsHighIntegrity()
    {
        var policy = CreatePolicy();

        var result = policy.ResolveSelection(
            CreateCredentialOption(
                "S-1-5-21-user",
                "User",
                accountPrivilegeLevel: PrivilegeLevel.HighIntegrity,
                suggestsBasicPrivilegeLevel: true));

        Assert.Equal(PrivilegeLevel.HighIntegrity, result.PrivilegeLevel);
        Assert.True(result.PrivilegeSelectionEnabled);
    }

    [Fact]
    public void ResolveSelection_WhenCreateContainerSelected_DisablesAddAppAndPrivilegeSelection()
    {
        var policy = CreatePolicy();

        var result = policy.ResolveSelection(
            new CreateContainerRunAsOption(
                "Create new container\u2026",
                IsSelectable: true,
                ExistingAppForSelection: null));

        Assert.Equal(PrivilegeLevel.LowIntegrity, result.PrivilegeLevel);
        Assert.False(result.PrivilegeSelectionEnabled);
        Assert.False(result.AddAppButtonEnabled);
    }

    [Fact]
    public void ResolveSelection_WhenSelectedOptionIsNotSelectable_ReportsDisallowedSelection()
    {
        var policy = CreatePolicy();

        var result = policy.ResolveSelection(
            new CredentialRunAsOption(
                new CredentialEntry { Sid = "S-1-5-21-user" },
                "S-1-5-21-user",
                "User",
                IsCurrentAccount: false,
                IsSelectable: false,
                PrivilegeLevel.Isolated,
                ExistingAppForSelection: null,
                SuggestsBasicPrivilegeLevel: false));

        Assert.False(result.IsSelectionAllowed);
    }

    [Fact]
    public void ResolveSelection_WhenProvidedAppDoesNotMatchSelectedOption_DoesNotAttachIt()
    {
        var policy = CreatePolicy();
        var unrelatedApp = new AppEntry
        {
            AccountSid = "S-1-5-21-other",
            AppContainerName = "rfn_other",
            ExePath = @"C:\Apps\other.exe",
            PrivilegeLevel = PrivilegeLevel.HighestAllowed
        };

        var result = policy.ResolveSelection(
            [CreateCredentialOption("S-1-5-21-user", "User")],
            initialAccountSid: "S-1-5-21-user",
            lastUsedAccountSid: null,
            lastUsedContainerName: null,
            currentUserSid: null,
            shortcutContext: null,
            app: unrelatedApp);

        Assert.Null(result.ExistingAppForSelection);
        Assert.Equal("Add app entry\u2026", result.AddAppButtonText);
    }

    private static RunAsSelectionPolicy CreatePolicy() => new();

    private static CredentialRunAsOption CreateCredentialOption(
        string sid,
        string displayName,
        AppEntry? existingApp = null,
        PrivilegeLevel accountPrivilegeLevel = PrivilegeLevel.Isolated,
        bool suggestsBasicPrivilegeLevel = false)
        => new(
            new CredentialEntry { Sid = sid },
            sid,
            displayName,
            IsCurrentAccount: false,
            IsSelectable: true,
            accountPrivilegeLevel,
            ExistingAppForSelection: existingApp,
            SuggestsBasicPrivilegeLevel: suggestsBasicPrivilegeLevel);

    private static AppContainerRunAsOption CreateContainerOption(string name, string displayName)
        => new(
            new AppContainerEntry { Name = name, DisplayName = displayName },
            $"S-1-15-2-{name}",
            name,
            displayName,
            IsSelectable: true,
            PrivilegeLevel.LowIntegrity,
            ExistingAppForSelection: null,
            SuggestsBasicPrivilegeLevel: false);
}
