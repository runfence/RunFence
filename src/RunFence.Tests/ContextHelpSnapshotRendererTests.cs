using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpSnapshotRendererTests
{
    [Fact]
    public void CaptureFormSnapshot_PreparesParticipantsAndRestoresOverlayAndButtonVisibility()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(240, 160)
            };
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.LightSteelBlue
            };
            var button = new ContextHelpButton
            {
                Location = new Point(200, 8),
                Size = new Size(28, 28),
                Visible = false
            };
            using var overlay = new ContextHelpOverlay
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(content);
            form.Controls.Add(button);
            form.Controls.Add(overlay);
            StaTestHelper.CreateControlTree(form);

            var participant = new TestSnapshotParticipant();
            var renderer = new ContextHelpSnapshotRenderer();
            var initialButtonVisible = button.Visible;
            var initialOverlayVisible = overlay.Visible;

            using var snapshot = renderer.CaptureFormSnapshot(form, button, overlay, [participant]);

            Assert.Equal(1, participant.PrepareCalls);
            Assert.Equal(initialButtonVisible, button.Visible);
            Assert.Equal(initialOverlayVisible, overlay.Visible);
            Assert.NotNull(snapshot);
            Assert.Equal(form.ClientSize.Width, snapshot.Width);
            Assert.Equal(form.ClientSize.Height, snapshot.Height);
        });
    }

    [Fact]
    public void CaptureFormSnapshot_AllowsParticipantsToRedactBeforeCapture()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(240, 160)
            };
            var textBox = new TextBox
            {
                Location = new Point(16, 16),
                Width = 120,
                PasswordChar = '\0'
            };
            var participant = new DelegateSnapshotParticipant(() =>
            {
                textBox.PasswordChar = '*';
            });

            var helpButton = new ContextHelpButton
            {
                Location = new Point(200, 8),
                Size = new Size(28, 28)
            };
            using var overlay = new ContextHelpOverlay
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(textBox);
            form.Controls.Add(helpButton);
            form.Controls.Add(overlay);
            StaTestHelper.CreateControlTree(form);

            var renderer = new ContextHelpSnapshotRenderer();

            using var snapshot = renderer.CaptureFormSnapshot(form, helpButton, overlay, [participant]);

            Assert.Equal('*', textBox.PasswordChar);
            Assert.NotNull(snapshot);
        });
    }

    [Fact]
    public void CaptureFormSnapshot_DoesNotMutatePasswordTextBoxesWithoutParticipants()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(240, 160)
            };
            var textBox = new TextBox
            {
                Location = new Point(16, 16),
                Width = 120,
                PasswordChar = '\0',
                Text = "visible-password"
            };
            var helpButton = new ContextHelpButton
            {
                Location = new Point(200, 8),
                Size = new Size(28, 28)
            };
            using var overlay = new ContextHelpOverlay
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(textBox);
            form.Controls.Add(helpButton);
            form.Controls.Add(overlay);
            StaTestHelper.CreateControlTree(form);

            var renderer = new ContextHelpSnapshotRenderer();

            using var snapshot = renderer.CaptureFormSnapshot(form, helpButton, overlay, []);

            Assert.Equal('\0', textBox.PasswordChar);
            Assert.Equal("visible-password", textBox.Text);
            Assert.NotNull(snapshot);
        });
    }

    private sealed class TestSnapshotParticipant : IContextHelpSnapshotParticipant
    {
        public int PrepareCalls { get; private set; }

        public void PrepareForContextHelpSnapshot()
        {
            PrepareCalls++;
        }
    }

    private sealed class DelegateSnapshotParticipant : IContextHelpSnapshotParticipant
    {
        private readonly Action _onPrepare;

        public DelegateSnapshotParticipant(Action onPrepare)
        {
            _onPrepare = onPrepare;
        }

        public void PrepareForContextHelpSnapshot()
        {
            _onPrepare();
        }
    }
}
