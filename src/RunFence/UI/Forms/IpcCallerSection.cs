using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Account;
using RunFence.RunAs.UI.Forms;

namespace RunFence.UI.Forms;

/// <summary>
/// Reusable IPC caller list editor. Used by AppEditDialog (per-app override)
/// and OptionsPanel (global list).
/// </summary>
public partial class IpcCallerSection : UserControl
{
    private readonly IWindowsAccountQueryService _accountQueryService;
    private readonly SidDisplayNameResolver _displayNameResolver;
    private readonly ISidEntryHelper _sidEntryHelper;
    private readonly IIpcCallerModalService _modalService;
    private IReadOnlyDictionary<string, string>? _sidNames;
    private Action<string, string>? _onSidNameLearned;

    /// <summary>Fired after an add or remove operation completes.</summary>
    public event Action? Changed;

    public void SetGroupTitle(string title)
    {
        _titleLabel.Text = title;
        _titleLabel.Visible = !string.IsNullOrEmpty(title);
    }

    public void SetDescription(string text)
    {
        _descLabel.Text = text;
        _descLabel.Visible = true;
    }

    public IpcCallerSection(
        IWindowsAccountQueryService accountQueryService,
        ISidEntryHelper sidEntryHelper,
        SidDisplayNameResolver displayNameResolver,
        IIpcCallerModalService modalService)
    {
        _accountQueryService = accountQueryService;
        _sidEntryHelper = sidEntryHelper;
        _displayNameResolver = displayNameResolver;
        _modalService = modalService;
        InitializeComponent();
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22));
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33));
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
    }

    public void SetSidNames(IReadOnlyDictionary<string, string>? sidNames, Action<string, string>? onSidNameLearned = null)
    {
        _sidNames = sidNames;
        _onSidNameLearned = onSidNameLearned;
    }

    public void SetCallers(List<string>? callers)
    {
        _listBox.Items.Clear();
        if (callers == null)
            return;
        foreach (var sid in callers)
            _listBox.Items.Add(new CallerDisplayItem(sid, _sidNames, _displayNameResolver));
    }

    public List<string> GetCallers()
    {
        return _listBox.Items.Cast<CallerDisplayItem>().Select(d => d.Sid).ToList();
    }

    public void SetEnabled(bool enabled)
    {
        _listBox.Enabled = enabled;
        _addButton.Enabled = enabled;
        _removeButton.Enabled = enabled && _listBox.SelectedIndex >= 0;
    }

    public void RegisterContextHelp(ContextHelpForm host, string headingHelpText)
    {
        host.SetContextHelp(this, headingHelpText);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _removeButton.Enabled = _listBox.Enabled && _listBox.SelectedIndex >= 0;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _listBox.SelectedIndex >= 0)
            OnRemoveClick(sender, e);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var index = _listBox.IndexFromPoint(e.Location);
            _listBox.SelectedIndex = index;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _listBox.SelectedIndex < 0;
        _ctxRemove.Visible = _listBox.SelectedIndex >= 0;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var owner = GetDialogOwner();
        var result = _modalService.PromptForCallerIdentity(owner, _accountQueryService.GetLocalUsers(), _sidEntryHelper);
        if (result.Accepted && result.SelectedSid != null)
        {
            if (_listBox.Items.Cast<CallerDisplayItem>().Any(item => string.Equals(item.Sid, result.SelectedSid, StringComparison.OrdinalIgnoreCase)))
            {
                _modalService.ShowDuplicateCallerWarning(owner);
                return;
            }

            if (result.DisplayName != null)
                _onSidNameLearned?.Invoke(result.SelectedSid, result.DisplayName);

            _listBox.Items.Add(new CallerDisplayItem(result.SelectedSid, _sidNames, _displayNameResolver));
            Changed?.Invoke();
        }
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_listBox.SelectedIndex < 0)
            return;
        _listBox.Items.RemoveAt(_listBox.SelectedIndex);
        Changed?.Invoke();
    }

    private IWin32Window? GetDialogOwner()
    {
        var form = FindForm();
        if (form != null)
            return form;

        return IsHandleCreated ? this : null;
    }

    private class CallerDisplayItem(
        string sid,
        IReadOnlyDictionary<string, string>? sidNames,
        SidDisplayNameResolver displayNameResolver)
    {
        public string Sid { get; } = sid;
        private readonly string _displayName = displayNameResolver.GetDisplayName(sid, null, sidNames);

        public override string ToString() => _displayName;
    }
}
