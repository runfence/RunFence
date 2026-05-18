using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpTextResolverTests
{
    [Fact]
    public void Resolve_UsesAncestorFallbackWhenOnlyParentIsRegistered()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var panel = new Panel { Size = new Size(120, 80) };
            var child = new Button { Location = new Point(10, 10), Size = new Size(60, 24) };
            panel.Controls.Add(child);
            form.Controls.Add(panel);
            StaTestHelper.CreateControlTree(form);

            form.SetContextHelp(panel, "parent-help");

            var target = ContextHelpTextResolver.Resolve(
                form,
                new ContextHelpButton(),
                child,
                child.PointToScreen(new Point(5, 5)));

            Assert.NotNull(target);
            Assert.Equal("parent-help", target.HelpText);
        });
    }

    [Fact]
    public void Resolve_PrefersChildHelpOverParentFallback()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var panel = new Panel { Size = new Size(120, 80) };
            var child = new Button { Location = new Point(10, 10), Size = new Size(60, 24) };
            panel.Controls.Add(child);
            form.Controls.Add(panel);
            StaTestHelper.CreateControlTree(form);

            form.SetContextHelp(panel, "parent-help");
            form.SetContextHelp(child, "child-help");

            var target = ContextHelpTextResolver.Resolve(
                form,
                new ContextHelpButton(),
                child,
                child.PointToScreen(new Point(5, 5)));

            Assert.NotNull(target);
            Assert.Equal("child-help", target.HelpText);
        });
    }

    [Fact]
    public void Resolve_ReturnsInstructionTargetForContextHelpButton()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var helpButton = new ContextHelpButton
            {
                Location = new Point(8, 8),
                Size = new Size(28, 28)
            };
            form.Controls.Add(helpButton);
            StaTestHelper.CreateControlTree(form);

            form.SetContextHelp(helpButton, ContextHelpTextResolver.InstructionText);
            var point = helpButton.PointToScreen(new Point(4, 4));

            var target = ContextHelpTextResolver.Resolve(form, helpButton, helpButton, point);

            Assert.NotNull(target);
            Assert.Same(helpButton, target.AnchorControl);
            Assert.Equal(ContextHelpTextResolver.InstructionText, target.HelpText);
            Assert.True(target.ShowInstructionsOnButton);
        });
    }

    [Fact]
    public void Resolve_ReturnsToolStripItemHelp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var strip = new ToolStrip();
            var item = new ToolStripButton("Action");
            strip.Items.Add(item);
            form.Controls.Add(strip);
            StaTestHelper.CreateControlTree(form);

            form.SetContextHelp(item, "item-help");
            var point = strip.PointToScreen(new Point(item.Bounds.Left + 2, item.Bounds.Top + 2));

            var target = ContextHelpTextResolver.Resolve(form, new ContextHelpButton(), strip, point);

            Assert.NotNull(target);
            Assert.Equal("item-help", target.HelpText);
        });
    }

    [Fact]
    public void Resolve_ReturnsDropDownHelpWhenItemHasNoExplicitHelp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var menu = new ContextMenuStrip();
            var item = new ToolStripMenuItem("Remove");
            menu.Items.Add(item);
            form.SetContextHelp(menu, "menu-help");

            menu.CreateControl();
            menu.PerformLayout();
            var point = new Point(item.Bounds.Left + 2, item.Bounds.Top + 2);
            point = menu.PointToScreen(point);

            var target = ContextHelpTextResolver.Resolve(form, new ContextHelpButton(), menu, point);

            Assert.NotNull(target);
            Assert.Equal("menu-help", target.HelpText);
        });
    }

    [Fact]
    public void Resolve_ReturnsTabPageHelpFromTabHeader()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var tabs = new TabControl { Size = new Size(180, 120) };
            var page = new TabPage("General");
            tabs.TabPages.Add(page);
            form.Controls.Add(tabs);
            StaTestHelper.CreateControlTree(form);

            form.SetContextHelp(page, "tab-help");
            var tabRect = tabs.GetTabRect(0);
            var point = tabs.PointToScreen(new Point(tabRect.Left + 2, tabRect.Top + 2));

            var target = ContextHelpTextResolver.Resolve(form, new ContextHelpButton(), tabs, point);

            Assert.NotNull(target);
            Assert.Equal("tab-help", target.HelpText);
        });
    }
}
