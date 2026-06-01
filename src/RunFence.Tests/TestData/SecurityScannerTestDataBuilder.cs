using System.Security.AccessControl;
using Moq;
using RunFence.Core;
using RunFence.SecurityScanner;
using RunFence.Tests.Helpers;
using Scanner = RunFence.SecurityScanner.SecurityScanner;

namespace RunFence.Tests.TestData;

public sealed class SecurityScannerTestDataBuilder
{
    private readonly TestScannerDataAccess _dataAccess = new();

    public SecurityScannerTestDataBuilder WithStartupFolderEntry(string folderPath, params string[] files)
    {
        _dataAccess.SetPublicStartupPath(folderPath);
        _dataAccess.AddFolderFiles(folderPath, files);
        return this;
    }

    public SecurityScannerTestDataBuilder WithScriptEntry(string userSid, string scriptsDirPath)
    {
        _dataAccess.SetGpScriptsDir(userSid, scriptsDirPath);
        return this;
    }

    public SecurityScannerTestDataBuilder WithAutorunEntry((string Hive, string Path) key, params string[] paths)
    {
        _dataAccess.AddRegistryAutorunPaths(key, paths.ToList());
        return this;
    }

    public SecurityScannerTestDataBuilder WithIfeoEntry(string exeName, string? debuggerPath = null, string? verifierDlls = null, RegistrySecurity? security = null)
    {
        _dataAccess.AddIfeoSubkeyName(exeName);
        _dataAccess.SetIfeoDebuggerPath(exeName, debuggerPath);
        _dataAccess.SetIfeoVerifierDlls(exeName, verifierDlls);
        if (security != null)
            _dataAccess.SetIfeoSubkeySecurity(exeName, security);
        return this;
    }

    public SecurityScannerTestDataBuilder WithLsaPolicyValue(RegistrySecurity? security, params string[] dllPaths)
    {
        _dataAccess.AddLsaPackageEntry(security, dllPaths.ToList());
        return this;
    }

    public SecurityScannerTestDataBuilder WithNetworkProvider(string displayPath, RegistrySecurity? security, string navTarget, params string[] dllPaths)
    {
        _dataAccess.AddNetworkProviderEntry(displayPath, security, dllPaths.ToList(), navTarget);
        return this;
    }

    public SecurityScannerTestDataBuilder WithAccountPolicyValue(int? lockoutThreshold = null, bool? adminLockoutEnabled = null, bool? blankPasswordRestrictionEnabled = null)
    {
        _dataAccess.SetAccountLockoutThreshold(lockoutThreshold);
        _dataAccess.SetAdminAccountLockoutEnabled(adminLockoutEnabled);
        _dataAccess.SetBlankPasswordRestrictionEnabled(blankPasswordRestrictionEnabled);
        return this;
    }

    public SecurityScannerTestDataBuilder WithFirewallPolicyValue((bool IsDisabled, bool IsStopped)? serviceState, params (string ProfileName, bool Enabled)[] profileStates)
    {
        _dataAccess.SetWindowsFirewallServiceState(serviceState);
        _dataAccess.SetFirewallProfileStates(profileStates.ToList());
        return this;
    }

    public SecurityScannerTestDataBuilder WithDiskRootAcl(string driveRoot, DirectorySecurity security)
    {
        _dataAccess.AddDriveRoot(driveRoot);
        _dataAccess.AddDirectorySecurity(driveRoot, security);
        return this;
    }

    public SecurityScannerTestDataBuilder WithCurrentUserSid(string? sid)
    {
        _dataAccess.SetCurrentUserSid(sid);
        return this;
    }

    public SecurityScannerTestDataBuilder WithInteractiveUserSid(string? sid)
    {
        _dataAccess.SetInteractiveUserSid(sid);
        return this;
    }

    public SecurityScannerTestDataBuilder WithAdminMemberSids(params string[] sids)
    {
        _dataAccess.SetAdminMemberSids(new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase));
        return this;
    }

    public SecurityScannerTestDataBuilder WithPublicStartupPath(string? path)
    {
        _dataAccess.SetPublicStartupPath(path);
        return this;
    }

    public SecurityScannerTestDataBuilder WithCurrentUserStartupPath(string? path)
    {
        _dataAccess.SetCurrentUserStartupPath(path);
        return this;
    }

    public SecurityScannerTestDataBuilder WithStartupFolderAcl(string path, DirectorySecurity security)
    {
        _dataAccess.AddDirectorySecurity(path, security);
        return this;
    }

    public SecurityScannerTestDataBuilder WithFolderFiles(string folderPath, params string[] files)
    {
        _dataAccess.AddFolderFiles(folderPath, files);
        return this;
    }

    public SecurityScannerTestDataBuilder WithoutStartupPaths()
    {
        _dataAccess.ClearStartupPaths();
        return this;
    }

    public SecurityScannerTestDataBuilder Configure(Action<SecurityScannerTestDataBuilder>? configure)
    {
        configure?.Invoke(this);
        return this;
    }

    public SecurityScannerTestDataBuilder ConfigureRaw(Action<TestScannerDataAccess>? configure)
    {
        configure?.Invoke(_dataAccess);
        return this;
    }

    public Scanner Build()
    {
        var log = Mock.Of<ILoggingService>();
        var aclCheck = new AclCheckHelper(_dataAccess, _dataAccess, log);
        var autorunChecker = new AutorunChecker(_dataAccess, aclCheck, log);
        var perUserScanner = new PerUserScanner(
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            aclCheck,
            autorunChecker,
            log);
        var registryScanner = new MachineLevelRegistryScanner(
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            aclCheck,
            log);
        var policyScanner = new MachineLevelPolicyScanner(
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            _dataAccess,
            aclCheck,
            perUserScanner,
            log);
        var diskRootScanner = new DiskRootScanner(_dataAccess, _dataAccess, aclCheck, log);

        return new Scanner(
            _dataAccess,
            aclCheck,
            autorunChecker,
            perUserScanner,
            registryScanner,
            policyScanner,
            diskRootScanner);
    }
}
