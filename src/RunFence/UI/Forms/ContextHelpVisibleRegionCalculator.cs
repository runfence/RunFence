namespace RunFence.UI.Forms;

public sealed class ContextHelpVisibleRegionCalculator
{
    private readonly ContextHelpForm _form;
    private readonly Control? _ignoredControl;

    public ContextHelpVisibleRegionCalculator(ContextHelpForm form, Control? ignoredControl = null)
    {
        _form = form;
        _ignoredControl = ignoredControl;
    }

    public Rectangle? GetVisibleRectangle(Control targetOwner, Rectangle targetRect)
    {
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
            return null;

        var visibleRect = targetRect;
        for (var current = targetOwner; current != null && current != _form; current = current.Parent)
        {
            if (current.Parent == null)
                break;

            var ancestorClientRect = _form.RectangleToClient(current.Parent.RectangleToScreen(current.Parent.ClientRectangle));
            visibleRect = Rectangle.Intersect(visibleRect, ancestorClientRect);
            if (visibleRect.Width <= 0 || visibleRect.Height <= 0)
                return null;

            var occludingSiblings = GetHigherZOrderSiblings(current);
            foreach (var sibling in occludingSiblings)
            {
                var siblingRect = _form.RectangleToClient(sibling.RectangleToScreen(sibling.ClientRectangle));
                var trimmedRect = SubtractOverlappingRectangle(visibleRect, siblingRect);
                if (trimmedRect == null)
                    return null;

                visibleRect = trimmedRect.Value;
            }
        }

        visibleRect = Rectangle.Intersect(visibleRect, _form.ClientRectangle);
        return visibleRect.Width > 0 && visibleRect.Height > 0
            ? visibleRect
            : null;
    }

    private IReadOnlyList<Control> GetHigherZOrderSiblings(Control control)
    {
        var parent = control.Parent;
        if (parent == null)
            return [];

        var siblings = parent.Controls;
        var controlZIndex = siblings.GetChildIndex(control, throwException: false);
        if (controlZIndex < 0)
            return [];

        var higherSiblings = new List<Control>();

        for (var zIndex = 0; zIndex < controlZIndex; zIndex++)
        {
            var sibling = siblings[zIndex];
            if (!IsVisibleWithinForm(sibling) || sibling.Width <= 0 || sibling.Height <= 0 || sibling == _ignoredControl)
                continue;

            higherSiblings.Add(sibling);
        }

        return higherSiblings;
    }

    private bool IsVisibleWithinForm(Control control)
    {
        for (var current = control; current != null && current != _form; current = current.Parent)
        {
            if (current.Parent != _form && !current.Visible)
                return false;
        }

        return true;
    }

    private static Rectangle? SubtractOverlappingRectangle(Rectangle sourceRect, Rectangle occluderRect)
    {
        var overlap = Rectangle.Intersect(sourceRect, occluderRect);
        if (overlap.Width <= 0 || overlap.Height <= 0)
            return sourceRect;

        var candidates = new[]
        {
            Rectangle.FromLTRB(sourceRect.Left, sourceRect.Top, overlap.Left, sourceRect.Bottom),
            Rectangle.FromLTRB(overlap.Right, sourceRect.Top, sourceRect.Right, sourceRect.Bottom),
            Rectangle.FromLTRB(sourceRect.Left, sourceRect.Top, sourceRect.Right, overlap.Top),
            Rectangle.FromLTRB(sourceRect.Left, overlap.Bottom, sourceRect.Right, sourceRect.Bottom)
        };

        Rectangle? bestCandidate = null;
        var bestArea = -1;
        foreach (var candidate in candidates)
        {
            if (candidate.Width <= 0 || candidate.Height <= 0)
                continue;

            var area = candidate.Width * candidate.Height;
            if (area <= bestArea)
                continue;

            bestArea = area;
            bestCandidate = candidate;
        }

        return bestCandidate;
    }
}
