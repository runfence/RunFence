using System.Drawing;
using System.Drawing.Imaging;
using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public class AccountGridIconLifetimeManagerTests
{
    [Fact]
    public void ReleaseRowIcons_DisposesOnlyTrackedRowImagesOnce()
    {
        using var manager = new AccountGridIconLifetimeManager();
        using var grid = new DataGridView();
        grid.Columns.Add("Account", "Account");
        var row1 = new DataGridViewRow();
        row1.CreateCells(grid, "row1");
        var row2 = new DataGridViewRow();
        row2.CreateCells(grid, "row2");

        using var icon1 = new Bitmap(8, 8);
        using var icon2 = new Bitmap(8, 8);
        manager.TrackOwned(row1, icon1);
        manager.TrackOwned(row2, icon2);

        manager.ReleaseRowIcons(row1);
        manager.ReleaseRowIcons(row1);

        AssertImageDisposed(icon1);
        AssertImageUsable(icon2);

        manager.ReleaseAllTrackedIcons();

        AssertImageDisposed(icon1);
        AssertImageDisposed(icon2);
    }

    [Fact]
    public void ReleaseAllTrackedIcons_DoesNotDisposeEmptyIcon()
    {
        using var manager = new AccountGridIconLifetimeManager();
        using var grid = new DataGridView();
        grid.Columns.Add("Account", "Account");
        var row = new DataGridViewRow();
        row.CreateCells(grid, "row");

        manager.TrackOwned(row, AccountGridHelper.EmptyIcon);
        manager.ReleaseAllTrackedIcons();

        Assert.True(AccountGridHelper.EmptyIcon.Width > 0);
    }

    private static void AssertImageUsable(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
    }

    private static void AssertImageDisposed(Image image)
    {
        using var stream = new MemoryStream();
        Assert.ThrowsAny<Exception>(() => image.Save(stream, ImageFormat.Png));
    }
}
