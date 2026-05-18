using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 3 (SID Mapping Review) of SidMigrationDialog.
/// Owns the loading label and delegates grid building to SidMigrationMappingBuilder.
/// </summary>
/// <remarks>Manually constructed by SidMigrationDialog with runtime data — not DI-registered.</remarks>
public partial class MigrationMappingStep : UserControl, ISidMigrationMappingStepView
{
    private readonly SessionContext _session;
    private readonly ISidMigrationService _sidMigrationService;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILoggingService _log;
    private readonly IEnumerable<OrphanedSid> _orphanedSids;
    private readonly IProfilePathResolver _profilePathResolver;
    private readonly ISidNameCacheService _sidNameCache;
    private readonly IMessageBoxService _messageBoxService;

    private SidMigrationMappingBuilder? _builder;

    public MigrationMappingStep(
        SessionContext session,
        ISidMigrationService sidMigrationService,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        IEnumerable<OrphanedSid> orphanedSids,
        IProfilePathResolver profilePathResolver,
        ISidNameCacheService sidNameCache,
        IMessageBoxService messageBoxService)
    {
        _session = session;
        _sidMigrationService = sidMigrationService;
        _localUserProvider = localUserProvider;
        _log = log;
        _orphanedSids = orphanedSids;
        _profilePathResolver = profilePathResolver;
        _sidNameCache = sidNameCache;
        _messageBoxService = messageBoxService;
        InitializeComponent();
        AdjustDescriptionLayout();
    }

    public void BeginAsync(Action onReady, Action onFailed)
    {
        var logic = new SidMigrationMappingLogic(
            _session, _sidMigrationService, _localUserProvider, _orphanedSids, _sidNameCache);
        var validator = new SidMigrationMappingValidator(_profilePathResolver);
        _builder = new SidMigrationMappingBuilder(logic, validator, _log, _messageBoxService, _orphanedSids);
        _builder.Ready += () =>
        {
            if (_builder.Content != null)
                Controls.Add(_builder.Content);
            onReady();
        };
        _builder.Failed += onFailed;
        _ = _builder.BuildMappingsAsync(_loadingLabel);
    }

    public bool TryCollectSelections(out List<SidMigrationMapping> mappings, out List<string> deleteSids)
    {
        if (_builder == null)
        {
            mappings = [];
            deleteSids = [];
            return true;
        }

        return _builder.TryCollectSelectionsFromGrid(out mappings, out deleteSids);
    }

    Control ISidMigrationStepView.View => this;

    private void AdjustDescriptionLayout()
    {
        var descriptionHeight = TextRenderer.MeasureText(
            _descriptionLabel.Text,
            _descriptionLabel.Font,
            new Size(_descriptionLabel.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        _descriptionLabel.Height = descriptionHeight + _descriptionLabel.Padding.Vertical;
        _loadingLabel.Top = _descriptionLabel.Bottom + 8;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AdjustDescriptionLayout();
    }
}
