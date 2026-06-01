using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpRegistryTests
{
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
