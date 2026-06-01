using Moq;
using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class ApplicationsGridPopulatorTests
{
    [Fact]
    public void PopulateGrid_DisposesOldCachedIconsOnExePathChange()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var iconService = new Mock<IIconService>();
            var sidNameCache = new Mock<ISidNameCacheService>();
            var appConfig = new AppConfigTestContext();

            var populator = new ApplicationsGridPopulator(iconService.Object, appConfig.Service, sidNameCache.Object);

            using var grid = new DataGridView();
            grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AppName" });
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

            sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns("TestUser");

            var testIcon = new Bitmap(16, 16);

            var app1 = new AppEntry
            {
                Id = "app1",
                Name = "App1",
                ExePath = @"C:\app1.exe",
                AccountSid = "S-1-5-21-111-222-333-1001"
            };
            db.Apps.Add(app1);

            iconService.Setup(i => i.GetOriginalAppIcon(It.IsAny<AppEntry>(), It.IsAny<int>())).Returns(testIcon);

            var dragDrop = new AppGridDragDropHandler(appConfig.Service);
            populator.PopulateGrid(dragDrop, _ => { }, () => { });

            app1.ExePath = @"C:\app1_v2.exe";
            var newIcon = new Bitmap(16, 16);
            iconService.Setup(i => i.GetOriginalAppIcon(It.IsAny<AppEntry>(), It.IsAny<int>())).Returns(newIcon);

            populator.PopulateGrid(dragDrop, _ => { }, () => { });

            Assert.ThrowsAny<Exception>(() => _ = testIcon.Size);

            populator.DisposeFont();
            newIcon.Dispose();
        });
    }
}
