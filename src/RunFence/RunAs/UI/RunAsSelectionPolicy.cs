using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs.UI;

public sealed class RunAsSelectionPolicy
{
    public RunAsSelectionResult ResolveSelection(
        IReadOnlyList<IRunAsAccountOption> options,
        string? initialAccountSid,
        string? lastUsedAccountSid,
        string? lastUsedContainerName,
        string? currentUserSid,
        ShortcutContext? shortcutContext,
        AppEntry? app)
    {
        if (options.Count == 0)
        {
            return new RunAsSelectionResult(
                SelectedIndex: -1,
                IsSelectionAllowed: false,
                PrivilegeLevel: PrivilegeLevel.Isolated,
                PrivilegeSelectionEnabled: false,
                AddAppButtonText: "Add app entry\u2026",
                AddAppButtonEnabled: false,
                ExistingAppForSelection: null);
        }

        var selectedIndex = ResolveSelectedIndex(
            options,
            initialAccountSid,
            lastUsedAccountSid,
            lastUsedContainerName,
            currentUserSid,
            shortcutContext);

        var selectedOption = options[selectedIndex];
        var optionWithApp = TryAttachMatchingApp(selectedOption, app);
        var selection = ResolveSelection(optionWithApp);
        return selection with { SelectedIndex = selectedIndex };
    }

    public RunAsSelectionResult ResolveSelection(IRunAsAccountOption option)
    {
        return option switch
        {
            CreateAccountRunAsOption => new RunAsSelectionResult(
                SelectedIndex: 0,
                IsSelectionAllowed: option.IsSelectable,
                PrivilegeLevel: SuggestPrivilegeLevel(option.AccountPrivilegeLevel, option.SuggestsBasicPrivilegeLevel),
                PrivilegeSelectionEnabled: true,
                AddAppButtonText: "Add app entry\u2026",
                AddAppButtonEnabled: true,
                ExistingAppForSelection: null),
            CreateContainerRunAsOption => new RunAsSelectionResult(
                SelectedIndex: 0,
                IsSelectionAllowed: option.IsSelectable,
                PrivilegeLevel: PrivilegeLevel.LowIntegrity,
                PrivilegeSelectionEnabled: false,
                AddAppButtonText: "Add app entry\u2026",
                AddAppButtonEnabled: false,
                ExistingAppForSelection: null),
            AppContainerRunAsOption => new RunAsSelectionResult(
                SelectedIndex: 0,
                IsSelectionAllowed: option.IsSelectable,
                PrivilegeLevel: PrivilegeLevel.LowIntegrity,
                PrivilegeSelectionEnabled: false,
                AddAppButtonText: option.ExistingAppForSelection != null ? "Edit app entry\u2026" : "Add app entry\u2026",
                AddAppButtonEnabled: true,
                ExistingAppForSelection: option.ExistingAppForSelection),
            CredentialRunAsOption credentialOption => ResolveCredentialState(credentialOption),
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
    }

    private RunAsSelectionResult ResolveCredentialState(CredentialRunAsOption option)
    {
        var isSystem = option.Sid.Length > 0 && SidResolutionHelper.IsSystemSid(option.Sid);

        PrivilegeLevel privilegeLevel;
        bool privilegeSelectionEnabled;
        if (option.ExistingAppForSelection != null && !isSystem)
        {
            privilegeLevel = option.ExistingAppForSelection.PrivilegeLevel ?? option.AccountPrivilegeLevel;
            privilegeSelectionEnabled = false;
        }
        else
        {
            privilegeLevel = SuggestPrivilegeLevel(option.AccountPrivilegeLevel, option.SuggestsBasicPrivilegeLevel);
            privilegeSelectionEnabled = !isSystem;
        }

        return new RunAsSelectionResult(
            SelectedIndex: 0,
            IsSelectionAllowed: option.IsSelectable,
            PrivilegeLevel: privilegeLevel,
            PrivilegeSelectionEnabled: privilegeSelectionEnabled,
            AddAppButtonText: option.ExistingAppForSelection != null ? "Edit app entry\u2026" : "Add app entry\u2026",
            AddAppButtonEnabled: true,
            ExistingAppForSelection: option.ExistingAppForSelection);
    }

    private static int ResolveSelectedIndex(
        IReadOnlyList<IRunAsAccountOption> options,
        string? initialAccountSid,
        string? lastUsedAccountSid,
        string? lastUsedContainerName,
        string? currentUserSid,
        ShortcutContext? shortcutContext)
    {
        if (shortcutContext is { IsAlreadyManaged: true, ManagedApp: not null })
        {
            var managedSelection = FindManagedSelection(options, shortcutContext.ManagedApp);
            if (managedSelection >= 0)
                return managedSelection;
        }

        var preferredSelection = FindPreferredSelectionForNewApp(
            options,
            initialAccountSid,
            lastUsedAccountSid,
            lastUsedContainerName,
            currentUserSid);
        return preferredSelection >= 0 ? preferredSelection : 0;
    }

    private static int FindManagedSelection(IReadOnlyList<IRunAsAccountOption> options, AppEntry app)
    {
        if (!string.IsNullOrEmpty(app.AccountSid))
        {
            var accountIndex = FindIndex(
                options,
                option => option is CredentialRunAsOption credentialOption
                    && string.Equals(credentialOption.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));
            if (accountIndex >= 0)
                return accountIndex;
        }

        if (!string.IsNullOrEmpty(app.AppContainerName))
        {
            var containerIndex = FindIndex(
                options,
                option => option is AppContainerRunAsOption containerOption
                    && string.Equals(containerOption.ContainerName, app.AppContainerName, StringComparison.OrdinalIgnoreCase));
            if (containerIndex >= 0)
                return containerIndex;
        }

        return 0;
    }

    private static int FindPreferredSelectionForNewApp(
        IReadOnlyList<IRunAsAccountOption> options,
        string? initialAccountSid,
        string? lastUsedAccountSid,
        string? lastUsedContainerName,
        string? currentUserSid)
    {
        if (!string.IsNullOrEmpty(initialAccountSid))
        {
            var initialSelection = FindIndex(
                options,
                option => option is CredentialRunAsOption credentialOption
                    && string.Equals(credentialOption.Sid, initialAccountSid, StringComparison.OrdinalIgnoreCase));
            if (initialSelection >= 0)
                return initialSelection;
        }

        if (!string.IsNullOrEmpty(lastUsedAccountSid)
            && !string.Equals(lastUsedAccountSid, currentUserSid, StringComparison.OrdinalIgnoreCase))
        {
            var lastUsedSelection = FindIndex(
                options,
                option => option is CredentialRunAsOption credentialOption
                    && string.Equals(credentialOption.Sid, lastUsedAccountSid, StringComparison.OrdinalIgnoreCase));
            if (lastUsedSelection >= 0)
                return lastUsedSelection;
        }

        if (!string.IsNullOrEmpty(lastUsedContainerName))
        {
            var containerSelection = FindIndex(
                options,
                option => option is AppContainerRunAsOption containerOption
                    && string.Equals(containerOption.ContainerName, lastUsedContainerName, StringComparison.OrdinalIgnoreCase));
            if (containerSelection >= 0)
                return containerSelection;
        }

        return FindIndex(
            options,
            option => option.IsSelectable
                && (option is AppContainerRunAsOption
                    || (option is CredentialRunAsOption credentialOption
                        && !string.Equals(credentialOption.Sid, currentUserSid, StringComparison.OrdinalIgnoreCase))));
    }

    private static int FindIndex(IReadOnlyList<IRunAsAccountOption> options, Func<IRunAsAccountOption, bool> predicate)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (predicate(options[i]))
                return i;
        }

        return -1;
    }

    private static IRunAsAccountOption TryAttachMatchingApp(IRunAsAccountOption option, AppEntry? app)
    {
        if (app == null || option.ExistingAppForSelection != null)
            return option;

        return option switch
        {
            CredentialRunAsOption credentialOption
                when !string.IsNullOrEmpty(app.AccountSid)
                     && string.Equals(credentialOption.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase)
                => credentialOption with { ExistingAppForSelection = app },
            AppContainerRunAsOption containerOption
                when !string.IsNullOrEmpty(app.AppContainerName)
                     && string.Equals(containerOption.ContainerName, app.AppContainerName, StringComparison.OrdinalIgnoreCase)
                => containerOption with { ExistingAppForSelection = app },
            _ => option
        };
    }

    private PrivilegeLevel SuggestPrivilegeLevel(PrivilegeLevel accountPrivilegeLevel, bool suggestsBasicPrivilegeLevel)
        => accountPrivilegeLevel == PrivilegeLevel.Isolated && suggestsBasicPrivilegeLevel
            ? PrivilegeLevel.Basic
            : accountPrivilegeLevel;
}
