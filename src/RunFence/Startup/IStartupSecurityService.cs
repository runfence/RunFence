using RunFence.Core.Models;

namespace RunFence.Startup;

public interface IStartupSecurityService
{
    List<StartupSecurityFinding> RunChecks(CancellationToken cancellationToken = default);
}