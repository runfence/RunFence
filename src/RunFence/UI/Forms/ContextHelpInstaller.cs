namespace RunFence.UI.Forms;

public class ContextHelpInstaller
{
    private const string ContextHelpButtonName = "_contextHelpButton";
    private const int ContextHelpButtonSize = 29;
    private const int DockedStripVerticalPadding = 2;
    private const int TopMargin = 0;
    private const int RightMargin = 0;

    public ContextHelpController Attach(ContextHelpForm form, ContextHelpRegistry registry)
    {
        if (form.Controls.Find(ContextHelpButtonName, true).Length > 0)
        {
            var existingButton = form.Controls.Find(ContextHelpButtonName, true).OfType<ContextHelpButton>().First();
            return CreateController(form, registry, existingButton);
        }

        if (UsesDockedRootLayout(form))
            return AttachDockedStrip(form, registry);

        return AttachOverlayButton(form, registry);
    }

    private bool UsesDockedRootLayout(Form form)
    {
        var rootControls = form.Controls.Cast<Control>()
            .Where(static control => control.Visible)
            .ToList();

        return rootControls.Count > 0 && rootControls.All(static control => control.Dock != DockStyle.None);
    }

    private ContextHelpController AttachDockedStrip(ContextHelpForm form, ContextHelpRegistry registry)
    {
        var scaledDockedStripVerticalPadding = Scale(form, DockedStripVerticalPadding);
        var scaledTopMargin = Scale(form, TopMargin);
        var scaledRightMargin = Scale(form, RightMargin);
        var scaledContextHelpButtonSize = Scale(form, ContextHelpButtonSize);

        var strip = new Panel
        {
            Dock = DockStyle.Top,
            Height = scaledContextHelpButtonSize + (scaledDockedStripVerticalPadding * 2),
            Padding = new Padding(0, scaledDockedStripVerticalPadding + scaledTopMargin, scaledRightMargin, scaledDockedStripVerticalPadding + scaledTopMargin),
            Margin = Padding.Empty,
            TabStop = false,
            BackColor = form.BackColor
        };

        var button = CreateButton(form);
        button.Dock = DockStyle.Right;
        strip.Controls.Add(button);
        form.SetContextHelp(button, ContextHelpTextResolver.InstructionText);

        form.Controls.Add(strip);
        strip.BringToFront();
        return CreateController(form, registry, button);
    }

    private ContextHelpController AttachOverlayButton(ContextHelpForm form, ContextHelpRegistry registry)
    {
        var scaledTopMargin = Scale(form, TopMargin);
        var scaledRightMargin = Scale(form, RightMargin);
        var button = CreateButton(form);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Location = new Point(
            Math.Max(0, form.ClientSize.Width - button.Width - scaledRightMargin),
            scaledTopMargin);
        form.SetContextHelp(button, ContextHelpTextResolver.InstructionText);
        form.Controls.Add(button);
        button.BringToFront();
        return CreateController(form, registry, button);
    }

    private ContextHelpButton CreateButton(ContextHelpForm form)
    {
        return new ContextHelpButton
        {
            Name = ContextHelpButtonName,
            Size = new Size(Scale(form, ContextHelpButtonSize), Scale(form, ContextHelpButtonSize)),
            TabStop = false,
            AccessibleName = "Context help"
        };
    }

    private ContextHelpController CreateController(ContextHelpForm form, ContextHelpRegistry registry, ContextHelpButton button)
    {
        var overlay = new ContextHelpOverlay();
        var targetResolver = new ContextHelpTargetResolver(form, button, overlay, registry);
        var snapshotRenderer = new ContextHelpSnapshotRenderer();
        var popupPresenter = new ContextHelpPopupPresenter(form);
        var selectionSession = new ContextHelpSelectionSession(form, button);
        var messageFilterRegistration = new ContextHelpMessageFilterRegistration();
        ContextHelpController? controller = null;
        var modeCoordinator = new ContextHelpModeCoordinator(
            form,
            button,
            overlay,
            targetResolver,
            snapshotRenderer,
            popupPresenter,
            selectionSession,
            ensureMessageFilterInstalled: () => messageFilterRegistration.EnsureInstalled(controller!),
            removeMessageFilter: () => messageFilterRegistration.Remove(controller!));
        controller = new ContextHelpController(
            form,
            button,
            overlay,
            modeCoordinator,
            removeMessageFilter: () => messageFilterRegistration.Remove(controller!));
        return controller;
    }

    private static int Scale(ContextHelpForm form, int logicalPixels) => form.ScaleHelpLogicalPixels(logicalPixels);

    private sealed class ContextHelpMessageFilterRegistration
    {
        private bool _installed;

        public void EnsureInstalled(IMessageFilter filter)
        {
            if (_installed)
                return;

            Application.AddMessageFilter(filter);
            _installed = true;
        }

        public void Remove(IMessageFilter filter)
        {
            if (!_installed)
                return;

            Application.RemoveMessageFilter(filter);
            _installed = false;
        }
    }
}
