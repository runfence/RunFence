using Microsoft.Win32;
using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AssociationAutoSetService"/>.
/// Uses Registry.CurrentUser with a unique subkey as the HKU override.
/// A stable fake SID (<c>S-1-5-21-9001-9002-9003-1001</c>) is used as the test user SID;
/// credential entries use this SID so they don't collide with the current user's real SID.
/// </summary>
public class AssociationAutoSetServiceTests : IDisposable
{
    // A stable fake SID that won't match the actual test runner's SID
    private const string TestSid = "S-1-5-21-9001-9002-9003-1001";
    private const string OtherSid = "S-1-5-21-9001-9002-9003-1002";

    private readonly string _testSubKey;
    private readonly RegistryKey _hkuRoot;
    private readonly TempDirectory _tempDir;
    private readonly string _launcherPath;

    private readonly Mock<IUserHiveManager> _hiveManager;
    private readonly Mock<ISessionProvider> _sessionProvider;
    private readonly Mock<IHandlerMappingService> _handlerMappingService;
    private readonly Mock<IIpcCallerAuthorizer> _callerAuthorizer;
    private readonly Mock<ILoggingService> _log;

    private readonly SessionContext _session;

    public AssociationAutoSetServiceTests()
    {
        _testSubKey = $@"Software\RunFenceTests\AutoSet_{Guid.NewGuid():N}";
        _hkuRoot = Registry.CurrentUser.CreateSubKey(_testSubKey)!;

        _tempDir = new TempDirectory("RunFenceAutoSetTests");
        _launcherPath = Path.Combine(_tempDir.Path, "RunFence.Launcher.exe");
        File.WriteAllText(_launcherPath, "stub");

        _hiveManager = new Mock<IUserHiveManager>();
        _sessionProvider = new Mock<ISessionProvider>();
        _handlerMappingService = new Mock<IHandlerMappingService>();
        _callerAuthorizer = new Mock<IIpcCallerAuthorizer>();
        _log = new Mock<ILoggingService>();

        // Mock hive manager: EnsureHiveLoaded returns null (hive already loaded),
        // IsHiveLoaded returns true — simulates an already-loaded hive.
        _hiveManager.Setup(h => h.EnsureHiveLoaded(It.IsAny<string>())).Returns((IDisposable?)null);
        _hiveManager.Setup(h => h.IsHiveLoaded(It.IsAny<string>())).Returns(true);

        // Default: no explicit per-app authorization (direct wins on conflict)
        _callerAuthorizer.Setup(a => a.HasExplicitPerAppAuthorization(
            It.IsAny<string?>(), It.IsAny<AppEntry>(), It.IsAny<AppDatabase>())).Returns(false);

        _session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        };
        _sessionProvider.Setup(p => p.GetSession()).Returns(_session);
    }

    public void Dispose()
    {
        _hkuRoot.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false); }
        catch { }
        _tempDir.Dispose();
    }

    private AssociationAutoSetService CreateService()
        => new(_hiveManager.Object, _sessionProvider.Object, _handlerMappingService.Object,
            _callerAuthorizer.Object, _log.Object, _hkuRoot, _launcherPath);

    private void SetMappings(Dictionary<string, HandlerMappingEntry> mappings)
        => _handlerMappingService
            .Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(mappings);

    private void AddCredential(string sid)
        => _session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = sid });

    private string? ReadDefaultValue(string path)
    {
        using var key = _hkuRoot.OpenSubKey(path);
        return key?.GetValue(null) as string;
    }

    private string? ReadValue(string path, string valueName)
    {
        using var key = _hkuRoot.OpenSubKey(path);
        return key?.GetValue(valueName) as string;
    }

    // --- AutoSet extension ---

    [Fact]
    public void AutoSetExtension_WritesRunFenceFallbackAndSetsDefaultValue()
    {
        // Arrange: pre-existing handler for .test extension
        const string existingProgId = "SomeApp.File";
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.test"))
            extKey.SetValue(null, existingProgId);

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: default value set to RunFence ProgId
        var defaultVal = ReadDefaultValue($@"{TestSid}\Software\Classes\.test");
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test", defaultVal);

        // Assert: RunFenceFallback contains original ProgId
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.test", Constants.RunFenceFallbackValueName);
        Assert.Equal(existingProgId, fallback);
    }

    // --- AutoSet protocol ---

    [Fact]
    public void AutoSetProtocol_WritesRunFenceFallbackAndSetsUrlProtocolAndCommand()
    {
        // Arrange: pre-existing command for mailto
        const string existingCommand = @"""C:\Mail\client.exe"" %1";
        using (var cmdKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\mailto\shell\open\command"))
            cmdKey.SetValue(null, existingCommand);

        SetMappings(new Dictionary<string, HandlerMappingEntry> { ["mailto"] = new HandlerMappingEntry("app1") });

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: command set to launcher
        var command = ReadDefaultValue($@"{TestSid}\Software\Classes\mailto\shell\open\command");
        Assert.NotNull(command);
        Assert.Contains(_launcherPath, command);
        Assert.Contains("--resolve", command);
        Assert.Contains("\"mailto\"", command);

        // Assert: URL Protocol written
        var urlProtocol = ReadValue($@"{TestSid}\Software\Classes\mailto", "URL Protocol");
        Assert.Equal(string.Empty, urlProtocol);

        // Assert: RunFenceFallback contains original command
        var fallback = ReadValue($@"{TestSid}\Software\Classes\mailto", Constants.RunFenceFallbackValueName);
        Assert.Equal(existingCommand, fallback);
    }

    // --- DefaultAppsOnly skip ---

    [Fact]
    public void AutoSet_SkipsDefaultAppsOnlyAssociations()
    {
        // All DefaultAppsOnlyAssociations: http, https, .htm, .html, .pdf, ftp
        var mappings = new Dictionary<string, HandlerMappingEntry>
        {
            ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1"),
            [".htm"] = new HandlerMappingEntry("app1"), [".html"] = new HandlerMappingEntry("app1"),
            [".pdf"] = new HandlerMappingEntry("app1"), ["ftp"] = new HandlerMappingEntry("app1"),
            ["mailto"] = new HandlerMappingEntry("app1") // This one should be processed
        };
        SetMappings(mappings);

        CreateService().AutoSetForUser(TestSid);

        // DefaultAppsOnly keys must NOT be written
        foreach (var key in Constants.DefaultAppsOnlyAssociations)
        {
            var path = key.StartsWith('.')
                ? $@"{TestSid}\Software\Classes\{key}"
                : $@"{TestSid}\Software\Classes\{key}\shell\open\command";
            Assert.Null(_hkuRoot.OpenSubKey(path)?.GetValue(null));
        }

        // mailto must be written
        var mailtoCmd = ReadDefaultValue($@"{TestSid}\Software\Classes\mailto\shell\open\command");
        Assert.NotNull(mailtoCmd);
        Assert.Contains("--resolve", mailtoCmd);
    }

    // --- Idempotency ---

    [Fact]
    public void AutoSet_Idempotent_DoesNotOverwriteRunFenceFallback()
    {
        const string originalProgId = "OriginalApp.File";
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.test"))
            extKey.SetValue(null, originalProgId);

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        var svc = CreateService();
        svc.AutoSetForUser(TestSid);

        // Change the default value externally (simulate someone else modifying it)
        using (var extKey = _hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.test", writable: true))
            extKey!.SetValue(null, "AnotherApp.File");

        // Use a fresh service to bypass _completedSids cache; RunFenceFallback already exists
        // from the first call, so the second call must not overwrite it.
        var svc2 = CreateService();
        svc2.AutoSetForUser(TestSid);

        // RunFenceFallback must still be the original value — not overwritten by second call
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.test", Constants.RunFenceFallbackValueName);
        Assert.Equal(originalProgId, fallback);
    }

    // --- Restore ---

    [Fact]
    public void Restore_CallsRestoreFromFallback()
    {
        // Arrange: set up an extension with a stored RunFenceFallback
        const string originalProgId = "OldApp.Document";
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.doc"))
        {
            extKey.SetValue(null, Constants.HandlerProgIdPrefix + ".doc");
            extKey.SetValue(Constants.RunFenceFallbackValueName, originalProgId);
        }
        // Need TestSid subkey to exist at the top level of _hkuRoot for enumeration
        _hkuRoot.CreateSubKey(TestSid)!.Dispose();

        AddCredential(TestSid);

        // Act
        CreateService().RestoreForAllUsers();

        // Assert: default value restored
        var defaultVal = ReadDefaultValue($@"{TestSid}\Software\Classes\.doc");
        Assert.Equal(originalProgId, defaultVal);

        // Assert: RunFenceFallback removed
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.doc", Constants.RunFenceFallbackValueName);
        Assert.Null(fallback);
    }

    // --- Stale ProgId cleanup ---

    [Fact]
    public void AutoSet_CleansStaleUserProgIds()
    {
        // Arrange: old per-user ProgId from before HKLM migration
        using (_hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\RunFence_http\shell\open\command")) { }

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: stale per-user RunFence ProgId deleted
        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\RunFence_http"));
    }

    // --- Target user SIDs ---

    [Fact]
    public void AutoSet_IncludesInteractiveAndCredentialUsers_ExcludesCurrentAdmin()
    {
        // Note: SidResolutionHelper.GetInteractiveUserSid() returns null in tests (not initialized),
        // so only credential SIDs participate. We add two fake credential SIDs (should be processed)
        // and the actual current user SID (should be excluded as current admin).
        var currentAdminSid = SidResolutionHelper.GetCurrentUserSid();
        AddCredential(TestSid);
        AddCredential(OtherSid);
        AddCredential(currentAdminSid); // current admin — must be excluded
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        CreateService().AutoSetForAllUsers();

        // Both fake credential SIDs processed
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test",
            ReadDefaultValue($@"{TestSid}\Software\Classes\.test"));
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test",
            ReadDefaultValue($@"{OtherSid}\Software\Classes\.test"));

        // Current admin SID excluded — nothing written for it
        _hiveManager.Verify(h => h.EnsureHiveLoaded(currentAdminSid), Times.Never);
    }

    [Fact]
    public void AutoSet_DeduplicatesWhenInteractiveEqualsCredentialSid()
    {
        // When the same SID appears twice (e.g., interactive == credential SID),
        // GetTargetUserSids deduplicates via HashSet so AutoSetForUserInternal runs once.
        // We simulate this by adding the same SID twice as credentials.
        AddCredential(TestSid);
        AddCredential(TestSid); // duplicate

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        // _hiveManager should only be called once for the SID (deduplicated)
        CreateService().AutoSetForAllUsers();

        // Verify it works (only one set written, no errors)
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test",
            ReadDefaultValue($@"{TestSid}\Software\Classes\.test"));

        // EnsureHiveLoaded called once — GetTargetUserSids deduplicates via HashSet
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Once);
    }

    [Fact]
    public void AutoSet_SkipsNullInteractiveSid()
    {
        // SidResolutionHelper.GetInteractiveUserSid() returns null in tests (not initialized).
        // Verify: null interactive SID is safely skipped; credential SIDs still get auto-set.
        AddCredential(TestSid);
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        CreateService().AutoSetForAllUsers();

        // Credential SID still processed despite null interactive SID
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test",
            ReadDefaultValue($@"{TestSid}\Software\Classes\.test"));
    }

    // --- CompletedSids cache ---

    [Fact]
    public void AutoSet_CompletedSids_SkipsRedundantCalls()
    {
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        var svc = CreateService();
        svc.AutoSetForUser(TestSid);
        svc.AutoSetForUser(TestSid); // second call on same service instance — should be a no-op

        // EnsureHiveLoaded only called once (second call skipped via _completedSids)
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Once);
    }

    // --- AutoSetForAllUsers clears cache ---

    [Fact]
    public void AutoSetForAllUsers_ClearsCompletedSids_ForcesReEvaluation()
    {
        AddCredential(TestSid);
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        var svc = CreateService();

        // First auto-set — populates _completedSids (1 call to EnsureHiveLoaded)
        svc.AutoSetForUser(TestSid);

        // Second call with same SID — skipped because already in _completedSids (no extra call)
        svc.AutoSetForUser(TestSid);

        // AutoSetForAllUsers clears _completedSids before processing, so it re-evaluates TestSid.
        // This triggers another EnsureHiveLoaded call despite TestSid being in _completedSids.
        svc.AutoSetForAllUsers();

        // Total: 1 (first AutoSetForUser) + 0 (second AutoSetForUser, cached) + 1 (AutoSetForAllUsers re-evaluated) = 2
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Exactly(2));
    }

    // --- ManageAssociations flag ---

    [Fact]
    public void AutoSetForUser_SkipsWhenManageAssociationsFalse()
    {
        _session.Database.GetOrCreateAccount(TestSid).ManageAssociations = false;
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        CreateService().AutoSetForUser(TestSid);

        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.test"));
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Never);
    }

    [Fact]
    public void AutoSetForAllUsers_SkipsSidsWithManageAssociationsFalse()
    {
        AddCredential(TestSid);
        AddCredential(OtherSid);
        _session.Database.GetOrCreateAccount(TestSid).ManageAssociations = false;
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        CreateService().AutoSetForAllUsers();

        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.test"));
        Assert.Equal(Constants.HandlerProgIdPrefix + ".test",
            ReadDefaultValue($@"{OtherSid}\Software\Classes\.test"));
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Never);
    }

    // --- RestoreForUser ---

    [Fact]
    public void RestoreForUser_RestoresSingleUserHandlers()
    {
        // Arrange: two users, both have RunFenceFallback set
        const string originalProgId = "OldApp.Document";
        foreach (var sid in new[] { TestSid, OtherSid })
        {
            using var extKey = _hkuRoot.CreateSubKey($@"{sid}\Software\Classes\.doc");
            extKey.SetValue(null, Constants.HandlerProgIdPrefix + ".doc");
            extKey.SetValue(Constants.RunFenceFallbackValueName, originalProgId);
        }

        // Act: restore only TestSid
        CreateService().RestoreForUser(TestSid);

        // Assert: TestSid restored
        var testDefault = ReadDefaultValue($@"{TestSid}\Software\Classes\.doc");
        Assert.Equal(originalProgId, testDefault);
        var testFallback = ReadValue($@"{TestSid}\Software\Classes\.doc", Constants.RunFenceFallbackValueName);
        Assert.Null(testFallback);

        // Assert: OtherSid untouched
        var otherDefault = ReadDefaultValue($@"{OtherSid}\Software\Classes\.doc");
        Assert.Equal(Constants.HandlerProgIdPrefix + ".doc", otherDefault);
    }

    [Fact]
    public void RestoreForUser_ClearsCompletedSids_AllowsReAutoSet()
    {
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });

        var svc = CreateService();
        svc.AutoSetForUser(TestSid);

        // Restore clears _completedSids for this SID
        svc.RestoreForUser(TestSid);

        // Re-auto-set should run (not skipped by cache)
        svc.AutoSetForUser(TestSid);

        // 1 (initial) + 1 (RestoreForUser) + 1 (re-auto-set) = 3
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Exactly(3));
    }

    // --- RestoreForAllUsers clears caches ---

    // --- Direct handler: class-based extension ---

    [Fact]
    public void AutoSetDirectClassExtension_SetsHkcuDefaultToClassName()
    {
        // Arrange
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.txt"))
            extKey.SetValue(null, "SomeOldProgId");

        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: HKCU default set to class name
        var defaultVal = ReadDefaultValue($@"{TestSid}\Software\Classes\.txt");
        Assert.Equal("txtfile", defaultVal);

        // Assert: RunFenceFallback stores original
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.txt", Constants.RunFenceFallbackValueName);
        Assert.Equal("SomeOldProgId", fallback);
    }

    [Fact]
    public void AutoSetDirectClassExtension_AlreadyCorrect_NoFallbackCreated()
    {
        // Arrange: already set to txtfile
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.txt"))
            extKey.SetValue(null, "txtfile");

        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: no fallback created because value was already correct
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.txt", Constants.RunFenceFallbackValueName);
        Assert.Null(fallback);
    }

    // --- Direct handler: command-based extension ---

    [Fact]
    public void AutoSetDirectCommandExtension_SetsShellOpenCommandAndRunFenceFallback()
    {
        // Arrange: existing default ProgId on extension key
        const string existingProgId = "SomePreviousProgId";
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.py"))
            extKey.SetValue(null, existingProgId);

        const string newCommand = @"""C:\python.exe"" ""%1""";
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".py"] = new DirectHandlerEntry { Command = newCommand }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: shell\open\command updated
        var command = ReadDefaultValue($@"{TestSid}\Software\Classes\.py\shell\open\command");
        Assert.Equal(newCommand, command);

        // Assert: RunFenceFallback on extension key stores original default value (not RunFenceDirectFallback)
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.py", Constants.RunFenceFallbackValueName);
        Assert.Equal(existingProgId, fallback);
    }

    [Fact]
    public void AutoSetDirectCommandExtension_NoExistingDefault_StoresEmptyFallback()
    {
        // Arrange: no existing default on extension key
        const string newCommand = @"""C:\python.exe"" ""%1""";
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".py"] = new DirectHandlerEntry { Command = newCommand }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: shell\open\command set
        var command = ReadDefaultValue($@"{TestSid}\Software\Classes\.py\shell\open\command");
        Assert.Equal(newCommand, command);

        // Assert: RunFenceFallback is empty string (no previous default)
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.py", Constants.RunFenceFallbackValueName);
        Assert.Equal(string.Empty, fallback);
    }

    [Fact]
    public void AutoSetDirectCommandExtension_AlreadyCorrect_NoFallbackCreated()
    {
        // Arrange: already correct command
        const string command = @"""C:\python.exe"" ""%1""";
        using (var cmdKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.py\shell\open\command"))
            cmdKey.SetValue(null, command);

        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".py"] = new DirectHandlerEntry { Command = command }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: no fallback created (command already matched)
        var fallback = ReadValue($@"{TestSid}\Software\Classes\.py", Constants.RunFenceFallbackValueName);
        Assert.Null(fallback);
    }

    // --- Direct handler: command-based protocol ---

    [Fact]
    public void AutoSetDirectCommandProtocol_SetsShellOpenCommandAndUrlProtocol()
    {
        // Arrange
        const string existingCommand = @"""C:\old-app.exe"" %1";
        using (var cmdKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\myproto\shell\open\command"))
            cmdKey.SetValue(null, existingCommand);

        const string newCommand = @"""C:\new-app.exe"" %1";
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["myproto"] = new DirectHandlerEntry { Command = newCommand }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: command updated
        var command = ReadDefaultValue($@"{TestSid}\Software\Classes\myproto\shell\open\command");
        Assert.Equal(newCommand, command);

        // Assert: URL Protocol written
        var urlProtocol = ReadValue($@"{TestSid}\Software\Classes\myproto", "URL Protocol");
        Assert.Equal(string.Empty, urlProtocol);

        // Assert: RunFenceFallback stores original command
        var fallback = ReadValue($@"{TestSid}\Software\Classes\myproto", Constants.RunFenceFallbackValueName);
        Assert.Equal(existingCommand, fallback);
    }

    // --- Restore handles command-based extension via RunFenceFallback ---

    [Fact]
    public void Restore_CommandExtension_CleansUpShellOpenCommand()
    {
        // Arrange: extension with RunFenceFallback set (was set by command-based direct handler)
        // The fallback stores the original default value (empty = no previous default)
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.txt"))
        {
            extKey.SetValue(Constants.RunFenceFallbackValueName, string.Empty);
            using (var cmdKey = extKey.CreateSubKey(@"shell\open\command"))
                cmdKey.SetValue(null, @"""C:\new-editor.exe"" ""%1""");
        }
        _hkuRoot.CreateSubKey(TestSid)!.Dispose();
        AddCredential(TestSid);

        // Act
        CreateService().RestoreForAllUsers();

        // Assert: shell\open\command cleaned up (RestoreFromFallback deletes it for extensions)
        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.txt\shell\open\command"));
        Assert.Null(ReadValue($@"{TestSid}\Software\Classes\.txt", Constants.RunFenceFallbackValueName));
    }

    [Fact]
    public void RestoreForUser_CommandExtension_CleansUpShellOpenCommand()
    {
        // Arrange: extension key with RunFenceFallback = original default ProgId
        const string originalProgId = "SomeApp.Document";
        using (var extKey = _hkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.log"))
        {
            extKey.SetValue(Constants.RunFenceFallbackValueName, originalProgId);
            using (var cmdKey = extKey.CreateSubKey(@"shell\open\command"))
                cmdKey.SetValue(null, @"""C:\new.exe"" ""%1""");
        }

        // Act
        CreateService().RestoreForUser(TestSid);

        // Assert: default restored and shell\open\command cleaned up
        Assert.Equal(originalProgId, ReadDefaultValue($@"{TestSid}\Software\Classes\.log"));
        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.log\shell\open\command"));
    }

    // --- Conflict resolution: app-based vs direct ---

    [Fact]
    public void ResolveConflicts_DirectWins_WhenNoExplicitPerAppAuth()
    {
        // Arrange: same key in both app-based and direct mappings, no explicit per-app auth
        var appEntry = new AppEntry { Id = "app1", Name = "TestApp", ExePath = "test.exe", AccountSid = TestSid };
        _session.Database.Apps.Add(appEntry);

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".txt"] = new HandlerMappingEntry("app1") });
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        // callerAuthorizer returns false (no explicit per-app auth) — direct wins
        _callerAuthorizer.Setup(a => a.HasExplicitPerAppAuthorization(
            TestSid, appEntry, It.IsAny<AppDatabase>())).Returns(false);

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: direct handler (class-based) applied — default set to "txtfile"
        var defaultVal = ReadDefaultValue($@"{TestSid}\Software\Classes\.txt");
        Assert.Equal("txtfile", defaultVal);
    }

    [Fact]
    public void ResolveConflicts_AppWins_WhenExplicitPerAppAuth()
    {
        // Arrange: same key in both app-based and direct mappings, explicit per-app auth present
        var appEntry = new AppEntry { Id = "app1", Name = "TestApp", ExePath = "test.exe", AccountSid = TestSid };
        _session.Database.Apps.Add(appEntry);

        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".txt"] = new HandlerMappingEntry("app1") });
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        // callerAuthorizer returns true (explicit per-app auth) — app wins
        _callerAuthorizer.Setup(a => a.HasExplicitPerAppAuthorization(
            TestSid, appEntry, It.IsAny<AppDatabase>())).Returns(true);

        // Act
        CreateService().AutoSetForUser(TestSid);

        // Assert: app-based handler applied — default set to RunFence ProgId
        var defaultVal = ReadDefaultValue($@"{TestSid}\Software\Classes\.txt");
        Assert.Equal(Constants.HandlerProgIdPrefix + ".txt", defaultVal);
    }

    // --- ManageAssociations=false skips direct handlers ---

    [Fact]
    public void AutoSetForUser_SkipsDirectHandlers_WhenManageAssociationsFalse()
    {
        _session.Database.GetOrCreateAccount(TestSid).ManageAssociations = false;
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        CreateService().AutoSetForUser(TestSid);

        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\.txt"));
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Never);
    }

    // --- Class-based protocol entry is skipped ---

    [Fact]
    public void AutoSetForUser_SkipsClassBasedProtocolDirectHandler()
    {
        // A class-based direct handler (ClassName set, no Command) for a protocol key (no leading '.')
        // is invalid — class-based handlers only apply to file extensions. The service should log a
        // warning and skip it, leaving no registry entries for that protocol.
        _session.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["myproto"] = new DirectHandlerEntry { ClassName = "MyApp.Protocol" } // protocol + ClassName = invalid
        };
        SetMappings(new Dictionary<string, HandlerMappingEntry>());

        CreateService().AutoSetForUser(TestSid);

        // No registry entry created for the invalid class-based protocol handler
        Assert.Null(_hkuRoot.OpenSubKey($@"{TestSid}\Software\Classes\myproto"));

        // Warning was logged
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("myproto") && s.Contains("class-based"))),
            Times.Once);
    }

    [Fact]
    public void RestoreForAllUsers_ClearsCompletedAndCleanedSids()
    {
        AddCredential(TestSid);
        SetMappings(new Dictionary<string, HandlerMappingEntry> { [".test"] = new HandlerMappingEntry("app1") });
        _hkuRoot.CreateSubKey(TestSid)!.Dispose();

        var svc = CreateService();

        // First auto-set — populates _completedSids (1 call to EnsureHiveLoaded)
        svc.AutoSetForUser(TestSid);

        // RestoreForAllUsers clears _completedSids and _cleanedSids.
        // It also calls EnsureHiveLoaded for credential SIDs (1 more call).
        svc.RestoreForAllUsers();

        // After restore, AutoSetForUser should NOT skip the SID (cache cleared).
        // This triggers another EnsureHiveLoaded call.
        svc.AutoSetForUser(TestSid);

        // Total: 1 (AutoSetForUser) + 1 (RestoreForAllUsers) + 1 (AutoSetForUser after restore) = 3
        _hiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Exactly(3));
    }
}
