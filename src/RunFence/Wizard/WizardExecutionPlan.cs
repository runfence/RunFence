using RunFence.Apps.UI;

namespace RunFence.Wizard;

public sealed record WizardExecutionPlan(
    string ResolvedSid,
    IReadOnlyList<AppEntryBuildOptions> AppBuildOptions,
    bool HasPreEnforcementAction,
    bool HasPostEnforcementAction,
    bool CreateDesktopShortcut);
