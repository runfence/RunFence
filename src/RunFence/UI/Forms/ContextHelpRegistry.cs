namespace RunFence.UI.Forms;

public sealed class ContextHelpRegistry
{
    private readonly Dictionary<Control, string> _controlTexts = [];
    private readonly Dictionary<ToolStripItem, string> _toolStripItemTexts = [];
    private readonly Dictionary<ToolStripDropDown, string> _toolStripDropDownTexts = [];
    private readonly Dictionary<TabPage, string> _tabPageTexts = [];
    private readonly List<IContextHelpSnapshotParticipant> _snapshotParticipants = [];

    public void SetContextHelp(Control control, string text)
    {
        ArgumentNullException.ThrowIfNull(control);
        _controlTexts[control] = NormalizeText(text);
    }

    public bool TryGetContextHelp(Control control, out string? text)
    {
        if (_controlTexts.TryGetValue(control, out var stored))
        {
            text = stored;
            return true;
        }

        text = null;
        return false;
    }

    public void SetContextHelp(ToolStripItem item, string text)
    {
        ArgumentNullException.ThrowIfNull(item);
        _toolStripItemTexts[item] = NormalizeText(text);
    }

    public bool TryGetContextHelp(ToolStripItem item, out string? text)
    {
        if (_toolStripItemTexts.TryGetValue(item, out var stored))
        {
            text = stored;
            return true;
        }

        text = null;
        return false;
    }

    public void SetContextHelp(ToolStripDropDown dropDown, string text)
    {
        ArgumentNullException.ThrowIfNull(dropDown);
        _toolStripDropDownTexts[dropDown] = NormalizeText(text);
    }

    public bool TryGetContextHelp(ToolStripDropDown dropDown, out string? text)
    {
        if (_toolStripDropDownTexts.TryGetValue(dropDown, out var stored))
        {
            text = stored;
            return true;
        }

        text = null;
        return false;
    }

    public void SetContextHelp(TabPage page, string text)
    {
        ArgumentNullException.ThrowIfNull(page);
        _tabPageTexts[page] = NormalizeText(text);
    }

    public bool TryGetContextHelp(TabPage page, out string? text)
    {
        if (_tabPageTexts.TryGetValue(page, out var stored))
        {
            text = stored;
            return true;
        }

        text = null;
        return false;
    }

    public IReadOnlyCollection<Control> GetExplicitContextHelpControls() => _controlTexts.Keys.ToList();
    public IReadOnlyCollection<ToolStripItem> GetExplicitContextHelpToolStripItems() => _toolStripItemTexts.Keys.ToList();
    public IReadOnlyCollection<ToolStripDropDown> GetExplicitContextHelpToolStripDropDowns() => _toolStripDropDownTexts.Keys.ToList();
    public IReadOnlyCollection<TabPage> GetExplicitContextHelpTabPages() => _tabPageTexts.Keys.ToList();

    public void RegisterSnapshotParticipant(IContextHelpSnapshotParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (!_snapshotParticipants.Contains(participant))
            _snapshotParticipants.Add(participant);
    }

    public void UnregisterSnapshotParticipant(IContextHelpSnapshotParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _snapshotParticipants.Remove(participant);
    }

    public IReadOnlyCollection<IContextHelpSnapshotParticipant> GetSnapshotParticipants() => _snapshotParticipants.ToList();

    public bool HasAnyContextHelpTargets() =>
        _controlTexts.Count > 0 ||
        _toolStripItemTexts.Count > 0 ||
        _toolStripDropDownTexts.Count > 0 ||
        _tabPageTexts.Count > 0;

    private static string NormalizeText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text.Trim();
    }
}
