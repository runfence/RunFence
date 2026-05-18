using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ContextHelpVisibleRegionCalculatorTests
{
    [Fact]
    public void GetVisibleRectangle_AncestorKeepsVisibleChildAreaWhileTrimmingToFrontSiblingEdge()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(220, 180)
            };
            var ancestor = new Panel
            {
                Location = new Point(10, 10),
                Size = new Size(140, 120)
            };
            var child = new Button
            {
                Location = new Point(20, 20),
                Size = new Size(60, 30)
            };
            var frontSibling = new Panel
            {
                Location = new Point(70, 25),
                Size = new Size(80, 60)
            };

            ancestor.Controls.Add(child);
            form.Controls.Add(ancestor);
            form.Controls.Add(frontSibling);
            form.Controls.SetChildIndex(frontSibling, 0);
            StaTestHelper.CreateControlTree(form);
            RemoveContextHelpButtons(form);

            var calculator = new ContextHelpVisibleRegionCalculator(form);
            var ancestorRect = form.RectangleToClient(ancestor.RectangleToScreen(ancestor.ClientRectangle));
            var childRect = form.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
            var frontSiblingRect = form.RectangleToClient(frontSibling.RectangleToScreen(frontSibling.ClientRectangle));

            var ancestorVisibleRect = calculator.GetVisibleRectangle(ancestor, ancestorRect);
            var childVisibleRect = calculator.GetVisibleRectangle(child, childRect);
            Assert.NotNull(ancestorVisibleRect);
            Assert.NotNull(childVisibleRect);

            var visibleChildPoint = form.PointToClient(child.PointToScreen(new Point(10, 10)));
            var overlapRect = Rectangle.Intersect(childRect, frontSiblingRect);

            Assert.True(overlapRect.Width > 0 && overlapRect.Height > 0);
            Assert.True(ancestorVisibleRect!.Value.Contains(visibleChildPoint));
            Assert.True(childVisibleRect!.Value.Contains(visibleChildPoint));
            Assert.Equal(ancestorRect.Left, ancestorVisibleRect.Value.Left);
            Assert.Equal(overlapRect.Left, ancestorVisibleRect.Value.Right);
            Assert.Equal(childRect.Left, childVisibleRect.Value.Left);
            Assert.Equal(overlapRect.Left, childVisibleRect.Value.Right);
        });
    }

    [Fact]
    public void GetVisibleRectangle_AncestorDoesNotExcludeVisibleDescendantArea()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new ContextHelpForm
            {
                ClientSize = new Size(220, 180)
            };
            var ancestor = new Panel
            {
                Location = new Point(10, 10),
                Size = new Size(140, 120)
            };
            var child = new Button
            {
                Location = new Point(20, 20),
                Size = new Size(60, 30)
            };

            ancestor.Controls.Add(child);
            form.Controls.Add(ancestor);
            StaTestHelper.CreateControlTree(form);
            RemoveContextHelpButtons(form);

            var calculator = new ContextHelpVisibleRegionCalculator(form);
            var ancestorRect = form.RectangleToClient(ancestor.RectangleToScreen(ancestor.ClientRectangle));

            var ancestorVisibleRect = calculator.GetVisibleRectangle(ancestor, ancestorRect);
            Assert.NotNull(ancestorVisibleRect);

            var pointInsideChild = form.PointToClient(child.PointToScreen(new Point(10, 10)));
            Assert.True(ancestorVisibleRect!.Value.Contains(pointInsideChild));
        });
    }

    private static void RemoveContextHelpButtons(Control root)
    {
        foreach (var button in root.Controls.OfType<ContextHelpButton>().ToList())
        {
            root.Controls.Remove(button);
            button.Dispose();
        }
    }
}
