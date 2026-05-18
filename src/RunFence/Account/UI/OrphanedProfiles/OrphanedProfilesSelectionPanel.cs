using RunFence.Account.OrphanedProfiles;

namespace RunFence.Account.UI.OrphanedProfiles;

public partial class OrphanedProfilesSelectionPanel : UserControl
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    private readonly IOrphanedProfileService _orphanedProfileService;
    private CancellationTokenSource? _sizeCalculationCts;
    private readonly Dictionary<string, ListViewItem> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private List<OrphanedProfile>? _pendingProfilesForSizeCalculation;
    private int _sizeCalculationVersion;

    public OrphanedProfilesSelectionPanel(IOrphanedProfileService orphanedProfileService)
        : this()
    {
        ArgumentNullException.ThrowIfNull(orphanedProfileService);
        _orphanedProfileService = orphanedProfileService;
    }

    public IReadOnlyList<OrphanedProfile> CheckedProfiles =>
        _profileListView.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => (OrphanedProfile)item.Tag!)
            .ToList();

    public void Populate(List<OrphanedProfile> profiles)
    {
        StopSizeCalculation();

        bool hasProfiles = profiles.Count > 0;

        _descLabel.Text = hasProfiles
            ? "The following orphaned profile directories were found:"
            : "No orphaned profiles found.";

        _profileListView.BeginUpdate();
        _itemsByPath.Clear();
        _profileListView.Items.Clear();
        foreach (var p in profiles)
        {
            var item = new ListViewItem(p.ProfilePath)
            {
                Checked = true,
                Tag = p
            };
            item.SubItems.Add("");
            _itemsByPath[p.ProfilePath] = item;
            _profileListView.Items.Add(item);
        }
        _profileListView.EndUpdate();

        _profileListView.Visible = hasProfiles;
        _selectAllButton.Enabled = hasProfiles;
        _selectAllButton.Visible = hasProfiles;
        _deselectAllButton.Enabled = hasProfiles;
        _deselectAllButton.Visible = hasProfiles;

        _pendingProfilesForSizeCalculation = hasProfiles ? profiles.ToList() : null;
        if (hasProfiles && IsHandleCreated)
            StartPendingSizeCalculation();
    }

    public void StopSizeCalculation()
    {
        _sizeCalculationVersion++;
        _pendingProfilesForSizeCalculation = null;
        _sizeCalculationCts?.Cancel();
        _sizeCalculationCts?.Dispose();
        _sizeCalculationCts = null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StartPendingSizeCalculation();
    }

    private void OnSelectAllClick(object? sender, EventArgs e)
    {
        foreach (ListViewItem item in _profileListView.Items)
            item.Checked = true;
    }

    private void OnDeselectAllClick(object? sender, EventArgs e)
    {
        foreach (ListViewItem item in _profileListView.Items)
            item.Checked = false;
    }

    private void StartPendingSizeCalculation()
    {
        if (_pendingProfilesForSizeCalculation == null || _sizeCalculationCts != null)
            return;

        var profiles = _pendingProfilesForSizeCalculation;
        _pendingProfilesForSizeCalculation = null;

        _sizeCalculationCts = new CancellationTokenSource();
        var version = _sizeCalculationVersion;
        _ = CalculateSizesAsync(
            profiles,
            _orphanedProfileService,
            version,
            SynchronizationContext.Current,
            _sizeCalculationCts.Token);
    }

    private async Task CalculateSizesAsync(
        List<OrphanedProfile> profiles,
        IOrphanedProfileService orphanedProfileService,
        int version,
        SynchronizationContext? synchronizationContext,
        CancellationToken cancellationToken)
    {
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PostSizeUpdate(profile.ProfilePath, "0+", version, synchronizationContext);

            try
            {
                var progress = new SizeProgressReporter(sizeMegabytes =>
                    PostSizeUpdate(profile.ProfilePath, $"{sizeMegabytes}+", version, synchronizationContext));
                var sizeBytes = await Task.Run(
                    () => orphanedProfileService.GetProfileSizeBytes(profile.ProfilePath, progress, cancellationToken),
                    cancellationToken);

                PostSizeUpdate(profile.ProfilePath, (sizeBytes / BytesPerMegabyte).ToString(), version, synchronizationContext);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private void PostSizeUpdate(string profilePath, string value, int version, SynchronizationContext? synchronizationContext)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (synchronizationContext != null && SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => ApplySizeUpdate(profilePath, value, version), null);
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplySizeUpdate(profilePath, value, version)));
            return;
        }

        ApplySizeUpdate(profilePath, value, version);
    }

    private void ApplySizeUpdate(string profilePath, string value, int version)
    {
        if (IsDisposed || version != _sizeCalculationVersion)
            return;

        if (!_itemsByPath.TryGetValue(profilePath, out var item))
            return;

        if (item.SubItems[1].Text == value)
            return;

        item.SubItems[1].Text = value;
    }

    private sealed class SizeProgressReporter(Action<long> reportSizeMegabytes) : IProgress<long>
    {
        public void Report(long value) => reportSizeMegabytes(value);
    }
}
