using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpRegistryTests
{
    [Fact]
    public void SetContextHelp_TracksAllSupportedTargetKinds_AndReportsTargetsPresent()
    {
        var registry = new ContextHelpRegistry();
        using var control = new Button();
        using var strip = new ToolStrip();
        using var dropDown = new ContextMenuStrip();
        using var page = new TabPage();
        var item = new ToolStripButton("Action");

        registry.SetContextHelp(control, "control-help");
        registry.SetContextHelp(item, "item-help");
        registry.SetContextHelp(dropDown, "drop-down-help");
        registry.SetContextHelp(page, "page-help");

        Assert.True(registry.HasAnyContextHelpTargets());
        Assert.True(registry.TryGetContextHelp(control, out var controlText));
        Assert.Equal("control-help", controlText);
        Assert.True(registry.TryGetContextHelp(item, out var itemText));
        Assert.Equal("item-help", itemText);
        Assert.True(registry.TryGetContextHelp(dropDown, out var dropDownText));
        Assert.Equal("drop-down-help", dropDownText);
        Assert.True(registry.TryGetContextHelp(page, out var pageText));
        Assert.Equal("page-help", pageText);
    }

    [Fact]
    public void SnapshotParticipants_RegisterOnce_AndCanBeRemoved()
    {
        var registry = new ContextHelpRegistry();
        var participant = new TestSnapshotParticipant();

        registry.RegisterSnapshotParticipant(participant);
        registry.RegisterSnapshotParticipant(participant);

        Assert.Single(registry.GetSnapshotParticipants());

        registry.UnregisterSnapshotParticipant(participant);

        Assert.Empty(registry.GetSnapshotParticipants());
    }

    private sealed class TestSnapshotParticipant : IContextHelpSnapshotParticipant
    {
        public void PrepareForContextHelpSnapshot()
        {
        }
    }
}
