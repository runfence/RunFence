using RunFence.Core;

namespace RunFence.SecurityScanner;

public sealed class SecurityScannerAutorunBuilder(
    ILoggingService log,
    IAutorunRegistryAccess autorunRegistry,
    IWinlogonRegistryAccess winlogonRegistry,
    IIfeoRegistryAccess ifeoRegistry,
    IWindowsComponentRegistryAccess windowsComponentRegistry,
    IGroupPolicyDataAccess groupPolicy)
{
    public AutorunChecker CreateAutorunChecker(
        IFileSystemDataAccess fileSystem,
        AclCheckHelper aclCheck) =>
        new(fileSystem, aclCheck, log);

    public PerUserScanner CreatePerUserScanner(
        IEnvironmentDataAccess environment,
        IFileSystemDataAccess fileSystem,
        AclCheckHelper aclCheck,
        AutorunChecker autorunChecker) =>
        new(environment, fileSystem, autorunRegistry, groupPolicy, aclCheck, autorunChecker, log);

    public MachineLevelRegistryScanner CreateMachineLevelRegistryScanner(
        IFileSystemDataAccess fileSystem,
        AclCheckHelper aclCheck) =>
        new(autorunRegistry, winlogonRegistry, ifeoRegistry, windowsComponentRegistry, fileSystem, aclCheck, log);
}
