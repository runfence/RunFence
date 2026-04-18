using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class ApplicationsGridPopulatorTests
{
    [Fact]
    public void PopulateGrid_DisposesOldCachedIconsOnExePathChange()
    {
        // Arrange
        var iconService = new Mock<IIconService>();
        var appConfigService = new Mock<IAppConfigService>();
        var sidNameCache = new Mock<ISidNameCacheService>();

        var populator = new ApplicationsGridPopulator(iconService.Object, appConfigService.Object, sidNameCache.Object);

        using var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExePath" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Account" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ACL" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Shortcuts" });

        var db = new AppDatabase();
        var credStore = new CredentialStore();
        var state = new Mock<IApplicationsPanelState>();
        state.Setup(s => s.Database).Returns(db);
        state.Setup(s => s.CredentialStore).Returns(credStore);
        state.Setup(s => s.IsSortActive).Returns(false);

        populator.Initialize(grid, state.Object, (apps, key) => apps.OrderBy(key));

        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns((string sid) => "TestUser");

        var testIcon = new Bitmap(16, 16);

        var app1 = new AppEntry
        {
            Id = "app1",
            Name = "App1",
            ExePath = @"C:\app1.exe",
            AccountSid = "S-1-5-21-111-222-333-1001"
        };
        db.Apps.Add(app1);

        iconService.Setup(i => i.GetOriginalAppIcon(It.IsAny<AppEntry>(), It.IsAny<int>())).Returns((Image)testIcon);

        var dragDrop = new AppGridDragDropHandler(appConfigService.Object);
        populator.PopulateGrid(dragDrop, _ => { }, () => { });

        app1.ExePath = @"C:\app1_v2.exe";
        var newIcon = new Bitmap(16, 16);
        iconService.Setup(i => i.GetOriginalAppIcon(It.IsAny<AppEntry>(), It.IsAny<int>())).Returns((Image)newIcon);

        // Act
        populator.PopulateGrid(dragDrop, _ => { }, () => { });

        // Assert: old icon was disposed
        Assert.ThrowsAny<Exception>(() => _ = testIcon.Size);

        // Cleanup
        populator.DisposeFont();
        newIcon.Dispose();
    }
}
