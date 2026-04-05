using RunFence.Core.Models;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class GroupFilterHelperTests
{
    private static LocalUserAccount Group(string name, string sid) => new(name, sid);

    [Fact]
    public void FilterForGroupsPanel_ExcludesNameFilteredGroups()
    {
        var groups = new[]
        {
            Group("Users", GroupFilterHelper.UsersSid),
            Group("Administrators", GroupFilterHelper.AdministratorsSid),
            Group("OpenSSH Users", "S-1-5-21-1234-5001"),
            Group("System Managed Accounts Group", "S-1-5-21-1234-5002"),
            Group("Device Owners", "S-1-5-21-1234-5003"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };

        var result = GroupFilterHelper.FilterForGroupsPanel(groups).ToList();

        Assert.DoesNotContain(result, g => g.Username == "OpenSSH Users");
        Assert.DoesNotContain(result, g => g.Username == "System Managed Accounts Group");
        Assert.DoesNotContain(result, g => g.Username == "Device Owners");
        Assert.Contains(result, g => g.Username == "Users");
        Assert.Contains(result, g => g.Username == "Administrators");
        Assert.Contains(result, g => g.Username == "Developers");
    }

    [Fact]
    public void FilterForGroupsPanel_IncludesSidFilteredGroups()
    {
        // Groups excluded from account dialogs (FilteredGroupSids) are shown in the Groups panel
        var groups = new[]
        {
            Group("Performance Monitor Users", "S-1-5-32-558"),
            Group("IIS_IUSRS", "S-1-5-32-568"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };

        var result = GroupFilterHelper.FilterForGroupsPanel(groups).ToList();

        Assert.Contains(result, g => g.Username == "Performance Monitor Users");
        Assert.Contains(result, g => g.Username == "IIS_IUSRS");
        Assert.Contains(result, g => g.Username == "Developers");
    }

    [Fact]
    public void FilterForGroupsPanel_SortsUsersFirst_AdministratorsSecond_ThenAlphabetical()
    {
        var groups = new[]
        {
            Group("Zebra", "S-1-5-21-1234-9999"),
            Group("Administrators", GroupFilterHelper.AdministratorsSid),
            Group("Alpha", "S-1-5-21-1234-1001"),
            Group("Users", GroupFilterHelper.UsersSid),
        };

        var result = GroupFilterHelper.FilterForGroupsPanel(groups).ToList();

        Assert.Equal("Users", result[0].Username);
        Assert.Equal("Administrators", result[1].Username);
        Assert.Equal("Alpha", result[2].Username);
        Assert.Equal("Zebra", result[3].Username);
    }

    [Fact]
    public void FilterForGroupsPanel_EmptyInput_ReturnsEmpty()
    {
        var result = GroupFilterHelper.FilterForGroupsPanel([]).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void FilterForCreateDialog_ExcludesSidFilteredGroups()
    {
        // Groups in FilteredGroupSids are excluded from the Create Account dialog (unlike Groups panel)
        var groups = new[]
        {
            Group("Performance Monitor Users", "S-1-5-32-558"),
            Group("IIS_IUSRS", "S-1-5-32-568"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };

        var result = GroupFilterHelper.FilterForCreateDialog(groups).ToList();

        Assert.DoesNotContain(result, g => g.Username == "Performance Monitor Users");
        Assert.DoesNotContain(result, g => g.Username == "IIS_IUSRS");
        Assert.Contains(result, g => g.Username == "Developers");
    }

    [Fact]
    public void FilterForCreateDialog_ExcludesNameFilteredGroups()
    {
        var groups = new[]
        {
            Group("OpenSSH Users", "S-1-5-21-1234-5001"),
            Group("Device Owners", "S-1-5-21-1234-5002"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };

        var result = GroupFilterHelper.FilterForCreateDialog(groups).ToList();

        Assert.DoesNotContain(result, g => g.Username == "OpenSSH Users");
        Assert.DoesNotContain(result, g => g.Username == "Device Owners");
        Assert.Contains(result, g => g.Username == "Developers");
    }

    [Fact]
    public void FilterForEditDialog_KeepsCurrentGroupSidsEvenIfFiltered()
    {
        // A SID-filtered group the account is already a member of must remain visible
        var groups = new[]
        {
            Group("Performance Monitor Users", "S-1-5-32-558"),
            Group("IIS_IUSRS", "S-1-5-32-568"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };
        var currentGroupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-32-558" };
        var neverFilteredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = GroupFilterHelper.FilterForEditDialog(groups, currentGroupSids, neverFilteredNames).ToList();

        Assert.Contains(result, g => g.Username == "Performance Monitor Users");
        Assert.DoesNotContain(result, g => g.Username == "IIS_IUSRS");
        Assert.Contains(result, g => g.Username == "Developers");
    }

    [Fact]
    public void FilterForEditDialog_KeepsNeverFilteredGroupNames()
    {
        // Groups whose names appear in neverFilteredGroupNames are always kept
        var groups = new[]
        {
            Group("OpenSSH Users", "S-1-5-21-1234-5001"),
            Group("Device Owners", "S-1-5-21-1234-5002"),
            Group("Developers", "S-1-5-21-1234-1001"),
        };
        var currentGroupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var neverFilteredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OpenSSH Users" };

        var result = GroupFilterHelper.FilterForEditDialog(groups, currentGroupSids, neverFilteredNames).ToList();

        Assert.Contains(result, g => g.Username == "OpenSSH Users");
        Assert.DoesNotContain(result, g => g.Username == "Device Owners");
        Assert.Contains(result, g => g.Username == "Developers");
    }
}