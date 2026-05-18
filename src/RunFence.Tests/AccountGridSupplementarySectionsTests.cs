using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountGridSupplementarySectionsTests
{
    [Fact]
    public void AddAppContainersSection_UsesPathProviderForProfilePath()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var grid = CreateAccountsGrid();
            using var iconLifetimeManager = new AccountGridIconLifetimeManager();
            host.Controls.Add(grid);
            StaTestHelper.CreateControlTree(host);

            var sections = new AccountGridSupplementarySections(
                new Mock<IWindowsAccountQueryService>().Object,
                new Mock<IAccountLoginRestrictionService>().Object,
                new SidDisplayNameResolver(new Mock<ISidResolver>().Object, new Mock<IProfilePathResolver>().Object),
                iconLifetimeManager,
                AppContainerProviderTestDoubles.CreatePathProvider(@"D:\Containers"));

            var database = new AppDatabase();
            database.AppContainers.Add(new AppContainerEntry
            {
                Name = "ram_browser",
                DisplayName = "Browser",
                Sid = "S-1-15-2-42"
            });

            sections.AddAppContainersSection(grid, database);

            var containerRow = Assert.Single(grid.Rows.Cast<DataGridViewRow>(), row => row.Tag is ContainerRow);
            Assert.Equal(@"D:\Containers\ram_browser", containerRow.Cells["ProfilePath"].Value);
        });
    }

    private static DataGridView CreateAccountsGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            RowHeadersVisible = false
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Import" });
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Credential" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Logon" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colAllowInternet" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Apps" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProfilePath" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SID" });
        return grid;
    }
}
