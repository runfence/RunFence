using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Non-DI data parameters for <see cref="AccountPickerStep"/>.
/// Groups the per-call configuration data separately from service dependencies and lambdas.
/// </summary>
public record AccountPickerStepOptions(
    List<CredentialEntry> Credentials,
    IReadOnlyDictionary<string, string> SidNames,
    string GroupSid,
    string StepTitle,
    string InfoText,
    string? InteractiveUserSid = null,
    bool ExcludeAdmins = false,
    bool DefaultToCreateNew = false);

/// <summary>
/// Wizard step that lists accounts from a specified group with credential status indicators
/// and an optional "Create new account" entry at the bottom.
/// Replaces following steps dynamically when the user switches between existing and new account.
/// When <see cref="AccountPickerStepOptions.InteractiveUserSid"/> is set, selecting that account shows a warning label
/// explaining that it provides no isolation.
/// Used by Elevated App (Administrators group) and Gaming Account (Users group, excluding Admins).
/// </summary>
public class AccountPickerStep : WizardStepPage
{
    private readonly Action<string?, bool> _setSelection;
    private readonly ILocalGroupMembershipService _groupMembership;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly List<CredentialEntry> _credentials;
    private readonly ISidResolver _sidResolver;
    private readonly CredentialFilterHelper _credentialFilterHelper;
    private readonly IReadOnlyDictionary<string, string> _sidNames;
    private readonly Func<bool, IReadOnlyList<WizardStepPage>>? _followingStepsFactory;
    private readonly Func<IWizardProgressReporter, Task>? _commitAction;
    private readonly string _groupSid;
    private readonly string _stepTitle;
    private readonly bool _excludeAdmins;
    private readonly string? _interactiveUserSid;
    private readonly bool _defaultToCreateNew;

    private Label _infoLabel = null!;
    private Label _warningLabel = null!;
    private ListBox _accountListBox = null!;
    private bool? _lastIsCreate; // null = followingStepsFactory not yet invoked
    private bool _isLoading;

    /// <param name="setSelection">Receives (sid, isCreate) on <see cref="Collect"/>. sid is null when "Create new account" is chosen.</param>
    /// <param name="groupMembership">Service for querying local group membership.</param>
    /// <param name="localUserProvider">Service for listing local user accounts.</param>
    /// <param name="sidResolver">Service for resolving SID display names.</param>
    /// <param name="options">Non-DI data parameters for this step instance.</param>
    /// <param name="followingStepsFactory">
    /// Called when the user switches between "Create new account" and an existing account.
    /// Receives <c>true</c> when "Create new account" is selected.
    /// If null, no dynamic step replacement occurs.
    /// </param>
    /// <param name="commitAction">
    /// Optional mid-wizard async action run after <see cref="Collect"/> and before the wizard advances.
    /// Used to show a credential dialog when the selected existing account has no stored credential.
    /// </param>
    public AccountPickerStep(
        Action<string?, bool> setSelection,
        ILocalGroupMembershipService groupMembership,
        ILocalUserProvider localUserProvider,
        ISidResolver sidResolver,
        CredentialFilterHelper credentialFilterHelper,
        AccountPickerStepOptions options,
        Func<bool, IReadOnlyList<WizardStepPage>>? followingStepsFactory = null,
        Func<IWizardProgressReporter, Task>? commitAction = null)
    {
        _setSelection = setSelection;
        _groupMembership = groupMembership;
        _localUserProvider = localUserProvider;
        _credentials = options.Credentials;
        _sidResolver = sidResolver;
        _credentialFilterHelper = credentialFilterHelper;
        _sidNames = options.SidNames;
        _groupSid = options.GroupSid;
        _stepTitle = options.StepTitle;
        _excludeAdmins = options.ExcludeAdmins;
        _interactiveUserSid = options.InteractiveUserSid;
        _defaultToCreateNew = options.DefaultToCreateNew;
        _followingStepsFactory = followingStepsFactory;
        _commitAction = commitAction;
        BuildContent(options.InfoText);
    }

    public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) =>
        _commitAction != null ? _commitAction(progress) : Task.CompletedTask;

    public override string StepTitle => _stepTitle;
    public override bool CanProceed => !_isLoading;

    public override void OnActivated() => _ = PopulateList();

    public override string? Validate()
    {
        if (_isLoading) return "Accounts are still loading, please wait.";
        return _accountListBox.SelectedItem == null ? "Please select an account." : null;
    }

    public override void Collect()
    {
        var isCreate = _accountListBox.SelectedItem is CreateAccountItem;
        string? sid = isCreate ? null : (_accountListBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
        // Defensive: sid should always be non-null for an existing account selection (CredentialDisplayItem
        // always has a Sid set), but guard against edge cases such as a transient entry with an empty Sid.
        if (sid == null && !isCreate)
            return;
        _setSelection(sid, isCreate);
    }

    private void BuildContent(string infoText)
    {
        SuspendLayout();
        Padding = new Padding(8);

        _infoLabel = new Label
        {
            Text = infoText,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8)
        };
        TrackWrappingLabel(_infoLabel);

        _warningLabel = new Label
        {
            Text = "\u26A0\uFE0F This is the current interactive user account. Selecting a normal usage account " +
                   "provides no isolation \u2014 files accessible to the desktop user will also be accessible here.",
            AutoSize = false,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(0x7A, 0x50, 0x00),
            BackColor = Color.FromArgb(0xFF, 0xF3, 0xCD),
            Dock = DockStyle.Bottom,
            Padding = new Padding(8, 5, 8, 5),
            Visible = false
        };
        TrackWrappingLabel(_warningLabel);

        _accountListBox = new ListBox
        {
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 24
        };
        _accountListBox.DrawItem += OnDrawItem;
        _accountListBox.SelectedIndexChanged += OnSelectionChanged;

        Controls.Add(_accountListBox);
        Controls.Add(_warningLabel);
        Controls.Add(_infoLabel);
        ResumeLayout(false);
    }

    private async Task PopulateList()
    {
        string? selectedSid = (_accountListBox.SelectedItem as CredentialDisplayItem)?.Credential.Sid;
        bool selectedCreate = _accountListBox.SelectedItem is CreateAccountItem;

        if (_accountListBox.Items.Count == 0)
        {
            _isLoading = true;
            NotifyCanProceedChanged();
            _accountListBox.Items.Add("Loading accounts...");
        }

        var membersTask = Task.Run(() => _groupMembership.GetMembersOfGroup(_groupSid));
        var adminsTask = _excludeAdmins
            ? Task.Run(() => _groupMembership.GetMembersOfGroup(GroupFilterHelper.AdministratorsSid))
            : Task.FromResult<List<LocalUserAccount>>([]);

        try { await Task.WhenAll(membersTask, adminsTask); } catch { }

        _accountListBox.Items.Clear();

        // Null means enumeration failed — skip the filter rather than showing nothing.
        HashSet<string>? localUserSids = null;
        try
        {
            var localUsers = _localUserProvider.GetLocalUserAccounts();
            if (localUsers.Count > 0)
                localUserSids = localUsers.Select(u => u.Sid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
        }

        HashSet<string> memberSids;
        try
        {
            memberSids = membersTask.Result
                .Where(u => localUserSids == null || localUserSids.Contains(u.Sid))
                .Select(u => u.Sid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            memberSids = [];
        }

        if (_excludeAdmins)
        {
            try
            {
                var adminSids = adminsTask.Result
                    .Select(u => u.Sid)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                memberSids.ExceptWith(adminSids);
            }
            catch
            {
            }
        }

        var representedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var resolvable = _credentialFilterHelper.FilterResolvableCredentials(_credentials, _sidNames);
        foreach (var cred in resolvable)
        {
            if (string.IsNullOrEmpty(cred.Sid))
                continue;
            if (!memberSids.Contains(cred.Sid))
                continue;
            _accountListBox.Items.Add(new CredentialDisplayItem(cred, _sidResolver, _sidNames));
            representedSids.Add(cred.Sid);
        }

        foreach (var memberSid in memberSids.OrderBy(s => _sidNames.GetValueOrDefault(s) ?? s, StringComparer.OrdinalIgnoreCase))
        {
            if (representedSids.Contains(memberSid))
                continue;
            var transient = new CredentialEntry { Id = Guid.NewGuid(), Sid = memberSid };
            _accountListBox.Items.Add(new CredentialDisplayItem(transient, _sidResolver, _sidNames, hasStoredCredential: false));
            representedSids.Add(memberSid);
        }

        _accountListBox.Items.Add(new CreateAccountItem());

        if (_isLoading)
        {
            _isLoading = false;
            NotifyCanProceedChanged();
        }

        if (selectedSid != null)
        {
            for (int i = 0; i < _accountListBox.Items.Count; i++)
            {
                if (_accountListBox.Items[i] is CredentialDisplayItem di &&
                    string.Equals(di.Credential.Sid, selectedSid, StringComparison.OrdinalIgnoreCase))
                {
                    _accountListBox.SelectedIndex = i;
                    return;
                }
            }
        }

        if (selectedCreate || _defaultToCreateNew)
        {
            for (int i = 0; i < _accountListBox.Items.Count; i++)
            {
                if (_accountListBox.Items[i] is CreateAccountItem)
                {
                    _accountListBox.SelectedIndex = i;
                    return;
                }
            }
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;

        bool isCreate = _accountListBox.SelectedItem is CreateAccountItem;

        if (_interactiveUserSid != null && !isCreate &&
            _accountListBox.SelectedItem is CredentialDisplayItem selected)
        {
            _warningLabel.Visible = string.Equals(
                selected.Credential.Sid, _interactiveUserSid, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            _warningLabel.Visible = false;
        }

        if (_followingStepsFactory != null && isCreate != _lastIsCreate)
        {
            _lastIsCreate = isCreate;
            RequestReplaceFollowingSteps(_followingStepsFactory(isCreate));
        }
    }

    private static void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        var listBox = (ListBox)sender!;
        if (e.Index < 0 || e.Index >= listBox.Items.Count)
            return;

        var item = listBox.Items[e.Index];
        bool isSelected = (e.State & DrawItemState.Selected) != 0;

        using var backBrush = new SolidBrush(isSelected ? SystemColors.Highlight : SystemColors.Window);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textColor = isSelected ? SystemColors.HighlightText : SystemColors.WindowText;
        var textBounds = e.Bounds with { X = e.Bounds.X + 4, Width = e.Bounds.Width - 4 };
        const TextFormatFlags textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        if (item is CredentialDisplayItem credItem)
        {
            var indicatorColor = credItem.HasStoredCredential
                ? Color.FromArgb(0x00, 0x88, 0x00)
                : Color.FromArgb(0xA0, 0xA0, 0xA0);
            using var dot = new SolidBrush(indicatorColor);
            e.Graphics.FillEllipse(dot, e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 8) / 2, 8, 8);
            textBounds = e.Bounds with { X = e.Bounds.X + 18, Width = e.Bounds.Width - 18 };
        }
        else if (item is string && !isSelected)
        {
            textColor = SystemColors.GrayText;
        }

        TextRenderer.DrawText(e.Graphics, item.ToString(), listBox.Font, textBounds, textColor, textFlags);
        e.DrawFocusRectangle();
    }
}