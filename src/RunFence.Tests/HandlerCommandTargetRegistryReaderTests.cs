using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class HandlerCommandTargetRegistryReaderTests : IDisposable
{
    private const string TargetAccountSid = "S-1-5-21-3000";
    private const string InteractiveSid = "S-1-5-21-4000";
    private const string CurrentSid = "S-1-5-21-5000";

    private readonly RegistryTestHelper _registry = new("HandlerTargetReaderHku", "HandlerTargetReaderHklm");
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ISidResolver> _sidResolver = new();

    public HandlerCommandTargetRegistryReaderTests()
    {
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        _sidResolver.Setup(r => r.GetCurrentUserSid()).Returns(CurrentSid);
    }

    public void Dispose() => _registry.Dispose();

    private HandlerCommandTargetRegistryReader CreateReader() =>
        new(
            _interactiveUserSidResolver.Object,
            _sidResolver.Object,
            new TestHkuRootProvider(_registry.HkuRoot),
            new TestHklmClassesRootProvider(_registry.HklmRoot),
            new AssociationRegistryProtocolMarkerReader());

    private static void SetExtensionAssociation(IRegistryKey root, string? sid, string extension, string className, string command)
    {
        var classesRoot = sid == null ? @"Software\Classes" : $@"{sid}\Software\Classes";
        using var extKey = root.CreateSubKey($@"{classesRoot}\{extension}");
        extKey.SetValue(null, className);
        using var commandKey = root.CreateSubKey($@"{classesRoot}\{className}\shell\open\command");
        commandKey.SetValue(null, command);
    }

    private static void SetExtensionAssociationWithoutCommand(IRegistryKey root, string sid, string extension, string className)
    {
        var classesRoot = $@"{sid}\Software\Classes";
        using var extKey = root.CreateSubKey($@"{classesRoot}\{extension}");
        extKey.SetValue(null, className);
    }

    private static void SetProtocolAssociation(IRegistryKey root, string? sid, string protocol, string command)
    {
        var classesRoot = sid == null ? @"Software\Classes" : $@"{sid}\Software\Classes";
        using var protocolKey = root.CreateSubKey($@"{classesRoot}\{protocol}");
        protocolKey.SetValue("URL Protocol", "");
        using var commandKey = root.CreateSubKey($@"{classesRoot}\{protocol}\shell\open\command");
        commandKey.SetValue(null, command);
    }

    private static void SetDefaultIcon(IRegistryKey root, string? sid, string className, string iconPath)
    {
        var classesRoot = sid == null ? @"Software\Classes" : $@"{sid}\Software\Classes";
        using var iconKey = root.CreateSubKey($@"{classesRoot}\{className}\DefaultIcon");
        iconKey.SetValue(null, iconPath);
    }

    [Fact]
    public void ReadTargets_ScansTargetAccountInteractiveCurrentUserAndHklmScopes()
    {
        SetExtensionAssociation(_registry.HkuRoot, TargetAccountSid, ".txt", "TxtFile.Handler", @"""C:\AccountScope\TxtViewer.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, TargetAccountSid, "mailto", @"""C:\AccountScope\MailViewer.exe"" ""%1""");
        SetExtensionAssociation(_registry.HkuRoot, InteractiveSid, ".cmd", "CmdFile.Handler", @"""C:\InteractiveScope\Command.exe"" /c ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "http", @"""C:\InteractiveScope\Protocol.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, CurrentSid, "postman", @"""C:\CurrentScope\Postman.exe"" ""%1""");

        SetExtensionAssociation(_registry.HklmRoot, null, ".pdf", "PdfFile.Handler", @"""C:\ProgramData\PdfViewer.exe"" ""%1""");
        SetProtocolAssociation(_registry.HklmRoot, null, "myproto", @"""C:\ProgramData\Proto.exe"" ""%1""");

        var reader = CreateReader();
        var targets = reader.ReadTargets(TargetAccountSid);

        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount && t.AssociationKey == ".txt");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount && t.AssociationKey == "mailto");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.InteractiveUser && t.AssociationKey == ".cmd");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.InteractiveUser && t.AssociationKey == "http");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.CurrentUser && t.AssociationKey == "postman");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.Hklm && t.AssociationKey == ".pdf");
        Assert.Contains(targets, t => t.Scope == HandlerCommandTargetRegistryScope.Hklm && t.AssociationKey == "myproto");
    }

    [Fact]
    public void ReadTargets_WhenSameAssociationExistsInMultipleScopes_UsesFirstValidScope()
    {
        SetProtocolAssociation(_registry.HkuRoot, TargetAccountSid, "postman", @"""C:\Target\Postman.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "postman", @"""C:\Interactive\Postman.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, CurrentSid, "postman", @"""C:\Current\Postman.exe"" ""%1""");
        SetProtocolAssociation(_registry.HklmRoot, null, "postman", @"""C:\Hklm\Postman.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "vscode", @"""C:\Interactive\Code.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, CurrentSid, "vscode", @"""C:\Current\Code.exe"" ""%1""");
        SetProtocolAssociation(_registry.HkuRoot, CurrentSid, "current-only", @"""C:\Current\Only.exe"" ""%1""");
        SetProtocolAssociation(_registry.HklmRoot, null, "current-only", @"""C:\Hklm\Only.exe"" ""%1""");

        var targets = CreateReader().ReadTargets(TargetAccountSid);

        var postman = Assert.Single(targets, t => t.AssociationKey == "postman");
        Assert.Equal(HandlerCommandTargetRegistryScope.TargetAccount, postman.Scope);
        Assert.Equal(@"C:\Target\Postman.exe", postman.ResolvedPath);

        var vscode = Assert.Single(targets, t => t.AssociationKey == "vscode");
        Assert.Equal(HandlerCommandTargetRegistryScope.InteractiveUser, vscode.Scope);
        Assert.Equal(@"C:\Interactive\Code.exe", vscode.ResolvedPath);

        var currentOnly = Assert.Single(targets, t => t.AssociationKey == "current-only");
        Assert.Equal(HandlerCommandTargetRegistryScope.CurrentUser, currentOnly.Scope);
        Assert.Equal(@"C:\Current\Only.exe", currentOnly.ResolvedPath);
    }

    [Fact]
    public void ReadTargets_SkipsTargetAccountScope_WhenSidHiveIsMissing()
    {
        const string MissingSid = "S-1-5-21-9999";
        SetExtensionAssociation(_registry.HkuRoot, InteractiveSid, ".cmd", "CmdFile.Handler", @"""C:\InteractiveScope\Command.exe"" /c ""%1""");
        SetProtocolAssociation(_registry.HklmRoot, null, "mailto", @"""C:\ProgramData\MailViewer.exe"" ""%1""");

        var reader = CreateReader();
        var targets = reader.ReadTargets(MissingSid);

        Assert.DoesNotContain(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);
        Assert.Contains(targets, t => t.AssociationKey == ".cmd");
        Assert.Contains(targets, t => t.AssociationKey == "mailto");
    }

    [Fact]
    public void ReadTargets_FallsBackToHklmForUserExtensionCommand()
    {
        SetExtensionAssociationWithoutCommand(_registry.HkuRoot, InteractiveSid, ".log", "Log.Handler");
        SetExtensionAssociation(_registry.HklmRoot, null, ".log", "Log.Handler", @"""C:\ProgramData\LogViewer.exe"" ""%1""");

        var reader = CreateReader();
        var targets = reader.ReadTargets(InteractiveSid);

        Assert.Contains(targets, t => t.ResolvedPath.Equals(@"C:\ProgramData\LogViewer.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadTargets_HkcuExtensionWithMachineOnlyProgId_UsesHklmCommandAndHkcuScope()
    {
        SetExtensionAssociationWithoutCommand(_registry.HkuRoot, TargetAccountSid, ".abc", "MachineOnly.Handler");
        using var commandKey = _registry.HklmRoot.CreateSubKey(@"Software\Classes\MachineOnly.Handler\shell\open\command");
        commandKey.SetValue(null, @"""C:\Program Files\MachineOnly\Viewer.exe"" ""%1""");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(TargetAccountSid), t => t.AssociationKey == ".abc");

        Assert.Equal(HandlerCommandTargetRegistryScope.TargetAccount, target.Scope);
        Assert.Equal(@"C:\Program Files\MachineOnly\Viewer.exe", target.ResolvedPath);
    }

    [Fact]
    public void ReadTargets_HkcuExtensionWithHklmFallbackCommand_UsesPrioritizedHkcuScope()
    {
        SetExtensionAssociationWithoutCommand(_registry.HkuRoot, InteractiveSid, ".log", "Log.Handler");
        using var hkuProgIdKey = _registry.HkuRoot.CreateSubKey($@"{InteractiveSid}\Software\Classes\Log.Handler");
        using var commandKey = _registry.HklmRoot.CreateSubKey(@"Software\Classes\Log.Handler\shell\open\command");
        commandKey.SetValue(null, @"""C:\ProgramData\LogViewer.exe"" ""%1""");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(InteractiveSid), t => t.AssociationKey == ".log" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(HandlerCommandTargetRegistryScope.TargetAccount, target.Scope);
        Assert.Equal(@"C:\ProgramData\LogViewer.exe", target.ResolvedPath);
    }

    [Fact]
    public void ReadTargets_ExtensionUsesCurrentScopeDefaultIconBeforeHklmFallback()
    {
        SetExtensionAssociation(_registry.HkuRoot, InteractiveSid, ".log", "Log.Handler", @"""C:\CommandPath\LogViewer.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "Log.Handler", @"""C:\RegIcons\LogViewer.exe"",2");
        SetDefaultIcon(_registry.HklmRoot, null, "Log.Handler", @"""C:\HklmIcons\LogViewer.exe"",1");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(InteractiveSid), t => t.AssociationKey == ".log" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"""C:\RegIcons\LogViewer.exe"",2", target.DefaultIconRawValue);
        Assert.Equal(@"C:\RegIcons\LogViewer.exe", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadTargets_UsesHklmDefaultIconWhenExtensionProgIdHasNoUserDefaultIcon()
    {
        SetExtensionAssociationWithoutCommand(_registry.HkuRoot, InteractiveSid, ".log", "Log.Handler");
        using var commandKey = _registry.HklmRoot.CreateSubKey(@"Software\Classes\Log.Handler\shell\open\command");
        commandKey.SetValue(null, @"""C:\ProgramData\LogViewer.exe"" ""%1""");
        SetDefaultIcon(_registry.HklmRoot, null, "Log.Handler", @"""C:\HklmIcons\LogViewer.exe""");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(InteractiveSid), t => t.AssociationKey == ".log" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"""C:\HklmIcons\LogViewer.exe""", target.DefaultIconRawValue);
        Assert.Equal(@"C:\HklmIcons\LogViewer.exe", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadTargets_BlankUserDefaultIcon_FallsBackToHklmDefaultIcon()
    {
        SetExtensionAssociationWithoutCommand(_registry.HkuRoot, InteractiveSid, ".log", "Log.Handler");
        using var commandKey = _registry.HklmRoot.CreateSubKey(@"Software\Classes\Log.Handler\shell\open\command");
        commandKey.SetValue(null, @"""C:\ProgramData\LogViewer.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "Log.Handler", "   ");
        SetDefaultIcon(_registry.HklmRoot, null, "Log.Handler", @"""C:\HklmIcons\LogViewer.exe""");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(InteractiveSid), t => t.AssociationKey == ".log" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"""C:\HklmIcons\LogViewer.exe""", target.DefaultIconRawValue);
        Assert.Equal(@"C:\HklmIcons\LogViewer.exe", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadTargets_ProtocolUsesOnlyCurrentScopeDefaultIcon()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "myproto", @"""C:\InteractiveScope\Proto.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "myproto", @"""C:\InteractiveScope\Proto.ico""");
        SetProtocolAssociation(_registry.HklmRoot, null, "myproto", @"""C:\ProgramData\Proto.exe"" ""%1""");
        SetDefaultIcon(_registry.HklmRoot, null, "myproto", @"""C:\ProgramData\Proto.ico""");

        var reader = CreateReader();
        var target = Assert.Single(reader.ReadTargets(InteractiveSid), t => t.AssociationKey == "myproto" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"""C:\InteractiveScope\Proto.ico""", target.DefaultIconRawValue);
        Assert.Equal(@"C:\InteractiveScope\Proto.ico", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_ParsesQuotedIconPathWithIndex()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "quoted", @"""C:\Icons\Quoted.ico"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "quoted", @"""C:\Icons\Quoted.exe"",2");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "quoted").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.True(target.HasExplicitDefaultIcon);
        Assert.Equal(@"""C:\Icons\Quoted.exe"",2", target.DefaultIconRawValue);
        Assert.Equal(@"C:\Icons\Quoted.exe", target.ResolvedDefaultIconPath);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_ParsesCommaSeparatedIconPath()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "comma", @"""C:\Icons\Comma.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "comma", @"C:\Icons\Comma.exe,1");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "comma").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"C:\Icons\Comma.exe", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_ParsesQuotedPathWithCommaInsidePath()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "comma", @"""C:\Icons\Comma.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "comma", @"""C:\Icons\With,Comma.exe"",2");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "comma").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal(@"C:\Icons\With,Comma.exe", target.ResolvedDefaultIconPath);
        Assert.True(target.HasExplicitDefaultIcon);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_ParsesEnvironmentExpandedIconPath()
    {
        var previousValue = Environment.GetEnvironmentVariable("RF_ICON_TEST", EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable("RF_ICON_TEST", @"C:\Icons", EnvironmentVariableTarget.Process);
            SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "env", @"""C:\Icons\Env.exe"" ""%1""");
            SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "env", @"%RF_ICON_TEST%\EnvIcon.ico,0");

            var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "env").ToList();
            var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

            Assert.Equal(@"C:\Icons\EnvIcon.ico", target.ResolvedDefaultIconPath);
            Assert.True(target.HasExplicitDefaultIcon);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RF_ICON_TEST", previousValue, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void ReadPreferredDefaultIcon_EmptyDefaultIconValue_ReturnsNullResolvedPath()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "empty", @"""C:\Icons\Empty.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "empty", @"   ");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "empty").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.False(target.HasExplicitDefaultIcon);
        Assert.Null(target.ResolvedDefaultIconPath);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_MalformedDefaultIconValue_ReturnsNullResolvedPath()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "malformed", @"""C:\Icons\Bad.exe"" ""%1""");
        SetDefaultIcon(_registry.HkuRoot, InteractiveSid, "malformed", @"""C:\Icons\Bad.exe");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "malformed").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.True(target.HasExplicitDefaultIcon);
        Assert.Null(target.ResolvedDefaultIconPath);
    }

    [Fact]
    public void ReadPreferredDefaultIcon_FallsBackToHklmAndParsesPath()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "fallback", @"""C:\User\Fallback.exe"" ""%1""");
        SetDefaultIcon(_registry.HklmRoot, null, "fallback", @"""C:\Hklm\Fallback.ico""");

        var targets = CreateReader().ReadTargets(InteractiveSid).Where(t => t.AssociationKey == "fallback").ToList();
        var target = Assert.Single(targets, t => t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.True(target.HasExplicitDefaultIcon);
        Assert.Equal(@"C:\Hklm\Fallback.ico", target.ResolvedDefaultIconPath);
    }

    [Fact]
    public void ReadTargets_SkipsRunFenceProgIdExtensions()
    {
        SetExtensionAssociation(
            _registry.HkuRoot,
            InteractiveSid,
            ".rf",
            $"{PathConstants.HandlerProgIdPrefix}.rf",
            @"""C:\RunFence\Runner.exe"" ""%1""");

        var reader = CreateReader();
        var targets = reader.ReadTargets(InteractiveSid);

        Assert.DoesNotContain(targets, t => string.Equals(t.AssociationKey, ".rf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadTargets_ScansUrlProtocolKeys()
    {
        SetProtocolAssociation(_registry.HkuRoot, InteractiveSid, "myproto", @"""C:\InteractiveScope\Proto.exe"" ""%1""");

        var reader = CreateReader();
        var targets = reader.ReadTargets(InteractiveSid);
        Assert.Contains(targets, t => t.AssociationKey == "myproto" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);
        var target = Assert.Single(targets, t => t.AssociationKey == "myproto" && t.Scope == HandlerCommandTargetRegistryScope.TargetAccount);

        Assert.Equal("myproto", target.AssociationKey);
        Assert.Equal(@"C:\InteractiveScope\Proto.exe", target.ResolvedPath);
        Assert.Equal(HandlerCommandTargetRegistryScope.TargetAccount, target.Scope);
    }

    private sealed class TestHkuRootProvider : IHkuRootProvider
    {
        private readonly InMemoryRegistryKey _root;

        public TestHkuRootProvider(InMemoryRegistryKey root)
        {
            _root = root;
        }

        public IRegistryKey OpenUsersRoot()
            => _root;
    }

    private sealed class TestHklmClassesRootProvider : IHklmClassesRootProvider
    {
        private readonly InMemoryRegistryKey _root;

        public TestHklmClassesRootProvider(InMemoryRegistryKey root)
        {
            using var _ = root.CreateSubKey(@"Software\Classes");
            _root = root;
        }

        public IRegistryKey OpenClassesRoot()
            => _root.OpenSubKey(@"Software\Classes")
               ?? throw new InvalidOperationException("Test HKLM classes root is unavailable.");
    }
}
