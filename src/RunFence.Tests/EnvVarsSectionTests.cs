using RunFence.Apps.UI.Forms;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class EnvVarsSectionTests
{
    [Fact]
    public void GetItems_CommitsActiveEditBeforeReadingRows()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var section = new EnvVarsSection();
            host.Controls.Add(section);
            host.CreateControl();
            section.CreateControl();

            section.SetItems(new Dictionary<string, string> { ["PATH"] = "old" });
            var grid = FindGrid(section);

            grid.CurrentCell = grid.Rows[0].Cells[1];
            Assert.True(grid.BeginEdit(true));
            ((DataGridViewTextBoxEditingControl)grid.EditingControl!).Text = "new";

            var items = section.GetItems();

            Assert.NotNull(items);
            Assert.Equal("new", items!["PATH"]);
        });
    }

    [Fact]
    public void GetFirstDuplicateName_CommitsActiveEditBeforeCheckingDuplicates()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var section = new EnvVarsSection();
            host.Controls.Add(section);
            host.CreateControl();
            section.CreateControl();

            section.SetItems(new Dictionary<string, string>
            {
                ["PATH"] = "one",
                ["TEMP"] = "two"
            });
            var grid = FindGrid(section);

            grid.CurrentCell = grid.Rows[1].Cells[0];
            Assert.True(grid.BeginEdit(true));
            ((DataGridViewTextBoxEditingControl)grid.EditingControl!).Text = "PATH";

            var duplicate = section.GetFirstDuplicateName();

            Assert.Equal("PATH", duplicate);
        });
    }

    private static DataGridView FindGrid(Control root) =>
        FindControls<DataGridView>(root).Single();

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }
}
