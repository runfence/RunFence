using System.ComponentModel;

namespace RunFence.UI.Forms;

public class ContextHelpForm : Form
{
    private readonly ContextHelpInstaller _contextHelpInstaller = new();
    private readonly ContextHelpRegistry _contextHelpRegistry = new();
    private ContextHelpController? _contextHelpController;
    private bool _contextHelpInstalled;
    private bool _contextHelpInstalling;
    private bool _contextHelpInitialized;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _contextHelpInitialized = true;
        EnsureContextHelpInstalled();
    }

    private void EnsureContextHelpInstalled()
    {
        if (_contextHelpInstalled || _contextHelpInstalling)
            return;

        if (!_contextHelpInitialized || IsInDesigner())
            return;

        if (!_contextHelpRegistry.HasAnyContextHelpTargets() || !IsHandleCreated || IsDisposed)
            return;

        _contextHelpInstalling = true;
        try
        {
            _contextHelpController = _contextHelpInstaller.Attach(this, _contextHelpRegistry);
            _contextHelpInstalled = true;
        }
        finally
        {
            _contextHelpInstalling = false;
        }
    }

    public int ScaleHelpLogicalPixels(int logicalPixels) => LogicalToDeviceUnits(logicalPixels);

    public void SetContextHelp(Control control, string text)
    {
        _contextHelpRegistry.SetContextHelp(control, text);
        EnsureContextHelpInstalled();
    }

    public bool TryGetContextHelp(Control control, out string? text) => _contextHelpRegistry.TryGetContextHelp(control, out text);

    public void SetContextHelp(ToolStripItem item, string text)
    {
        _contextHelpRegistry.SetContextHelp(item, text);
        EnsureContextHelpInstalled();
    }

    public bool TryGetContextHelp(ToolStripItem item, out string? text) => _contextHelpRegistry.TryGetContextHelp(item, out text);

    public void SetContextHelp(ToolStripDropDown dropDown, string text)
    {
        _contextHelpRegistry.SetContextHelp(dropDown, text);
        EnsureContextHelpInstalled();
    }

    public bool TryGetContextHelp(ToolStripDropDown dropDown, out string? text) => _contextHelpRegistry.TryGetContextHelp(dropDown, out text);

    public void SetContextHelp(TabPage page, string text)
    {
        _contextHelpRegistry.SetContextHelp(page, text);
        EnsureContextHelpInstalled();
    }

    public bool TryGetContextHelp(TabPage page, out string? text) => _contextHelpRegistry.TryGetContextHelp(page, out text);

    public IReadOnlyCollection<Control> GetExplicitContextHelpControls() => _contextHelpRegistry.GetExplicitContextHelpControls();
    public IReadOnlyCollection<ToolStripItem> GetExplicitContextHelpToolStripItems() => _contextHelpRegistry.GetExplicitContextHelpToolStripItems();
    public IReadOnlyCollection<ToolStripDropDown> GetExplicitContextHelpToolStripDropDowns() => _contextHelpRegistry.GetExplicitContextHelpToolStripDropDowns();
    public IReadOnlyCollection<TabPage> GetExplicitContextHelpTabPages() => _contextHelpRegistry.GetExplicitContextHelpTabPages();
    public void RegisterContextHelpSnapshotParticipant(IContextHelpSnapshotParticipant participant) => _contextHelpRegistry.RegisterSnapshotParticipant(participant);
    public void UnregisterContextHelpSnapshotParticipant(IContextHelpSnapshotParticipant participant) => _contextHelpRegistry.UnregisterSnapshotParticipant(participant);
    public IReadOnlyCollection<IContextHelpSnapshotParticipant> GetContextHelpSnapshotParticipants() => _contextHelpRegistry.GetSnapshotParticipants();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _contextHelpController?.Dispose();
        _contextHelpController = null;
        _contextHelpInstalled = false;
        base.OnFormClosed(e);
    }

    private bool IsInDesigner()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            return true;

        return Site?.DesignMode == true;
    }
}
