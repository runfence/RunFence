using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpTargetResolverTests
{
    [Fact]
    public void GetHighlightTargets_ReturnsRegisteredControlToolStripAndTabTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(320, 240)
            };
            var button = new ContextHelpButton
            {
                Location = new Point(260, 8),
                Size = new Size(28, 28)
            };
            var actionButton = new Button
            {
                Location = new Point(16, 20),
                Size = new Size(80, 24)
            };
            var strip = new ToolStrip
            {
                Location = new Point(16, 60)
            };
            var stripItem = new ToolStripButton("Action");
            strip.Items.Add(stripItem);
            var tabs = new TabControl
            {
                Location = new Point(16, 100),
                Size = new Size(180, 100)
            };
            var page = new TabPage("General");
            tabs.TabPages.Add(page);

            form.Controls.Add(button);
            form.Controls.Add(actionButton);
            form.Controls.Add(strip);
            form.Controls.Add(tabs);
            StaTestHelper.CreateControlTree(form);

            var registry = new ContextHelpRegistry();
            registry.SetContextHelp(button, ContextHelpTextResolver.InstructionText);
            registry.SetContextHelp(actionButton, "button-help");
            registry.SetContextHelp(stripItem, "strip-help");
            registry.SetContextHelp(page, "tab-help");
            using var overlay = new ContextHelpOverlay();
            var resolver = new ContextHelpTargetResolver(form, button, overlay, registry);

            var targets = resolver.GetHighlightTargets();

            Assert.Contains(targets, target => target.HelpText == ContextHelpTextResolver.InstructionText && target.ShowInstructionsOnButton);
            Assert.Contains(targets, target => target.HelpText == "button-help" && !target.ShowInstructionsOnButton);
            Assert.Contains(targets, target => target.HelpText == "strip-help");
            Assert.Contains(targets, target => target.HelpText == "tab-help");
            Assert.All(targets, target => Assert.True(target.Rect.Width > 0 && target.Rect.Height > 0));
        });
    }

    [Fact]
    public void TryGetHighlightAt_PrefersDeepestMatchingTarget()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(220, 180)
            };
            var helpButton = new ContextHelpButton
            {
                Location = new Point(180, 8),
                Size = new Size(28, 28)
            };
            form.Controls.Add(helpButton);
            StaTestHelper.CreateControlTree(form);

            using var overlay = new ContextHelpOverlay
            {
                Bounds = form.ClientRectangle
            };
            form.Controls.Add(overlay);
            overlay.BringToFront();

            var resolver = new ContextHelpTargetResolver(form, helpButton, overlay, new ContextHelpRegistry());
            var screenPoint = overlay.PointToScreen(new Point(40, 40));
            var targets = new[]
            {
                new ContextHelpTargetResolver.HighlightTarget(new Rectangle(10, 10, 80, 80), 1, form, "outer", false),
                new ContextHelpTargetResolver.HighlightTarget(new Rectangle(30, 30, 20, 20), 2, helpButton, "inner", false)
            };

            var result = resolver.TryGetHighlightAt(targets, screenPoint);

            Assert.NotNull(result);
            Assert.Equal("inner", result.Value.HelpText);
        });
    }

    [Fact]
    public void ResolveHitTarget_UsesAncestorFallbackWhenNoExplicitHighlightMatches()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(220, 180)
            };
            var helpButton = new ContextHelpButton
            {
                Location = new Point(180, 8),
                Size = new Size(28, 28)
            };
            var panel = new Panel
            {
                Location = new Point(16, 16),
                Size = new Size(120, 80)
            };
            var child = new Button
            {
                Location = new Point(10, 10),
                Size = new Size(60, 24)
            };
            panel.Controls.Add(child);
            form.Controls.Add(helpButton);
            form.Controls.Add(panel);
            StaTestHelper.CreateControlTree(form);

            var registry = new ContextHelpRegistry();
            registry.SetContextHelp(panel, "panel-help");

            using var overlay = new ContextHelpOverlay();
            var resolver = new ContextHelpTargetResolver(form, helpButton, overlay, registry);

            var target = resolver.ResolveHitTarget(child.PointToScreen(new Point(5, 5)));

            Assert.NotNull(target);
            Assert.Equal("panel-help", target.HelpText);
            Assert.Same(child, target.AnchorControl);
        });
    }
}
