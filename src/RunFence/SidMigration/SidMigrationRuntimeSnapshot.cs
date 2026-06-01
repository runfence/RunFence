using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public sealed class SidMigrationRuntimeSnapshot(
    AppDatabase databaseBefore,
    CredentialStore credentialsBefore,
    AppConfigRuntimeStateSnapshot appConfigStateBefore)
{
    public AppDatabase DatabaseBefore { get; } = databaseBefore;

    public CredentialStore CredentialsBefore { get; } = credentialsBefore;

    public AppConfigRuntimeStateSnapshot AppConfigStateBefore { get; } = appConfigStateBefore;
}
