using RunFence.Core;

namespace RunFence.SecurityScanner;

public class SecurityScannerCompositionFactory
{
    private readonly SecurityScannerEnvironmentBuilder _environmentBuilder;
    private readonly SecurityScannerAutorunBuilder _autorunBuilder;
    private readonly SecurityScannerPolicyBuilder _policyBuilder;

    public SecurityScannerCompositionFactory()
        : this(
            CreateDefaultEnvironmentBuilder(out var autorunRegistry, out var log, out var groupPolicy),
            CreateDefaultAutorunBuilder(log, autorunRegistry, groupPolicy),
            CreateDefaultPolicyBuilder(log, groupPolicy))
    {
    }

    public SecurityScannerCompositionFactory(
        SecurityScannerEnvironmentBuilder environmentBuilder,
        SecurityScannerAutorunBuilder autorunBuilder,
        SecurityScannerPolicyBuilder policyBuilder)
    {
        _environmentBuilder = environmentBuilder;
        _autorunBuilder = autorunBuilder;
        _policyBuilder = policyBuilder;
    }

    public SecurityScanner CreateDefaultScanner()
    {
        var environment = _environmentBuilder.CreateEnvironmentDataAccess();
        var fileSystem = _environmentBuilder.CreateFileSystemDataAccess();
        var aclCheck = _environmentBuilder.CreateAclCheckHelper(environment);
        var autorunChecker = _autorunBuilder.CreateAutorunChecker(fileSystem, aclCheck);
        var perUserScanner = _autorunBuilder.CreatePerUserScanner(environment, fileSystem, aclCheck, autorunChecker);
        var registryScanner = _autorunBuilder.CreateMachineLevelRegistryScanner(fileSystem, aclCheck);
        var policyScanner = _policyBuilder.CreateMachineLevelPolicyScanner(environment, fileSystem, aclCheck, perUserScanner);
        var diskRootScanner = _policyBuilder.CreateDiskRootScanner(fileSystem, aclCheck);

        return new SecurityScanner(
            environment,
            aclCheck,
            autorunChecker,
            perUserScanner,
            registryScanner,
            policyScanner,
            diskRootScanner);
    }

    private static SecurityScannerEnvironmentBuilder CreateDefaultEnvironmentBuilder(
        out IAutorunRegistryAccess autorunRegistry,
        out ILoggingService log,
        out IGroupPolicyDataAccess groupPolicy)
    {
        log = new ConsoleLoggingService();
        autorunRegistry = new AutorunRegistryDataAccess();
        groupPolicy = new GroupPolicyDataAccess();

        return new SecurityScannerEnvironmentBuilder(
            log,
            Console.Error.WriteLine,
            new LocalGroupPolicyNativeReader(),
            new ShortcutResolver(),
            autorunRegistry);
    }

    private static SecurityScannerAutorunBuilder CreateDefaultAutorunBuilder(
        ILoggingService log,
        IAutorunRegistryAccess autorunRegistry,
        IGroupPolicyDataAccess groupPolicy) =>
        new(
            log,
            autorunRegistry,
            new WinlogonRegistryDataAccess(),
            new IfeoRegistryDataAccess(),
            new WindowsComponentRegistryDataAccess(),
            groupPolicy);

    private static SecurityScannerPolicyBuilder CreateDefaultPolicyBuilder(
        ILoggingService log,
        IGroupPolicyDataAccess groupPolicy) =>
        new(
            log,
            new DriveRootNativeReader(),
            new TaskSchedulerDataAccess(),
            groupPolicy,
            new ServiceRegistryDataAccess(Console.Error.WriteLine),
            new FirewallPolicyDataAccess(new FirewallServiceNativeReader()),
            new AccountPolicyDataAccess(new SamAccountPolicyNativeReader()));
}
