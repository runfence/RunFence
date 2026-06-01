using RunFence.Core;

namespace RunFence.SecurityScanner;

public sealed class SecurityScannerEnvironmentBuilder(
    ILoggingService log,
    Action<string> errorLogger,
    ILocalGroupPolicyNativeReader localGroupPolicyReader,
    ShortcutResolver shortcutResolver,
    IAutorunRegistryAccess autorunRegistry)
{
    public EnvironmentDataAccess CreateEnvironmentDataAccess() =>
        new(localGroupPolicyReader, errorLogger, new NTTranslateApi(log));

    public FileSystemDataAccess CreateFileSystemDataAccess() =>
        new(shortcutResolver);

    public AclCheckHelper CreateAclCheckHelper(IEnvironmentDataAccess environment) =>
        new(environment, autorunRegistry, log);
}
