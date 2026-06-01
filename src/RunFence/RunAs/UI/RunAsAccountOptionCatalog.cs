using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;

namespace RunFence.RunAs.UI;

public sealed class RunAsAccountOptionCatalog(IExecutableKindService executableKindService)
{
    public IReadOnlyList<RunAsAccountOptionListEntry> Build(
        IReadOnlyList<RunAsAccountOptionSource> optionSources,
        IReadOnlyList<AppEntry> existingApps,
        string filePath,
        string? currentUserSid,
        IReadOnlyDictionary<string, PrivilegeLevel>? accountPrivilegeLevels)
    {
        var suggestsBasicPrivilegeLevel = executableKindService.SuggestsBasicPrivilegeLevel(filePath);
        var options = new List<RunAsAccountOptionListEntry>();
        foreach (var optionSource in optionSources)
        {
            if (!TryCreateOptionEntry(
                    optionSource,
                    optionSource.ListIndex,
                    existingApps,
                    filePath,
                    currentUserSid,
                    accountPrivilegeLevels,
                    suggestsBasicPrivilegeLevel,
                    out var option))
                continue;

            options.Add(option);
        }

        return options;
    }

    public RunAsAccountOptionListEntry? GetSelected(
        RunAsAccountOptionSource? selectedSource,
        int selectedIndex,
        IReadOnlyList<AppEntry> existingApps,
        string filePath,
        string? currentUserSid,
        IReadOnlyDictionary<string, PrivilegeLevel>? accountPrivilegeLevels)
        => TryCreateOptionEntry(
            selectedSource,
            selectedIndex,
            existingApps,
            filePath,
            currentUserSid,
            accountPrivilegeLevels,
            executableKindService.SuggestsBasicPrivilegeLevel(filePath),
            out var option)
            ? option
            : null;

    private bool TryCreateOptionEntry(
        RunAsAccountOptionSource? optionSource,
        int listIndex,
        IReadOnlyList<AppEntry> existingApps,
        string filePath,
        string? currentUserSid,
        IReadOnlyDictionary<string, PrivilegeLevel>? accountPrivilegeLevels,
        bool suggestsBasicPrivilegeLevel,
        out RunAsAccountOptionListEntry option)
    {
        switch (optionSource)
        {
            case CredentialRunAsOptionSource credentialSource:
            {
                var existingApp = existingApps.FirstOrDefault(app =>
                    string.Equals(app.AccountSid, credentialSource.Credential.Sid, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(app.ExePath, filePath, StringComparison.OrdinalIgnoreCase));
                var accountPrivilegeLevel = accountPrivilegeLevels?.GetValueOrDefault(
                    credentialSource.Credential.Sid,
                    PrivilegeLevel.Isolated) ?? PrivilegeLevel.Isolated;
                option = new RunAsAccountOptionListEntry(
                    listIndex,
                    new CredentialRunAsOption(
                        credentialSource.Credential,
                        credentialSource.Credential.Sid,
                        credentialSource.DisplayText,
                        string.Equals(credentialSource.Credential.Sid, currentUserSid, StringComparison.OrdinalIgnoreCase),
                        IsSelectable: true,
                        accountPrivilegeLevel,
                        existingApp,
                        suggestsBasicPrivilegeLevel));
                return true;
            }
            case AppContainerRunAsOptionSource containerSource:
            {
                var existingApp = existingApps.FirstOrDefault(app =>
                    string.Equals(app.AppContainerName, containerSource.Container.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(app.ExePath, filePath, StringComparison.OrdinalIgnoreCase));
                option = new RunAsAccountOptionListEntry(
                    listIndex,
                    new AppContainerRunAsOption(
                        containerSource.Container,
                        containerSource.ContainerSid,
                        containerSource.Container.Name,
                        containerSource.DisplayText,
                        IsSelectable: true,
                        PrivilegeLevel.LowIntegrity,
                        existingApp,
                        SuggestsBasicPrivilegeLevel: false));
                return true;
            }
            case CreateAccountRunAsOptionSource createAccountSource:
                option = new RunAsAccountOptionListEntry(
                    listIndex,
                    new CreateAccountRunAsOption(
                        createAccountSource.DisplayText,
                        IsSelectable: true,
                        PrivilegeLevel.Isolated,
                        ExistingAppForSelection: null,
                        suggestsBasicPrivilegeLevel));
                return true;
            case CreateContainerRunAsOptionSource createContainerSource:
                option = new RunAsAccountOptionListEntry(
                    listIndex,
                    new CreateContainerRunAsOption(
                        createContainerSource.DisplayText,
                        IsSelectable: true,
                        ExistingAppForSelection: null));
                return true;
            default:
                option = null!;
                return false;
        }
    }
}
