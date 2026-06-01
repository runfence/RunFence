using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.Traverse;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class GroupPolicyScriptHelperTests : IDisposable
{
    private const string FakeSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private sealed class FakeLogonScriptTraverseGranter : ILogonScriptTraverseGranter
    {
        public (string Sid, string ScriptsDirPath)? LastGrantCall { get; private set; }
        public (string Sid, string ScriptsDirPath)? LastRevokeCall { get; private set; }
        public List<string>? NextGrantResult { get; set; }
        public Exception? NextGrantException { get; set; }
        public Exception? NextRevokeException { get; set; }
        public int GrantCallCount { get; private set; }
        public int RevokeCallCount { get; private set; }

        public List<string>? GrantTraverseAccess(string sid, string scriptsDirPath)
        {
            LastGrantCall = (sid, scriptsDirPath);
            GrantCallCount++;
            if (NextGrantException != null)
                throw NextGrantException;

            return NextGrantResult;
        }

        public void RevokeTraverseAccess(string sid, string scriptsDirPath)
        {
            LastRevokeCall = (sid, scriptsDirPath);
            RevokeCallCount++;
            if (NextRevokeException != null)
                throw NextRevokeException;

        }
    }

    private readonly TempDirectory _tempDir;
    private readonly FakeLogonScriptTraverseGranter _traverseGranter;
    private readonly Mock<IProgramDataDirectoryProvisioningService> _directoryService;
    private readonly Mock<IProgramDataManagedObjectRepairService> _repairService;
    private readonly Mock<IProgramDataPathPolicyService> _pathPolicyService;
    private readonly Mock<IProgramDataObjectProvisioner> _objectProvisioner;
    private readonly Mock<IProgramDataKnownPathResolver> _pathResolver;
    private readonly List<ProgramDataExplicitFileRequest> _explicitFileRequests = [];
    private readonly GroupPolicyScriptHelper _helper;
    private readonly string _iniPath;
    private readonly string _gptPath;
    private readonly string _scriptsDir;
    private readonly string _legacyScriptsDir;

    public GroupPolicyScriptHelperTests()
    {
        _tempDir = new TempDirectory("ram_gptest");
        _scriptsDir = Path.Combine(_tempDir.Path, "scripts");
        _legacyScriptsDir = Path.Combine(_tempDir.Path, "legacy_scripts");
        Directory.CreateDirectory(_scriptsDir);
        Directory.CreateDirectory(_legacyScriptsDir);
        _traverseGranter = new FakeLogonScriptTraverseGranter();
        _directoryService = new Mock<IProgramDataDirectoryProvisioningService>();
        _repairService = new Mock<IProgramDataManagedObjectRepairService>();
        _pathPolicyService = new Mock<IProgramDataPathPolicyService>();
        _objectProvisioner = new Mock<IProgramDataObjectProvisioner>();
        _pathResolver = new Mock<IProgramDataKnownPathResolver>();
        _pathResolver
            .Setup(resolver => resolver.GetDirectoryPath(ProgramDataPolicies.Scripts))
            .Returns(_scriptsDir);
        SetupRestrictedProvisioner();
        SetupManagedScriptsDirectory();
        _helper = new GroupPolicyScriptHelper(new LogonScriptIniManager(), new LogonScriptStateRollbackStore(), new Mock<ILoggingService>().Object,
            _directoryService.Object,
            _repairService.Object,
            _pathPolicyService.Object,
            _objectProvisioner.Object,
            _pathResolver.Object,
            _traverseGranter,
            systemDir: _tempDir.Path, scriptsDir: _scriptsDir, legacyScriptsDir: _legacyScriptsDir);
        _iniPath = Path.Combine(_tempDir.Path, "GroupPolicyUsers", FakeSid, "User", "Scripts", "scripts.ini");
        _gptPath = Path.Combine(_tempDir.Path, "GroupPolicyUsers", FakeSid, "gpt.ini");
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    // --- Round-trip ---

    [Fact]
    public void SetLoginBlocked_True_ThenIsLoginBlocked_ReturnsTrue()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        Assert.True(_helper.IsLoginBlocked(FakeSid));
    }

    // --- IsLoginBlocked ---

    [Fact]
    public void IsLoginBlocked_NoIniFile_ReturnsFalse()
    {
        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_LogoffInLogonSection_ReturnsTrue()
    {
        WriteIni("[Logon]\r\n0CmdLine=logoff.exe\r\n0Parameters=\r\n");

        Assert.True(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_DifferentScript_ReturnsFalse()
    {
        WriteIni("[Logon]\r\n0CmdLine=other.cmd\r\n0Parameters=\r\n");

        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_CaseInsensitiveCmdLine()
    {
        WriteIni("[Logon]\r\n0CmdLine=LOGOFF.EXE\r\n0Parameters=\r\n");

        Assert.True(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_WrapperScriptPathInLogonSection_ReturnsTrue()
    {
        // New-style detection: the INI contains the full wrapper script path (not bare logoff.exe)
        // This matches what SetLoginBlocked(true) writes — the wrapper path is derived from scriptsDir + SID
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        WriteIni($"[Logon]\r\n0CmdLine={wrapperPath}\r\n0Parameters=\r\n");

        Assert.True(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_WrapperScriptPathForDifferentSid_ReturnsFalse()
    {
        // Wrapper script for a different SID must not match
        var otherSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
        var otherWrapperPath = Path.Combine(_scriptsDir, $"{otherSid}_block_login.cmd");
        WriteIni($"[Logon]\r\n0CmdLine={otherWrapperPath}\r\n0Parameters=\r\n");

        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void IsLoginBlocked_LogoffInLogoffSection_ReturnsFalse()
    {
        // logoff.exe in [Logoff] section should not count
        WriteIni("[Logoff]\r\n0CmdLine=logoff.exe\r\n0Parameters=\r\n");

        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    // --- SetLoginBlocked result ---

    [Fact]
    public void SetLoginBlocked_Block_ReturnsScriptPathAndNullTraverse()
    {
        var result = _helper.SetLoginBlocked(FakeSid, true);

        Assert.NotNull(result.ScriptPath);
        Assert.Contains("block_login.cmd", result.ScriptPath);
        // TraversePaths is null because no AncestorTraverseGranter is injected
        Assert.Null(result.TraversePaths);
    }

    [Fact]
    public void SetLoginBlocked_Unblock_ReturnsScriptPathForGrantCleanup()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var result = _helper.SetLoginBlocked(FakeSid, false);

        // ScriptPath is returned even on unblock so callers with a database
        // can remove the corresponding AccountGrants entries.
        Assert.NotNull(result.ScriptPath);
        Assert.Contains("block_login.cmd", result.ScriptPath);
        Assert.Null(result.TraversePaths);
    }

    // --- SetLoginBlocked (block) ---

    [Fact]
    public void SetLoginBlocked_Block_CreatesIniWithWrapperEntry()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        Assert.True(File.Exists(_iniPath));
        var content = File.ReadAllText(_iniPath);
        Assert.Contains("[Logon]", content);
        Assert.Contains("0CmdLine=", content);
        Assert.Contains("block_login.cmd", content);
        Assert.Contains("0Parameters=", content);

        // Wrapper script should exist and contain logoff.exe
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        Assert.True(File.Exists(wrapperPath));
        Assert.Contains("logoff.exe", File.ReadAllText(wrapperPath));
    }

    [Fact]
    public void SetLoginBlocked_Block_UsesSecureScriptsDirectoryAndPerUserWrapperAcl()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        var request = Assert.Single(_explicitFileRequests);
        Assert.StartsWith(wrapperPath + ".", request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".tmp", request.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProgramDataFileAclProfile.CurrentProcessUserFullControl, request.Profile);
        Assert.Contains(request.AdditionalAccess, access =>
            access.Principal.Value == FakeSid &&
            access.Rights == FileSystemRights.ReadAndExecute &&
            access.InheritanceFlags == InheritanceFlags.None &&
            access.PropagationFlags == PropagationFlags.None);

        _repairService.Verify(s => s.EnsureManagedFileOwner(wrapperPath), Times.Once);

        _repairService.Verify(
            s => s.EnsureManagedFileOwner(Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd")),
            Times.Once);
    }

    [Fact]
    public void SetLoginBlocked_Block_WithNonManagedScriptsDirectory_SecuresDirectoryAcl()
    {
        var scriptsDir = Path.Combine(_tempDir.Path, "non_managed_scripts");
        var helper = new GroupPolicyScriptHelper(
            new LogonScriptIniManager(),
            new LogonScriptStateRollbackStore(),
            new Mock<ILoggingService>().Object,
            Mock.Of<IProgramDataDirectoryProvisioningService>(),
            Mock.Of<IProgramDataManagedObjectRepairService>(),
            Mock.Of<IProgramDataPathPolicyService>(),
            Mock.Of<IProgramDataObjectProvisioner>(),
            Mock.Of<IProgramDataKnownPathResolver>(),
            _traverseGranter,
            systemDir: _tempDir.Path,
            scriptsDir: scriptsDir,
            legacyScriptsDir: _legacyScriptsDir);

        helper.SetLoginBlocked(FakeSid, true);

        var directorySecurity = new DirectoryInfo(scriptsDir).GetAccessControl();
        Assert.True(directorySecurity.AreAccessRulesProtected);
    }

    [Fact]
    public void SetLoginBlocked_BlockTwice_RewritesWrapperAndReappliesTraverse()
    {
        _traverseGranter.NextGrantResult = [_scriptsDir];
        _helper.SetLoginBlocked(FakeSid, true);
        ResetProgramDataMocks();
        SetupManagedScriptsDirectory();

        var result = _helper.SetLoginBlocked(FakeSid, true);

        Assert.NotNull(result.TraversePaths);
        Assert.NotNull(_traverseGranter.LastGrantCall);
        Assert.Equal((FakeSid, _scriptsDir), _traverseGranter.LastGrantCall);
        Assert.Equal(2, _traverseGranter.GrantCallCount);
        _repairService.Verify(
            s => s.EnsureManagedFileOwner(result.ScriptPath!),
            Times.Once);
    }

    [Fact]
    public void SetLoginBlocked_Block_InvokesTraverseGranterAndReturnsPaths()
    {
        _traverseGranter.NextGrantResult = [_scriptsDir, Path.Combine(_tempDir.Path, "common")];

        var result = _helper.SetLoginBlocked(FakeSid, true);

        Assert.NotNull(_traverseGranter.LastGrantCall);
        Assert.Equal((FakeSid, _scriptsDir), _traverseGranter.LastGrantCall);
        Assert.Equal(_traverseGranter.NextGrantResult, result.TraversePaths);
    }

    [Fact]
    public void SetLoginBlocked_AlreadyBlocked_ExistingBlockRepairFailure_DoesNotLoseExistingState_WhenEnsureOwnerFails()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        var iniBytes = File.ReadAllBytes(_iniPath);
        var gptBytes = File.ReadAllBytes(_gptPath);
        var wrapperBytes = File.ReadAllBytes(wrapperPath);

        ResetProgramDataMocks();
        SetupManagedScriptsDirectory();
        _repairService.Setup(s => s.EnsureManagedFileOwner(wrapperPath))
            .Throws(new IOException("forced"));

        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.True(_helper.IsLoginBlocked(FakeSid));
        Assert.Equal(iniBytes, File.ReadAllBytes(_iniPath));
        Assert.Equal(gptBytes, File.ReadAllBytes(_gptPath));
        Assert.Equal(wrapperBytes, File.ReadAllBytes(wrapperPath));
    }

    [Fact]
    public void SetLoginBlocked_AlreadyBlocked_ExistingBlockRepairFailure_RestoresReadOnlyWrapper()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        var wrapperBytes = File.ReadAllBytes(wrapperPath);
        File.SetAttributes(wrapperPath, File.GetAttributes(wrapperPath) | FileAttributes.ReadOnly);

        try
        {
            ResetProgramDataMocks();
            SetupManagedScriptsDirectory();
            _repairService.Setup(s => s.EnsureManagedFileOwner(wrapperPath))
                .Throws(new IOException("forced"));

            var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
                _helper.SetLoginBlocked(FakeSid, true));

            Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
            Assert.True(File.Exists(wrapperPath));
            Assert.Equal(wrapperBytes, File.ReadAllBytes(wrapperPath));
            Assert.True((File.GetAttributes(wrapperPath) & FileAttributes.ReadOnly) != 0);
            Assert.True(_helper.IsLoginBlocked(FakeSid));
        }
        finally
        {
            if (File.Exists(wrapperPath))
                File.SetAttributes(wrapperPath, File.GetAttributes(wrapperPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void SetLoginBlocked_AlreadyBlocked_ExistingBlockRepairFailure_DoesNotDeleteExistingScripts()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        ResetProgramDataMocks();
        SetupManagedScriptsDirectory();
        _traverseGranter.NextGrantException = new IOException("forced");

        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.True(_helper.IsLoginBlocked(FakeSid));
        Assert.True(File.Exists(_iniPath));
        Assert.True(File.Exists(_gptPath));
        Assert.True(File.Exists(wrapperPath));
    }

    [Fact]
    public void SetLoginBlocked_FirstTimeBlockFailure_RollsBackCreatedArtifacts()
    {
        _traverseGranter.NextGrantException = new IOException("forced");
        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.False(File.Exists(_iniPath));
        Assert.False(File.Exists(_gptPath));
        Assert.False(File.Exists(wrapperPath));
        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void SetLoginBlocked_FirstTimeWrapperPublishFailure_DeletesTemporaryWrapper()
    {
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        Directory.CreateDirectory(wrapperPath);

        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.Empty(Directory.EnumerateFiles(_scriptsDir, $"{FakeSid}_block_login.cmd.*.tmp"));
        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void SetLoginBlocked_FirstTimeScriptsIniPublishFailure_DeletesTemporaryIni()
    {
        Directory.CreateDirectory(_iniPath);

        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(_iniPath)!, "scripts.ini.*.tmp"));
        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void SetLoginBlocked_FirstTimeGptIniPublishFailure_DeletesTemporaryGptIni()
    {
        Directory.CreateDirectory(_gptPath);

        var ex = Assert.Throws<AccountRestrictionOperationException>(() =>
            _helper.SetLoginBlocked(FakeSid, true));

        Assert.Equal(AccountRestrictionStatus.RolledBack, ex.Status);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(_gptPath)!, "gpt.ini.*.tmp"));
        Assert.False(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void SetLoginBlocked_Unblock_InvokesTraverseRevoke()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.NotNull(_traverseGranter.LastRevokeCall);
        Assert.Equal((FakeSid, _scriptsDir), _traverseGranter.LastRevokeCall);
    }

    [Fact]
    public void SetLoginBlocked_BlockTwice_NoDuplicate()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        _helper.SetLoginBlocked(FakeSid, true);

        var content = File.ReadAllText(_iniPath);
        var count = content.Split("block_login.cmd").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void SetLoginBlocked_Block_IniIsUtf16LeWithBom()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var bytes = File.ReadAllBytes(_iniPath);
        // UTF-16 LE BOM: FF FE
        Assert.True(bytes.Length >= 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
    }

    // --- SetLoginBlocked (unblock) ---

    [Fact]
    public void SetLoginBlocked_Unblock_DeletesIniWhenOnlyEntry()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        Assert.True(File.Exists(_iniPath));

        _helper.SetLoginBlocked(FakeSid, false);
        Assert.False(File.Exists(_iniPath));

        // Wrapper script should also be cleaned up
        var wrapperPath = Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");
        Assert.False(File.Exists(wrapperPath));
    }

    [Fact]
    public void SetLoginBlocked_UnblockNonBlocked_NoOp()
    {
        // Should not throw when there's no ini file
        _helper.SetLoginBlocked(FakeSid, false);
        Assert.False(File.Exists(_iniPath));
    }

    [Fact]
    public void SetLoginBlocked_Unblock_CleansUpEmptyDirs()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        var sidDir = Path.Combine(_tempDir.Path, "GroupPolicyUsers", FakeSid);
        Assert.True(Directory.Exists(sidDir));

        _helper.SetLoginBlocked(FakeSid, false);
        Assert.False(Directory.Exists(sidDir));
    }

    // --- Preserves other entries ---

    [Fact]
    public void SetLoginBlocked_PreservesOtherLogonScripts()
    {
        WriteIni("[Logon]\r\n0CmdLine=startup.cmd\r\n0Parameters=/quiet\r\n");

        _helper.SetLoginBlocked(FakeSid, true);

        var content = File.ReadAllText(_iniPath);
        Assert.Contains("startup.cmd", content);
        Assert.Contains("block_login.cmd", content);
    }

    [Fact]
    public void SetLoginBlocked_UnblockPreservesOtherScripts()
    {
        WriteIni("[Logon]\r\n0CmdLine=startup.cmd\r\n0Parameters=/quiet\r\n1CmdLine=logoff.exe\r\n1Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.True(File.Exists(_iniPath));
        var content = File.ReadAllText(_iniPath);
        Assert.Contains("startup.cmd", content);
        Assert.DoesNotContain("logoff.exe", content);
    }

    [Fact]
    public void SetLoginBlocked_UnblockReindexesRemaining()
    {
        WriteIni("[Logon]\r\n0CmdLine=first.cmd\r\n0Parameters=\r\n1CmdLine=logoff.exe\r\n1Parameters=\r\n2CmdLine=third.cmd\r\n2Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, false);

        var content = File.ReadAllText(_iniPath);
        Assert.Contains("0CmdLine=first.cmd", content);
        Assert.Contains("1CmdLine=third.cmd", content);
        Assert.DoesNotContain("2CmdLine=", content);
    }

    // --- Per-user isolation ---

    [Fact]
    public void SetLoginBlocked_DifferentSids_Independent()
    {
        var sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

        _helper.SetLoginBlocked(FakeSid, true);
        _helper.SetLoginBlocked(sid2, true);

        Assert.True(_helper.IsLoginBlocked(FakeSid));
        Assert.True(_helper.IsLoginBlocked(sid2));

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.False(_helper.IsLoginBlocked(FakeSid));
        Assert.True(_helper.IsLoginBlocked(sid2));
    }

    // --- Atomic writes ---

    [Fact]
    public void SetLoginBlocked_NoTmpFilesRemain()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var dir = Path.GetDirectoryName(_iniPath)!;
        var tmpFiles = Directory.GetFiles(dir, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    // --- gpt.ini management ---

    [Fact]
    public void SetLoginBlocked_Block_CreatesGptIniWithExtensionNamesAndVersion()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        Assert.True(File.Exists(_gptPath));
        var content = File.ReadAllText(_gptPath);
        Assert.Contains("[General]", content);
        Assert.Contains("gPCUserExtensionNames=[{42B5FAAE-6536-11D2-AE5A-0000F87571E3}{40B66650-4972-11D1-A7CA-0000F87571E3}]", content);
        // Initial version = 65536 (machine version 1, matching GP editor increment of +65536)
        Assert.Contains("Version=65536", content);
    }

    [Fact]
    public void SetLoginBlocked_Block_SidDirectoryIsHiddenAndSystem()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var sidDir = Path.Combine(_tempDir.Path, "GroupPolicyUsers", FakeSid);
        var attrs = new DirectoryInfo(sidDir).Attributes;
        Assert.True(attrs.HasFlag(FileAttributes.Hidden), "SID directory must be Hidden");
        Assert.True(attrs.HasFlag(FileAttributes.System), "SID directory must be System");
    }

    [Fact]
    public void SetLoginBlocked_Block_GptIniHasNoBom()
    {
        _helper.SetLoginBlocked(FakeSid, true);

        var bytes = File.ReadAllBytes(_gptPath);
        Assert.True(bytes.Length >= 2);
        // Must NOT have UTF-16 LE BOM (FF FE) — that's for scripts.ini only
        Assert.False(bytes[0] == 0xFF && bytes[1] == 0xFE, "gpt.ini must not have UTF-16 LE BOM");
        // Must NOT have UTF-8 BOM (EF BB BF) — would break the INI section parser
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..],
            "gpt.ini must not have UTF-8 BOM");
    }

    [Fact]
    public void SetLoginBlocked_Unblock_DeletesGptIni()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        Assert.True(File.Exists(_gptPath));

        _helper.SetLoginBlocked(FakeSid, false);
        Assert.False(File.Exists(_gptPath));
    }

    [Fact]
    public void SetLoginBlocked_UnblockWithOtherScripts_KeepsGptIniWithExtensions()
    {
        // Pre-existing other script + we add our block
        WriteIni("[Logon]\r\n0CmdLine=startup.cmd\r\n0Parameters=/quiet\r\n");
        _helper.SetLoginBlocked(FakeSid, true);

        // Unblock — our entry removed but startup.cmd remains
        _helper.SetLoginBlocked(FakeSid, false);

        // scripts.ini still exists, so gpt.ini must also remain with extension names
        Assert.True(File.Exists(_iniPath));
        Assert.True(File.Exists(_gptPath));
        var content = File.ReadAllText(_gptPath);
        Assert.Contains("gPCUserExtensionNames=", content);
    }

    [Fact]
    public void SetLoginBlocked_GptIniVersionIncrementsOnEachChange()
    {
        // Pre-existing other scripts so gpt.ini persists across block/unblock
        WriteIni("[Logon]\r\n0CmdLine=startup.cmd\r\n0Parameters=/quiet\r\n");

        _helper.SetLoginBlocked(FakeSid, true); // block: version → 1
        var v1 = ReadGptVersion();

        _helper.SetLoginBlocked(FakeSid, false); // unblock (other script remains): version → 2
        var v2 = ReadGptVersion();

        Assert.True(v2 > v1, $"Expected version to increment: v1={v1}, v2={v2}");
    }

    [Fact]
    public void SetLoginBlocked_Block_BareLogoffAlreadyBlocked_ReplacesEntryWithCurrentWrapper()
    {
        WriteIni("[Logon]\r\n0CmdLine=logoff.exe\r\n0Parameters=\r\n");

        var result = _helper.SetLoginBlocked(FakeSid, true);

        var wrapperPath = GetWrapperPath();
        Assert.Equal(wrapperPath, result.ScriptPath);
        Assert.True(File.Exists(wrapperPath));
        Assert.True(File.Exists(_gptPath));

        var content = File.ReadAllText(_iniPath);
        Assert.Contains($"0CmdLine={wrapperPath}", content);
        Assert.DoesNotContain("CmdLine=logoff.exe", content);
        Assert.Equal(65536, ReadGptVersion());
    }

    [Fact]
    public void SetLoginBlocked_Block_LegacyWrapperAlreadyBlocked_ReplacesEntryWithCurrentWrapper()
    {
        var legacyWrapperPath = GetLegacyWrapperPath();
        WriteIni($"[Logon]\r\n0CmdLine={legacyWrapperPath}\r\n0Parameters=\r\n");

        var result = _helper.SetLoginBlocked(FakeSid, true);

        var wrapperPath = GetWrapperPath();
        Assert.Equal(wrapperPath, result.ScriptPath);
        Assert.True(File.Exists(wrapperPath));
        Assert.True(File.Exists(_gptPath));

        var content = File.ReadAllText(_iniPath);
        Assert.Contains($"0CmdLine={wrapperPath}", content);
        Assert.DoesNotContain(legacyWrapperPath, content);
        Assert.Equal(65536, ReadGptVersion());
    }

    [Fact]
    public void SetLoginBlocked_Block_CurrentWrapperAlreadyBlocked_KeepsExistingGptVersion()
    {
        _helper.SetLoginBlocked(FakeSid, true);
        var version = ReadGptVersion();
        ResetProgramDataMocks();
        SetupManagedScriptsDirectory();

        _helper.SetLoginBlocked(FakeSid, true);

        var content = File.ReadAllText(_iniPath);
        Assert.Contains($"0CmdLine={GetWrapperPath()}", content);
        Assert.Equal(version, ReadGptVersion());
    }

    [Fact]
    public void SetLoginBlocked_Block_CurrentAndLegacyWrappersAlreadyBlocked_RemovesLegacyEntry()
    {
        var wrapperPath = GetWrapperPath();
        var legacyWrapperPath = GetLegacyWrapperPath();
        WriteIni($"[Logon]\r\n0CmdLine={wrapperPath}\r\n0Parameters=\r\n1CmdLine={legacyWrapperPath}\r\n1Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, true);

        var content = File.ReadAllText(_iniPath);
        Assert.Contains($"0CmdLine={wrapperPath}", content);
        Assert.DoesNotContain(legacyWrapperPath, content);
        Assert.DoesNotContain("1CmdLine=", content);
        Assert.Equal(65536, ReadGptVersion());
    }

    [Fact]
    public void SetLoginBlocked_UnblockNonBlocked_NoGptIniCreated()
    {
        _helper.SetLoginBlocked(FakeSid, false);

        Assert.False(File.Exists(_gptPath));
    }

    // --- Legacy RunAsManager path support ---

    [Fact]
    public void IsLoginBlocked_LegacyWrapperPathInLogonSection_ReturnsTrue()
    {
        var legacyWrapperPath = Path.Combine(_legacyScriptsDir, $"{FakeSid}_block_login.cmd");
        WriteIni($"[Logon]\r\n0CmdLine={legacyWrapperPath}\r\n0Parameters=\r\n");

        Assert.True(_helper.IsLoginBlocked(FakeSid));
    }

    [Fact]
    public void SetLoginBlocked_Unblock_RemovesLegacyEntryFromIni()
    {
        var legacyWrapperPath = Path.Combine(_legacyScriptsDir, $"{FakeSid}_block_login.cmd");
        WriteIni($"[Logon]\r\n0CmdLine={legacyWrapperPath}\r\n0Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.False(File.Exists(_iniPath));
    }

    [Fact]
    public void SetLoginBlocked_Unblock_DeletesLegacyWrapperFile()
    {
        var legacyWrapperPath = Path.Combine(_legacyScriptsDir, $"{FakeSid}_block_login.cmd");
        Directory.CreateDirectory(_legacyScriptsDir);
        File.WriteAllText(legacyWrapperPath, "@echo off\r\nlogoff.exe\r\n");
        WriteIni($"[Logon]\r\n0CmdLine={legacyWrapperPath}\r\n0Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.False(File.Exists(legacyWrapperPath));
    }

    [Fact]
    public void SetLoginBlocked_Unblock_RemovesLegacyEntryPreservingOtherScripts()
    {
        var legacyWrapperPath = Path.Combine(_legacyScriptsDir, $"{FakeSid}_block_login.cmd");
        WriteIni($"[Logon]\r\n0CmdLine=startup.cmd\r\n0Parameters=/quiet\r\n1CmdLine={legacyWrapperPath}\r\n1Parameters=\r\n");

        _helper.SetLoginBlocked(FakeSid, false);

        Assert.True(File.Exists(_iniPath));
        var content = File.ReadAllText(_iniPath);
        Assert.Contains("startup.cmd", content);
        Assert.DoesNotContain("legacy_scripts", content);
    }

    private int ReadGptVersion()
    {
        foreach (var line in File.ReadAllLines(_gptPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed["Version=".Length..], out var v))
                return v;
        }

        throw new InvalidOperationException("Version not found in gpt.ini");
    }

    private string GetWrapperPath() =>
        Path.Combine(_scriptsDir, $"{FakeSid}_block_login.cmd");

    private string GetLegacyWrapperPath() =>
        Path.Combine(_legacyScriptsDir, $"{FakeSid}_block_login.cmd");

    private void WriteIni(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_iniPath)!);
        File.WriteAllText(_iniPath, content,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
    }

    private void SetupManagedScriptsDirectory()
    {
        _pathPolicyService.Setup(s => s.IsUnderRoot(It.Is<string>(path =>
                path.Equals(_scriptsDir, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(_scriptsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
            .Returns(true);
        _objectProvisioner
            .Setup(p => p.CreateOrRepairDirectory(It.Is<ProgramDataExplicitDirectoryRequest>(
                request =>
                    request.Path == _scriptsDir &&
                    request.Profile == ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl &&
                    request.AdditionalAccess.Count == 0 &&
                    request.ReplaceExistingSecurity)))
            .Callback<ProgramDataExplicitDirectoryRequest>(request => Directory.CreateDirectory(request.Path));
        _repairService.Setup(s => s.EnsureManagedFileOwner(It.IsAny<string>())).Returns(false);
    }

    private void ResetProgramDataMocks()
    {
        _directoryService.Reset();
        _repairService.Reset();
        _pathPolicyService.Reset();
    }

    private void SetupRestrictedProvisioner()
    {
        _objectProvisioner
            .Setup(p => p.CreateFile(It.IsAny<ProgramDataExplicitFileRequest>(), It.IsAny<Action<Stream>>()))
            .Callback<ProgramDataExplicitFileRequest, Action<Stream>>((request, writeContent) =>
            {
                _explicitFileRequests.Add(request);
                Directory.CreateDirectory(Path.GetDirectoryName(request.Path)!);
                using var stream = new FileStream(request.Path, FileMode.CreateNew, FileAccess.ReadWrite, request.Share);
                writeContent(stream);
            });
        _objectProvisioner
            .Setup(p => p.CreateOrRepairDirectory(It.IsAny<ProgramDataExplicitDirectoryRequest>()))
            .Callback<ProgramDataExplicitDirectoryRequest>(request => Directory.CreateDirectory(request.Path));
    }
}
