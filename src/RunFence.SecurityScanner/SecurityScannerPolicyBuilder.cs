using RunFence.Core;

namespace RunFence.SecurityScanner;

public sealed class SecurityScannerPolicyBuilder(
    ILoggingService log,
    IDriveRootNativeReader driveRoots,
    ITaskSchedulerDataAccess taskScheduler,
    IGroupPolicyDataAccess groupPolicy,
    IServiceRegistryAccess serviceRegistry,
    IFirewallPolicyDataAccess firewallPolicy,
    IAccountPolicyDataAccess accountPolicy)
{
    public MachineLevelPolicyScanner CreateMachineLevelPolicyScanner(
        IEnvironmentDataAccess environment,
        IFileSystemDataAccess fileSystem,
        AclCheckHelper aclCheck,
        PerUserScanner perUserScanner) =>
        new(environment, fileSystem, taskScheduler, groupPolicy, serviceRegistry, firewallPolicy, accountPolicy, aclCheck, perUserScanner, log);

    public DiskRootScanner CreateDiskRootScanner(
        IFileSystemDataAccess fileSystem,
        AclCheckHelper aclCheck) =>
        new(driveRoots, fileSystem, aclCheck, log);
}
