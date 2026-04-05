using RunFence.Core.Models;

namespace RunFence.Startup;

public record struct ContainerTraverseGrant(AppContainerEntry Container, string TraverseDir, List<string> AppliedPaths);

public record EnforcementResult(
    Dictionary<string, DateTime> TimestampUpdates,
    List<ContainerTraverseGrant> TraverseGrants);

public interface IStartupEnforcementService
{
    /// <summary>
    /// Enforces ACLs, icons, shortcuts, and container traverse grants for all apps.
    /// Returns timestamp updates for apps whose icons were regenerated and a list of
    /// container traverse grants to re-track on the live database.
    /// AppEntry property fixes must be applied by the caller (via <c>FixAppEntryDefaults</c>)
    /// before calling this method.
    /// </summary>
    EnforcementResult Enforce(AppDatabase database);
}