using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 3 (SID Mapping Review) of SidMigrationDialog.
/// Owns the loading label and delegates grid building to SidMigrationMappingBuilder.
/// </summary>
/// <remarks>Manually constructed by SidMigrationDialog with runtime data — not DI-registered.</remarks>
public partial class MigrationMappingStep : UserControl
{
    private readonly SessionContext _session;
    private readonly ISidMigrationService _sidMigrationService;
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ILoggingService _log;
    private readonly IEnumerable<OrphanedSid> _orphanedSids;
    private readonly IProfilePathResolver _profilePathResolver;
    private readonly ISidNameCacheService _sidNameCache;

    private SidMigrationMappingBuilder? _builder;

    public MigrationMappingStep(
        SessionContext session,
        ISidMigrationService sidMigrationService,
        ILocalUserProvider localUserProvider,
        ILoggingService log,
        IEnumerable<OrphanedSid> orphanedSids,
        IProfilePathResolver profilePathResolver,
        ISidNameCacheService sidNameCache)
    {
        _session = session;
        _sidMigrationService = sidMigrationService;
        _localUserProvider = localUserProvider;
        _log = log;
        _orphanedSids = orphanedSids;
        _profilePathResolver = profilePathResolver;
        _sidNameCache = sidNameCache;
        InitializeComponent();
    }

    public void BeginAsync(Action onReady, Action onFailed)
    {
        var logic = new SidMigrationMappingLogic(
            _session, _sidMigrationService, _localUserProvider, _orphanedSids, _sidNameCache);
        var validator = new SidMigrationMappingValidator(_profilePathResolver);
        _builder = new SidMigrationMappingBuilder(logic, validator, _log, _orphanedSids);
        _builder.Ready += () =>
        {
            if (_builder.Content != null)
                Controls.Add(_builder.Content);
            onReady();
        };
        _builder.Failed += onFailed;
        _ = _builder.BuildMappingsAsync(_loadingLabel);
    }

    public bool TryCollectMappings(out List<SidMigrationMapping> mappings)
    {
        if (_builder == null)
        {
            mappings = [];
            return true;
        }

        return _builder.TryCollectMappingsFromGrid(out mappings);
    }

    public List<string> CollectDeleteSids()
        => _builder?.CollectDeleteSidsFromGrid() ?? [];
}
