namespace RunFence.Persistence;

public enum ConfigIntegrityResult
{
    Valid,
    FirstRun,
    DecryptionFailed
}

public interface IDatabaseService :
    ICredentialStorePersistence,
    IMainConfigPersistence,
    IAppConfigPersistence,
    IConfigIntegrityVerifier,
    IConfigSaltReader,
    IConfigReencryptionPersistence;
