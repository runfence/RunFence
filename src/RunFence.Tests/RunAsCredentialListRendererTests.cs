using System.Drawing.Imaging;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.RunAs.UI;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsCredentialListRendererTests
{
    [Theory]
    [MemberData(nameof(RenderedDisplayItemCases))]
    public void Renderer_UsesDisplayItemRendering(object displayItem)
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new TestListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                Font = SystemFonts.MessageBoxFont
            };
            var renderer = new RunAsCredentialListRenderer();
            renderer.Attach(listBox);
            var isSeparator = displayItem is ContainerSeparatorItem;

            using var expected = Render(listBox, new RunAsAccountListItem(displayItem, "wrapper text A", null, isSeparator));
            using var actual = Render(listBox, new RunAsAccountListItem(displayItem, "wrapper text B", null, isSeparator));

            Assert.True(BitmapsEqual(expected, actual));
        });
    }

    [Fact]
    public void Renderer_DifferentDisplayItems_ProduceDifferentOutput()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new TestListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                Font = SystemFonts.MessageBoxFont
            };
            var renderer = new RunAsCredentialListRenderer();
            renderer.Attach(listBox);

            var first = new CredentialDisplayItem(
                new CredentialEntry { Sid = "S-1-5-21-alpha" },
                sidResolver: new TestSidResolver(),
                profilePathResolver: new TestProfilePathResolver(),
                sidNames: new Dictionary<string, string> { ["S-1-5-21-alpha"] = "Alpha User" },
                hasStoredCredential: true);
            var second = new CredentialDisplayItem(
                new CredentialEntry { Sid = "S-1-5-21-bravo" },
                sidResolver: new TestSidResolver(),
                profilePathResolver: new TestProfilePathResolver(),
                sidNames: new Dictionary<string, string> { ["S-1-5-21-bravo"] = "Bravo User" },
                hasStoredCredential: true);

            using var firstBitmap = Render(listBox, new RunAsAccountListItem(first, "wrapper text", null, isSeparator: false));
            using var secondBitmap = Render(listBox, new RunAsAccountListItem(second, "wrapper text", null, isSeparator: false));

            Assert.False(BitmapsEqual(firstBitmap, secondBitmap));
        });
    }

    public static TheoryData<object> RenderedDisplayItemCases()
    {
        return new TheoryData<object>
        {
            new CredentialDisplayItem(
                new CredentialEntry { Sid = "S-1-5-21-user" },
                sidResolver: new TestSidResolver(),
                profilePathResolver: new TestProfilePathResolver(),
                sidNames: new Dictionary<string, string> { ["S-1-5-21-user"] = "User" },
                hasStoredCredential: true),
            new AppContainerDisplayItem(
                new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" },
                "S-1-15-2-100"),
            new CreateAccountItem(),
            new CreateContainerItem(),
            new ContainerSeparatorItem()
        };
    }

    private static Bitmap Render(TestListBox listBox, RunAsAccountListItem item)
    {
        listBox.Items.Clear();
        listBox.Items.Add(item);

        var bitmap = new Bitmap(240, 30, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var drawItemArgs = new DrawItemEventArgs(
            graphics,
            listBox.Font,
            bounds,
            0,
            DrawItemState.Default,
            Color.Black,
            Color.White);
        listBox.RaiseDrawItem(drawItemArgs);
        return bitmap;
    }

    private static bool BitmapsEqual(Bitmap expected, Bitmap actual)
    {
        if (expected.Size != actual.Size)
            return false;

        for (var x = 0; x < expected.Width; x++)
        {
            for (var y = 0; y < expected.Height; y++)
            {
                if (expected.GetPixel(x, y) != actual.GetPixel(x, y))
                    return false;
            }
        }

        return true;
    }

    private sealed class TestListBox : ListBox
    {
        public void RaiseDrawItem(DrawItemEventArgs e) => OnDrawItem(e);
    }

    private sealed class TestSidResolver : ISidResolver
    {
        public string GetCurrentUserSid() => "S-1-5-21-current";
        public string? ResolveSidFromName(string accountName, IReadOnlyList<LocalUserAccount>? localUsers) => null;
        public string? TryResolveName(string sid) => sid switch
        {
            "S-1-5-21-user" => "User",
            "S-1-5-21-alpha" => "Alpha User",
            "S-1-5-21-bravo" => "Bravo User",
            _ => null
        };
        public string? TryResolveSid(string accountName) => null;
    }

    private sealed class TestProfilePathResolver : IProfilePathResolver
    {
        public string? TryGetProfilePath(string sid) => null;
        public string? TryGetDesktopPath(string sid, bool isCurrentAccount) => null;
        public string? TryGetStartMenuProgramsPath(string sid, bool isCurrentAccount) => null;
        public string? TryGetTaskBarPath(string sid, bool isCurrentAccount) => null;
        public string? TryResolveNameFromRegistry(string sid) => null;
    }
}
