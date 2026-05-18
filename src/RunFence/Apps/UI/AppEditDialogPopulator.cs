using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.UI;

namespace RunFence.Apps.UI;

/// <summary>
/// Builds account and config combo-box items for <see cref="AppEditDialog"/>.
/// </summary>
public class AppEditDialogPopulator(
    IAppConfigService appConfigService,
    CredentialDisplayItemFactory displayItemFactory,
    CredentialFilterHelper credentialFilterHelper)
{
    private readonly CredentialDisplayItemFactory _displayItemFactory = displayItemFactory;

    /// <summary>
    /// Builds account combo-box items from credentials (filtered to resolvable accounts)
    /// and AppContainer items separated by a visual divider.
    /// </summary>
    public IReadOnlyList<object> BuildAccountItems(
        List<CredentialEntry> credentials,
        IReadOnlyDictionary<string, string>? sidNames,
        AppEntry? existing,
        AppDatabase? database,
        string? preferredAccountSid = null)
    {
        var ephemeralSids = database?.Accounts.Where(a => a.DeleteAfterUtc.HasValue).Select(a => a.Sid)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<object>();

        void AddTransientOrExistingSid(string sid, bool isEphemeral = false)
        {
            if (string.IsNullOrEmpty(sid) || representedSids.Contains(sid))
                return;

            items.Add(_displayItemFactory.Create(credentials, sid, sidNames, isEphemeral: isEphemeral));
            representedSids.Add(sid);
        }

        foreach (var cred in credentialFilterHelper.FilterResolvableCredentials(credentials, sidNames, existing))
        {
            var isEphemeral = !string.IsNullOrEmpty(cred.Sid) && ephemeralSids.Contains(cred.Sid);
            items.Add(_displayItemFactory.Create(cred, sidNames, isEphemeral: isEphemeral));
            if (!string.IsNullOrEmpty(cred.Sid))
                representedSids.Add(cred.Sid);
        }

        // Ensure SYSTEM appears as an explicit account option even if no stored credential exists.
        if (!representedSids.Contains(Core.SidConstants.SystemSid))
            AddTransientOrExistingSid(Core.SidConstants.SystemSid);

        // Add interactive user if not already represented by a stored credential
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null && !representedSids.Contains(interactiveSid))
        {
            AddTransientOrExistingSid(interactiveSid, isEphemeral: ephemeralSids.Contains(interactiveSid));
        }

        if (preferredAccountSid != null)
            AddTransientOrExistingSid(preferredAccountSid);

        if (database?.AppContainers.Count > 0)
        {
            items.Add(new ContainerSeparatorItem());
            foreach (var container in database.AppContainers.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
                items.Add(new AppContainerDisplayItem(container, container.Sid));
        }

        return items.AsReadOnly();
    }

    /// <summary>
    /// Builds config combo-box items with the main config entry and all loaded additional config paths.
    /// </summary>
    public IReadOnlyList<ConfigComboItem> BuildConfigItems()
    {
        var items = new List<ConfigComboItem> { new(null) };
        foreach (var path in appConfigService.GetLoadedConfigPaths())
            items.Add(new ConfigComboItem(path));
        return items.AsReadOnly();
    }
}
