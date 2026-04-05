namespace RunFence.Account.UI;

/// <summary>
/// Manages the install-package checklist for account dialogs.
/// Handles package dependency enforcement (Winget required for Windows Terminal)
/// and prevents unchecking already-installed packages.
/// </summary>
public class InstallPackageSelector
{
    private HashSet<int> _installedPackageIndices = new();

    /// <summary>
    /// Populates the list and marks already-installed packages as checked and locked.
    /// </summary>
    public void Configure(CheckedListBox list, bool canInstall, Func<InstallablePackage, bool>? isPackageInstalled)
    {
        _installedPackageIndices = new HashSet<int>();

        foreach (var package in KnownPackages.All)
            list.Items.Add(package.DisplayName);

        if (!canInstall)
        {
            list.Enabled = false;
            return;
        }

        if (isPackageInstalled != null)
        {
            for (int i = 0; i < KnownPackages.All.Count; i++)
            {
                if (isPackageInstalled(KnownPackages.All[i]))
                {
                    list.SetItemChecked(i, true);
                    _installedPackageIndices.Add(i);
                }
            }
        }
    }

    /// <summary>
    /// Validates a pending item-check event and returns the override state if a dependency
    /// constraint applies, or <c>null</c> to accept the user's choice as-is.
    /// </summary>
    public CheckState? ValidateItemCheck(CheckedListBox list, int index, CheckState newValue)
    {
        // Prevent unchecking already-installed items
        if (_installedPackageIndices.Contains(index))
            return CheckState.Checked;

        // Auto-check Winget when Terminal is checked
        if (newValue == CheckState.Checked && KnownPackages.All[index] == KnownPackages.WindowsTerminal)
        {
            int wingetIndex = FindPackageIndex(KnownPackages.Winget);
            if (wingetIndex >= 0 && !_installedPackageIndices.Contains(wingetIndex))
                list.BeginInvoke(() => list.SetItemChecked(wingetIndex, true));
        }

        // Prevent unchecking Winget when Terminal is checked (dependency)
        if (newValue == CheckState.Unchecked && KnownPackages.All[index] == KnownPackages.Winget)
        {
            int terminalIndex = FindPackageIndex(KnownPackages.WindowsTerminal);
            if (terminalIndex >= 0 && list.GetItemChecked(terminalIndex))
                return CheckState.Checked;
        }

        return null;
    }

    /// <summary>
    /// Returns packages that are selected by the user but not already installed.
    /// </summary>
    public List<InstallablePackage> GetSelectedPackages(CheckedListBox list)
    {
        var result = new List<InstallablePackage>();
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.GetItemChecked(i) && !_installedPackageIndices.Contains(i))
                result.Add(KnownPackages.All[i]);
        }

        return result;
    }

    private static int FindPackageIndex(InstallablePackage package)
    {
        for (int i = 0; i < KnownPackages.All.Count; i++)
        {
            if (KnownPackages.All[i] == package)
                return i;
        }

        return -1;
    }
}