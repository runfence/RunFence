using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpSelectionSessionTests
{
    [Fact]
    public void EnterFromButton_ReleaseOnButton_KeepsHelpModeActive()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var button = new ContextHelpButton
            {
                Location = new Point(20, 20),
                Size = new Size(28, 28)
            };
            form.Controls.Add(button);
            StaTestHelper.CreateControlTree(form);

            var session = new ContextHelpSelectionSession(form, button);
            var pointOnButton = button.PointToScreen(new Point(10, 10));

            session.EnterFromButton(pointOnButton);

            Assert.True(session.IsActive);
            Assert.True(session.MouseSelectionInProgress);
            Assert.True(session.ShouldKeepHelpModeActive(pointOnButton));
            Assert.True(session.CompleteMouseSelection(pointOnButton));
            Assert.False(session.MouseSelectionInProgress);
        });
    }

    [Fact]
    public void UpdateButtonDragState_AwayFromButton_ClearsKeepActiveDecision()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var button = new ContextHelpButton
            {
                Location = new Point(20, 20),
                Size = new Size(28, 28)
            };
            form.Controls.Add(button);
            StaTestHelper.CreateControlTree(form);

            var session = new ContextHelpSelectionSession(form, button);
            var startPoint = button.PointToScreen(new Point(10, 10));

            session.EnterFromButton(startPoint);
            session.UpdateButtonDragState(new Point(startPoint.X + 30, startPoint.Y + 30));

            Assert.False(session.ShouldKeepHelpModeActive(startPoint));
            Assert.False(session.CompleteMouseSelection(startPoint));
        });
    }

    [Fact]
    public void Exit_ClearsActiveAndSelectionState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm();
            var button = new ContextHelpButton
            {
                Location = new Point(20, 20),
                Size = new Size(28, 28)
            };
            form.Controls.Add(button);
            StaTestHelper.CreateControlTree(form);

            var session = new ContextHelpSelectionSession(form, button);
            session.EnterFromButton(button.PointToScreen(new Point(1, 1)));
            session.BeginOverlaySelection();

            session.Exit();

            Assert.False(session.IsActive);
            Assert.False(session.MouseSelectionInProgress);
            Assert.False(session.ShouldKeepHelpModeActive(button.PointToScreen(new Point(1, 1))));
        });
    }
}
