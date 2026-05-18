namespace RunFence.UI.Forms;

public sealed class ContextHelpTargetResolver
{
    private readonly ContextHelpForm _form;
    private readonly ContextHelpButton _button;
    private readonly ContextHelpOverlay _overlay;
    private readonly ContextHelpRegistry _registry;

    public ContextHelpTargetResolver(
        ContextHelpForm form,
        ContextHelpButton button,
        ContextHelpOverlay overlay,
        ContextHelpRegistry registry)
    {
        _form = form;
        _button = button;
        _overlay = overlay;
        _registry = registry;
    }

    public readonly record struct HighlightTarget(
        Rectangle Rect,
        int Depth,
        Control AnchorControl,
        string HelpText,
        bool ShowInstructionsOnButton);

    public IReadOnlyList<HighlightTarget> GetHighlightTargets()
    {
        var targets = new List<HighlightTarget>();
        var visibleRegionCalculator = new ContextHelpVisibleRegionCalculator(_form, _overlay);

        foreach (var control in _registry.GetExplicitContextHelpControls())
        {
            if (control.IsDisposed || control.FindForm() != _form || !IsEligibleControlTarget(control))
                continue;

            var rect = _form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            var visibleRect = visibleRegionCalculator.GetVisibleRectangle(control, rect);
            if (visibleRect == null || !_registry.TryGetContextHelp(control, out var helpText) || string.IsNullOrWhiteSpace(helpText))
                continue;

            targets.Add(new HighlightTarget(
                visibleRect.Value,
                GetControlDepth(control),
                control,
                helpText,
                control == _button));
        }

        foreach (var item in _registry.GetExplicitContextHelpToolStripItems())
        {
            if (item.Owner is not ToolStrip owner ||
                (_form.Visible && !owner.Visible) ||
                !item.Available ||
                item.Bounds.Width <= 0 ||
                item.Bounds.Height <= 0)
                continue;

            var rect = _form.RectangleToClient(owner.RectangleToScreen(item.Bounds));
            var visibleRect = visibleRegionCalculator.GetVisibleRectangle(owner, rect);
            if (visibleRect == null || !_registry.TryGetContextHelp(item, out var helpText) || string.IsNullOrWhiteSpace(helpText))
                continue;

            targets.Add(new HighlightTarget(
                visibleRect.Value,
                GetControlDepth(owner) + 1,
                owner,
                helpText,
                false));
        }

        foreach (var page in _registry.GetExplicitContextHelpTabPages())
        {
            if (page.Parent is not TabControl owner || (_form.Visible && !owner.Visible))
                continue;

            var index = owner.TabPages.IndexOf(page);
            if (index < 0)
                continue;

            var rect = _form.RectangleToClient(owner.RectangleToScreen(owner.GetTabRect(index)));
            var visibleRect = visibleRegionCalculator.GetVisibleRectangle(owner, rect);
            if (visibleRect == null || !_registry.TryGetContextHelp(page, out var helpText) || string.IsNullOrWhiteSpace(helpText))
                continue;

            targets.Add(new HighlightTarget(
                visibleRect.Value,
                GetControlDepth(owner) + 1,
                owner,
                helpText,
                false));
        }

        return targets;
    }

    public HighlightTarget? TryGetHighlightAt(IReadOnlyList<HighlightTarget> highlightTargets, Point screenPoint)
    {
        var overlayPoint = _overlay.PointToClient(screenPoint);
        HighlightTarget? bestTarget = null;
        foreach (var target in highlightTargets)
        {
            if (!target.Rect.Contains(overlayPoint))
                continue;

            if (bestTarget == null || target.Depth > bestTarget.Value.Depth)
                bestTarget = target;
        }

        return bestTarget;
    }

    public ContextHelpHitTarget CreateHitTarget(HighlightTarget target, Point screenPoint)
    {
        if (target.ShowInstructionsOnButton)
        {
            return new ContextHelpHitTarget(
                target.AnchorControl,
                new Point(Math.Max(0, target.AnchorControl.Width - _form.ScaleHelpLogicalPixels(4)), target.AnchorControl.Height),
                target.HelpText,
                showInstructionsOnButton: true);
        }

        var localPoint = target.AnchorControl.PointToClient(screenPoint);
        var offsetX = _form.ScaleHelpLogicalPixels(12);
        var offsetY = _form.ScaleHelpLogicalPixels(18);
        var anchorPoint = new Point(
            Math.Max(0, Math.Min(target.AnchorControl.Width - 1, localPoint.X + offsetX)),
            Math.Max(0, Math.Min(target.AnchorControl.Height - 1, localPoint.Y + offsetY)));

        return new ContextHelpHitTarget(target.AnchorControl, anchorPoint, target.HelpText);
    }

    public ContextHelpHitTarget? ResolveHitTarget(Point screenPoint)
    {
        if (!IsPointInsideForm(screenPoint))
            return null;

        var control = FindDeepestVisibleChild(_form, screenPoint, _overlay);
        var highlightTarget = TryGetHighlightAt(GetHighlightTargets(), screenPoint);
        if (highlightTarget != null &&
            (highlightTarget.Value.ShowInstructionsOnButton ||
             ReferenceEquals(highlightTarget.Value.AnchorControl, control)))
        {
            return CreateHitTarget(highlightTarget.Value, screenPoint);
        }

        return ContextHelpTextResolver.Resolve(_registry, _form, _button, control, screenPoint);
    }

    private static Control FindDeepestVisibleChild(Control root, Point screenPoint, Control? ignoredControl = null)
    {
        var form = root.FindForm();
        var localPoint = root.PointToClient(screenPoint);
        foreach (Control child in root.Controls.Cast<Control>().Reverse())
        {
            var isVisible = form?.Visible == false
                ? child.Parent != null
                : child.Visible;
            if (child == ignoredControl || !isVisible)
                continue;

            if (!child.Bounds.Contains(localPoint))
                continue;

            return FindDeepestVisibleChild(child, screenPoint, ignoredControl);
        }

        return root;
    }

    private bool IsPointInsideForm(Point screenPoint) =>
        _form.RectangleToScreen(_form.ClientRectangle).Contains(screenPoint);

    private bool IsEligibleControlTarget(Control control)
    {
        if (!_form.Visible)
            return control == _button || control.Parent != null;

        return control.Visible || control == _button;
    }

    private int GetControlDepth(Control control)
    {
        var depth = 0;
        for (var current = control; current != null && current != _form; current = current.Parent)
            depth++;

        return depth;
    }
}
