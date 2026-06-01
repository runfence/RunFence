using Moq;
using RunFence.Acl.Permissions;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="ExeAssociationRegistryReader"/>.
/// Uses in-memory HKU and HKLM overrides.
/// A fake SID "S-1-2-3" is returned by a mocked <see cref="IInteractiveUserResolver"/>.
/// Registry hive infrastructure is shared via <see cref="RegistryTestHelper"/>.
/// </summary>
public class ExeAssociationRegistryReaderTests : IDisposable
{
    private const string TestSid = "S-1-2-3";
    private const string AppAccountSid = "S-1-2-4";
    private const string AppExe = @"C:\Apps\myapp.exe";

    private readonly RegistryTestHelper _registry = new("ReaderHku", "ReaderHklm");
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver;

    public ExeAssociationRegistryReaderTests()
    {
        _interactiveUserResolver = new Mock<IInteractiveUserResolver>();
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(TestSid);
    }

    public void Dispose() => _registry.Dispose();

    private ExeAssociationRegistryReader CreateReader() =>
        new(
            _registry.HiveManager.Object,
            _interactiveUserResolver.Object,
            new AssociationRegistryProtocolMarkerReader(),
            _registry.HklmRoot,
            _registry.HkuRoot);

    private void SetHkuProtocol(string protocol, string command)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{protocol}");
        key.SetValue("URL Protocol", "");
        using var cmdKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{protocol}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    private void SetHkuExtension(string ext, string progId, string? command = null)
    {
        using var extKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{ext}");
        extKey.SetValue(null, progId);
        if (command != null)
        {
            using var cmdKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{progId}\shell\open\command");
            cmdKey.SetValue(null, command);
        }
    }

    private void SetHklmExtension(string ext, string progId, string command)
    {
        using var extKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{ext}");
        extKey.SetValue(null, progId);
        using var cmdKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{progId}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    private void SetHklmProtocol(string protocol, string command)
    {
        using var cmdKey = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{protocol}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    private void SetAccountProtocol(string sid, string protocol, string command)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{protocol}");
        key.SetValue("URL Protocol", "");
        using var cmdKey = _registry.HkuRoot.CreateSubKey($@"{sid}\Software\Classes\{protocol}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    // --- Protocol in HKU with non-default args ---

    [Fact]
    public void GetHandledAssociations_HkuProtocolMatchingExe_IncludesKey()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" -- ""%1""");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.Contains("http", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_LoadedAppAccountHiveMatchingExe_IncludesKey()
    {
        SetAccountProtocol(AppAccountSid, "mailto", $@"""{AppExe}"" --account ""%1""");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe, AppAccountSid);

        Assert.Contains("mailto", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNonDefaultArguments_HkuProtocolWithFlag_ReturnsArgs()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" -- ""%1""");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, "http");

        Assert.Equal("-- \"%1\"", args);
    }

    [Fact]
    public void GetNonDefaultArguments_LoadedAppAccountHiveProtocolWithFlag_ReturnsArgs()
    {
        SetAccountProtocol(AppAccountSid, "mailto", $@"""{AppExe}"" --account ""%1""");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, "mailto", AppAccountSid);

        Assert.Equal("--account \"%1\"", args);
    }

    [Fact]
    public void GetHandledAssociations_AppAccountHiveNotLoaded_SkipsAccountClasses()
    {
        SetAccountProtocol(AppAccountSid, "mailto", $@"""{AppExe}"" --account ""%1""");
        _registry.HiveManager.Setup(h => h.IsHiveLoaded(AppAccountSid)).Returns(false);

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe, AppAccountSid);

        Assert.DoesNotContain("mailto", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_InteractiveUserUnavailable_StillUsesLoadedAppAccountHive()
    {
        SetAccountProtocol(AppAccountSid, "mailto", $@"""{AppExe}"" --account ""%1""");
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe, AppAccountSid);

        Assert.Contains("mailto", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_AppAccountHiveLoadsAfterCachedMiss_RefreshesForLoadedState()
    {
        SetAccountProtocol(AppAccountSid, "mailto", $@"""{AppExe}"" --account ""%1""");
        _registry.HiveManager.Setup(h => h.IsHiveLoaded(AppAccountSid)).Returns(false);
        var reader = CreateReader();

        var beforeLoad = reader.GetHandledAssociations(AppExe, AppAccountSid);

        _registry.HiveManager.Setup(h => h.IsHiveLoaded(AppAccountSid)).Returns(true);
        var afterLoad = reader.GetHandledAssociations(AppExe, AppAccountSid);

        Assert.DoesNotContain("mailto", beforeLoad, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("mailto", afterLoad, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_KeyInAccountAndInteractiveHives_UsesAccountHiveSuggestion()
    {
        SetAccountProtocol(AppAccountSid, "http", $@"""{AppExe}"" --account ""%1""");
        SetHkuProtocol("http", @"""C:\Other\otherapp.exe"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe, AppAccountSid);

        Assert.Contains("http", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNonDefaultArguments_KeyInAccountAndInteractiveHives_AccountHiveWins()
    {
        SetAccountProtocol(AppAccountSid, "http", $@"""{AppExe}"" --account ""%1""");
        SetHkuProtocol("http", $@"""{AppExe}"" --interactive ""%1""");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, "http", AppAccountSid);

        Assert.Equal("--account \"%1\"", args);
    }

    // --- Extension in HKLM via ProgId resolution ---

    [Fact]
    public void GetHandledAssociations_HklmExtensionViaProgId_IncludesKey()
    {
        SetHklmExtension(".pdf", "MyApp.PDF", $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.Contains(".pdf", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNonDefaultArguments_HklmExtensionDefaultArgs_ReturnsNull()
    {
        SetHklmExtension(".pdf", "MyApp.PDF", $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, ".pdf");

        Assert.Null(args);
    }

    // --- RunFence ProgId as default value → excluded ---

    [Fact]
    public void GetHandledAssociations_RunFenceProgIdAsDefault_ExcludesKey()
    {
        using var extKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.pdf");
        extKey.SetValue(null, $"{PathConstants.HandlerProgIdPrefix}.pdf");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.DoesNotContain(".pdf", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- AppData exe path → HKLM scan skipped ---

    [Fact]
    public void GetHandledAssociations_AppDataExe_SkipsHklm()
    {
        const string appDataExe = @"C:\Users\test\AppData\Local\myapp\myapp.exe";
        SetHklmProtocol("http", $@"""{appDataExe}"" %1");
        SetHklmExtension(".pdf", "MyApp.PDF", $@"""{appDataExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(appDataExe);

        Assert.DoesNotContain("http", keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pdf", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_AppDataExe_StillFindsHkuEntries()
    {
        const string appDataExe = @"C:\Users\test\AppData\Local\myapp\myapp.exe";
        SetHkuProtocol("http", $@"""{appDataExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(appDataExe);

        Assert.Contains("http", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_CustomAppDataProtocolMatchingExe_IncludesKey()
    {
        const string appDataExe = @"C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe";
        SetHkuProtocol("postman", $@"""{appDataExe}"" ""%1""");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(appDataExe);

        Assert.Contains("postman", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetHandledAssociations_MatchingExeWithDifferentDirectorySeparators_IncludesKey()
    {
        const string commandExe = @"C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe";
        const string selectedExe = "C:/Users/Vlad/AppData/Local/Postman/app-12.7.6/Postman.exe";
        SetHkuProtocol("postman", $@"""{commandExe}"" ""%1""");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(selectedExe);

        Assert.Contains("postman", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- Default args → GetNonDefaultArguments returns null ---

    [Fact]
    public void GetNonDefaultArguments_DefaultArgs_ReturnsNull()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, "http");

        Assert.Null(args);
    }

    // --- HKU priority over HKLM ---

    [Fact]
    public void GetHandledAssociations_KeyInBothHkuAndHklm_HkuWins()
    {
        const string hkuCommand = $@"""{AppExe}"" --hku ""%1""";
        const string hklmCommand = $@"""{AppExe}"" %1";
        SetHkuProtocol("http", hkuCommand);
        SetHklmProtocol("http", hklmCommand);

        var reader = CreateReader();
        reader.GetHandledAssociations(AppExe);
        var args = reader.GetNonDefaultArguments(AppExe, "http");

        Assert.Equal("--hku \"%1\"", args);
    }

    // --- Cache: GetNonDefaultArguments after GetHandledAssociations ---

    [Fact]
    public void GetNonDefaultArguments_AfterFullScan_ReturnsCachedResult()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" -- ""%1""");

        var reader = CreateReader();
        reader.GetHandledAssociations(AppExe);
        var args = reader.GetNonDefaultArguments(AppExe, "http");

        Assert.Equal("-- \"%1\"", args);
        _registry.HiveManager.Verify(h => h.EnsureHiveLoaded(TestSid), Times.Once);
    }

    // --- GetNonDefaultArguments targeted lookup before full scan ---

    [Fact]
    public void GetNonDefaultArguments_TargetedLookupBeforeFullScan_ReturnsCorrectResult()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" -- ""%1""");

        var reader = CreateReader();
        var args = reader.GetNonDefaultArguments(AppExe, "http");

        Assert.Equal("-- \"%1\"", args);
    }

    // --- Extension in HKU with ProgId command in HKU ---

    [Fact]
    public void GetHandledAssociations_HkuExtensionWithProgId_IncludesKey()
    {
        SetHkuExtension(".txt", "MyApp.TXT", $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.Contains(".txt", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- Extension in HKU with ProgId resolved via HKLM fallback ---

    [Fact]
    public void GetHandledAssociations_HkuExtensionProgIdResolvedViaHklm_IncludesKey()
    {
        // HKU has the .txt → ProgId mapping but ProgId command is in HKLM
        using var extKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\.txt");
        extKey.SetValue(null, "SharedApp.TXT");
        using var cmdKey = _registry.HklmRoot.CreateSubKey(@"Software\Classes\SharedApp.TXT\shell\open\command");
        cmdKey.SetValue(null, $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.Contains(".txt", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- Non-matching exe → not included ---

    [Fact]
    public void GetHandledAssociations_DifferentExe_ExcludesKey()
    {
        SetHkuProtocol("http", @"""C:\Other\otherapp.exe"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.DoesNotContain("http", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- Protocol subkey without URL Protocol value → excluded ---

    [Fact]
    public void GetHandledAssociations_SubkeyWithoutUrlProtocol_ExcludesKey()
    {
        // Create a subkey that is NOT a URL protocol handler (no "URL Protocol" value)
        using var key = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\SomeProgId\shell\open\command");
        key.SetValue(null, $@"""{AppExe}"" %1");

        var reader = CreateReader();
        var keys = reader.GetHandledAssociations(AppExe);

        Assert.DoesNotContain("SomeProgId", keys, StringComparer.OrdinalIgnoreCase);
    }

    // --- GetNonDefaultArguments after full scan for unmatched key → null ---

    [Fact]
    public void GetNonDefaultArguments_AfterFullScanForUnmatchedKey_ReturnsNull()
    {
        SetHkuProtocol("http", $@"""{AppExe}"" %1");

        var reader = CreateReader();
        reader.GetHandledAssociations(AppExe);
        var args = reader.GetNonDefaultArguments(AppExe, "mailto");

        Assert.Null(args);
    }

    // --- IsRegisteredProgId ---

    [Fact]
    public void IsRegisteredProgId_ExtensionKey_ClassNameHasShellOpenCommand_ReturnsTrue()
    {
        // Arrange: ProgId "txtfile" has HKLM shell\open\command
        using (_registry.HklmRoot.CreateSubKey(@"Software\Classes\txtfile\shell\open\command")) { }

        var reader = CreateReader();

        Assert.True(reader.IsRegisteredProgId(".txt", "txtfile"));
    }

    [Fact]
    public void IsRegisteredProgId_ExtensionKey_ClassNameHasNoShellOpenCommand_ReturnsFalse()
    {
        // Arrange: ProgId has no HKLM shell\open\command entry
        var reader = CreateReader();

        Assert.False(reader.IsRegisteredProgId(".txt", "UnknownClass"));
    }

    [Fact]
    public void IsRegisteredProgId_ProtocolKey_ReturnsFalse()
    {
        // Arrange: ProgId exists but the key is a protocol (not extension)
        using (_registry.HklmRoot.CreateSubKey(@"Software\Classes\SomeProt\shell\open\command")) { }

        var reader = CreateReader();

        // Protocols never qualify as registered ProgIds
        Assert.False(reader.IsRegisteredProgId("http", "SomeProt"));
    }

    [Fact]
    public void IsRegisteredProgId_ExtensionKey_ClassNameSubKeyExistsButNoCommand_ReturnsFalse()
    {
        // Arrange: class key exists but no shell\open\command
        using (_registry.HklmRoot.CreateSubKey(@"Software\Classes\NoCmd")) { }

        var reader = CreateReader();

        Assert.False(reader.IsRegisteredProgId(".xyz", "NoCmd"));
    }
}
