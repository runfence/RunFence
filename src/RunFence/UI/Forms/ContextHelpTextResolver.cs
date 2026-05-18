namespace RunFence.UI.Forms;

public static class ContextHelpTextResolver
{
    public const string InstructionText = "Click on elements to learn about them.\r\nPress Esc to cancel.";

    public static ContextHelpHitTarget? Resolve(
        ContextHelpForm form,
        ContextHelpButton helpButton,
        Control control,
        Point screenPoint)
    {
        return ResolveCore(
            form,
            helpButton,
            control,
            screenPoint,
            tryGetControlText: current => form.TryGetContextHelp(current, out var text) ? text : null,
            tryGetToolStripItemText: item => form.TryGetContextHelp(item, out var text) ? text : null,
            tryGetDropDownText: dropDown => form.TryGetContextHelp(dropDown, out var text) ? text : null,
            tryGetTabPageText: page => form.TryGetContextHelp(page, out var text) ? text : null);
    }

    public static ContextHelpHitTarget? Resolve(
        ContextHelpRegistry registry,
        ContextHelpForm form,
        ContextHelpButton helpButton,
        Control control,
        Point screenPoint)
    {
        return ResolveCore(
            form,
            helpButton,
            control,
            screenPoint,
            tryGetControlText: current => registry.TryGetContextHelp(current, out var text) ? text : null,
            tryGetToolStripItemText: item => registry.TryGetContextHelp(item, out var text) ? text : null,
            tryGetDropDownText: dropDown => registry.TryGetContextHelp(dropDown, out var text) ? text : null,
            tryGetTabPageText: page => registry.TryGetContextHelp(page, out var text) ? text : null);
    }

    private static ContextHelpHitTarget? ResolveCore(
        ContextHelpForm form,
        ContextHelpButton helpButton,
        Control control,
        Point screenPoint,
        Func<Control, string?> tryGetControlText,
        Func<ToolStripItem, string?> tryGetToolStripItemText,
        Func<ToolStripDropDown, string?> tryGetDropDownText,
        Func<TabPage, string?> tryGetTabPageText)
    {
        if (control is ContextHelpButton)
        {
            return new ContextHelpHitTarget(
                helpButton,
                GetButtonAnchorPoint(form, helpButton),
                InstructionText,
                showInstructionsOnButton: true);
        }

        if (TryResolveToolStripTarget(form, control, screenPoint, tryGetToolStripItemText, tryGetDropDownText, out var toolStripTarget))
            return toolStripTarget;

        if (TryResolveTabTarget(form, control, screenPoint, tryGetTabPageText, out var tabTarget))
            return tabTarget;

        return TryFindExplicitHelp(form, control, tryGetControlText, out var helpText)
            ? CreateControlTarget(form, control, helpText, screenPoint)
            : null;
    }

    private static bool TryFindExplicitHelp(
        ContextHelpForm form,
        Control control,
        Func<Control, string?> tryGetControlText,
        out string helpText)
    {
        for (var current = control; current != null && current != form; current = current.Parent)
        {
            var registeredText = tryGetControlText(current);
            if (!string.IsNullOrWhiteSpace(registeredText))
            {
                helpText = registeredText;
                return true;
            }
        }

        helpText = null!;
        return false;
    }

    private static ContextHelpHitTarget CreateControlTarget(ContextHelpForm form, Control control, string helpText, Point screenPoint)
    {
        var anchorPoint = GetCursorAnchorPoint(form, control, screenPoint);
        return new ContextHelpHitTarget(control, anchorPoint, helpText);
    }

    private static bool TryResolveToolStripTarget(
        ContextHelpForm form,
        Control control,
        Point screenPoint,
        Func<ToolStripItem, string?> tryGetToolStripItemText,
        Func<ToolStripDropDown, string?> tryGetDropDownText,
        out ContextHelpHitTarget target)
    {
        var toolStrip = control as ToolStrip ?? control.Parent as ToolStrip;
        if (toolStrip == null)
        {
            target = null!;
            return false;
        }

        var item = toolStrip.GetItemAt(toolStrip.PointToClient(screenPoint));
        if (item == null)
        {
            target = null!;
            return false;
        }

        var helpText = tryGetToolStripItemText(item);
        if (string.IsNullOrWhiteSpace(helpText) && toolStrip is ToolStripDropDown dropDown)
            helpText = tryGetDropDownText(dropDown);
        if (string.IsNullOrWhiteSpace(helpText))
        {
            target = null!;
            return false;
        }

        var anchorPoint = GetCursorAnchorPoint(form, toolStrip, screenPoint);
        target = new ContextHelpHitTarget(toolStrip, anchorPoint, helpText);
        return true;
    }

    private static bool TryResolveTabTarget(
        ContextHelpForm form,
        Control control,
        Point screenPoint,
        Func<TabPage, string?> tryGetTabPageText,
        out ContextHelpHitTarget target)
    {
        var tabControl = control as TabControl ?? control.Parent as TabControl;
        if (tabControl == null)
        {
            target = null!;
            return false;
        }

        var localPoint = tabControl.PointToClient(screenPoint);
        for (var i = 0; i < tabControl.TabPages.Count; i++)
        {
            var tabRect = tabControl.GetTabRect(i);
            if (!tabRect.Contains(localPoint))
                continue;

            var page = tabControl.TabPages[i];
            var helpText = tryGetTabPageText(page);
            if (string.IsNullOrWhiteSpace(helpText))
                continue;

            var anchorPoint = GetCursorAnchorPoint(form, tabControl, screenPoint);
            target = new ContextHelpHitTarget(tabControl, anchorPoint, helpText);
            return true;
        }

        target = null!;
        return false;
    }

    private static Point GetCursorAnchorPoint(ContextHelpForm form, Control anchorControl, Point screenPoint)
    {
        var localPoint = anchorControl.PointToClient(screenPoint);
        var offsetX = form.ScaleHelpLogicalPixels(12);
        var offsetY = form.ScaleHelpLogicalPixels(18);

        return new Point(
            Math.Max(0, Math.Min(anchorControl.Width - 1, localPoint.X + offsetX)),
            Math.Max(0, Math.Min(anchorControl.Height - 1, localPoint.Y + offsetY)));
    }

    private static Point GetButtonAnchorPoint(ContextHelpForm form, Control button)
    {
        return new Point(
            Math.Max(0, button.Width - form.ScaleHelpLogicalPixels(4)),
            button.Height);
    }

}
