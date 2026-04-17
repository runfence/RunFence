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
/// Handles combo box population and item search for <see cref="AppEditDialog"/>.
/// Consolidates repeated combo-search patterns into a single generic helper.
/// </summary>
public class AppEditDialogPopulator(
    IAppConfigService appConfigService,
    ISidResolver sidResolver,
    CredentialFilterHelper credentialFilterHelper)
{
    /// <summary>
    /// Populates the account combo box with credentials (filtered to resolvable accounts)
    /// and AppContainer items separated by a visual divider.
    /// </summary>
    public void PopulateAccountCombo(
        ComboBox combo,
        List<CredentialEntry> credentials,
        IReadOnlyDictionary<string, string>? sidNames,
        AppEntry? existing,
        AppDatabase? database)
    {
        var ephemeralSids = database?.Accounts.Where(a => a.DeleteAfterUtc.HasValue).Select(a => a.Sid)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cred in credentialFilterHelper.FilterResolvableCredentials(credentials, sidNames, existing))
        {
            var isEphemeral = !string.IsNullOrEmpty(cred.Sid) && ephemeralSids.Contains(cred.Sid);
            combo.Items.Add(new CredentialDisplayItem(cred, sidResolver, sidNames, isEphemeral: isEphemeral));
            if (!string.IsNullOrEmpty(cred.Sid))
                representedSids.Add(cred.Sid);
        }

        // Add interactive user if not already represented by a stored credential
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid != null && !representedSids.Contains(interactiveSid))
        {
            var transient = new CredentialEntry { Id = Guid.NewGuid(), Sid = interactiveSid };
            combo.Items.Add(new CredentialDisplayItem(transient, sidResolver, sidNames,
                isEphemeral: ephemeralSids.Contains(interactiveSid)));
        }

        if (database?.AppContainers.Count > 0)
        {
            combo.Items.Add(new ContainerSeparatorItem());
            foreach (var container in database.AppContainers.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
                combo.Items.Add(new AppContainerDisplayItem(container, container.Sid));
        }
    }

    /// <summary>
    /// Populates the config combo box with the main config entry and all loaded additional config paths.
    /// </summary>
    public void PopulateConfigCombo(ComboBox combo)
    {
        combo.Items.Add(new ConfigComboItem(null));
        foreach (var path in appConfigService.GetLoadedConfigPaths())
            combo.Items.Add(new ConfigComboItem(path));
        combo.SelectedIndex = 0;
    }

    /// <summary>
    /// Selects the first combo item of type <typeparamref name="T"/> matching the predicate,
    /// starting from <paramref name="startIndex"/>. Returns true if found.
    /// </summary>
    public static bool SelectComboItem<T>(ComboBox combo, Func<T, bool> match, int startIndex = 0)
        where T : class
    {
        for (int i = startIndex; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is T item && match(item))
            {
                combo.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }
}