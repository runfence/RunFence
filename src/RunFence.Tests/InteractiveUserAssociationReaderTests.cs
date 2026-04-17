using Microsoft.Win32;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="InteractiveUserAssociationReader"/>.
/// Uses Registry.CurrentUser as HKU/HKLM override with a fake SID.
/// Interactive user SID is provided via a mocked <see cref="IInteractiveUserResolver"/>.
/// Registry hive infrastructure is shared via <see cref="RegistryTestHelper"/>.
/// </summary>
public class InteractiveUserAssociationReaderTests : IDisposable
{
    private const string TestSid = "S-1-5-21-7777-8888-9999-1001";
    private const string LauncherPath = @"C:\RunFence\RunFence.Launcher.exe";

    private readonly RegistryTestHelper _registry = new("IUARHku", "IUARHklm");
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver;

    public InteractiveUserAssociationReaderTests()
    {
        _interactiveUserResolver = new Mock<IInteractiveUserResolver>();
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(TestSid);
    }

    public void Dispose() => _registry.Dispose();

    private InteractiveUserAssociationReader CreateReader()
        => new(_registry.HiveManager.Object, _interactiveUserResolver.Object, _registry.HkuRoot, _registry.HklmRoot);

    private void SetHkuExtension(string ext, string progId)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{ext}");
        key.SetValue(null, progId);
    }

    private void SetHkuCommand(string classPath, string command)
    {
        using var key = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{classPath}\shell\open\command");
        key.SetValue(null, command);
    }

    private void SetHklmCommand(string classPath, string command)
    {
        using var key = _registry.HklmRoot.CreateSubKey($@"Software\Classes\{classPath}\shell\open\command");
        key.SetValue(null, command);
    }

    private void SetHkuProtocol(string proto, string command)
    {
        using var protoKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{proto}");
        protoKey.SetValue("URL Protocol", string.Empty);
        using var cmdKey = _registry.HkuRoot.CreateSubKey($@"{TestSid}\Software\Classes\{proto}\shell\open\command");
        cmdKey.SetValue(null, command);
    }

    private void SetUserChoice(string ext, string progId)
    {
        using var key = _registry.HkuRoot.CreateSubKey(
            $@"{TestSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
        key.SetValue("ProgId", progId);
    }

    // --- Extension resolution: class-based (HKLM) ---

    [Fact]
    public void Extension_WithHkuDefaultAndHklmClass_ReturnsClassName()
    {
        SetHkuExtension(".txt", "txtfile");
        SetHklmCommand("txtfile", @"""C:\Windows\system32\notepad.exe"" %1");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal(".txt", entries[0].Key);
        Assert.Equal("txtfile", entries[0].Handler.ClassName);
        Assert.Null(entries[0].Handler.Command);
    }

    // --- Extension resolution: command-based (HKU only) ---

    [Fact]
    public void Extension_WithHkuDefaultAndHkuClassOnly_ReturnsCommand()
    {
        SetHkuExtension(".log", "myeditor");
        SetHkuCommand("myeditor", @"""C:\apps\myeditor.exe"" ""%1""");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal(".log", entries[0].Key);
        Assert.Equal(@"""C:\apps\myeditor.exe"" ""%1""", entries[0].Handler.Command);
        Assert.Null(entries[0].Handler.ClassName);
    }

    // --- Extension resolution: filters RunFence ProgIds ---

    [Fact]
    public void Extension_WithRunFenceProgId_IsSkipped()
    {
        SetHkuExtension(".pdf", "RunFence_.pdf");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Empty(entries);
    }

    // --- Extension resolution via UserChoice ---

    [Fact]
    public void Extension_ViaUserChoice_WhenNoHkuDefault()
    {
        SetUserChoice(".docx", "Word.Document.16");
        SetHklmCommand("Word.Document.16", @"""C:\Program Files\Word.exe"" ""%1""");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal(".docx", entries[0].Key);
        Assert.Equal("Word.Document.16", entries[0].Handler.ClassName);
    }

    [Fact]
    public void Extension_UserChoice_RunFenceProgId_IsSkipped()
    {
        SetUserChoice(".pdf", "RunFence_.pdf");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Empty(entries);
    }

    // --- Protocol resolution ---

    [Fact]
    public void Protocol_WithUrlProtocol_ReturnsCommand()
    {
        SetHkuProtocol("mailto", @"""C:\apps\mail.exe"" %1");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal("mailto", entries[0].Key);
        Assert.Equal(@"""C:\apps\mail.exe"" %1", entries[0].Handler.Command);
    }

    [Fact]
    public void Protocol_RunFenceLauncherCommand_IsSkipped()
    {
        SetHkuProtocol("mailto", $"\"{LauncherPath}\" --resolve \"mailto\" %1");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Empty(entries);
    }

    // --- Null interactive SID ---

    [Fact]
    public void GetInteractiveUserAssociations_NullSid_ReturnsEmpty()
    {
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Empty(entries);
    }

    // --- GetAssociationHandler ---

    [Fact]
    public void GetAssociationHandler_Extension_ReturnsHandler()
    {
        SetHkuExtension(".txt", "txtfile");
        SetHklmCommand("txtfile", @"""C:\Windows\system32\notepad.exe"" %1");

        var handler = CreateReader().GetAssociationHandler(".txt");

        Assert.NotNull(handler);
        Assert.Equal("txtfile", handler.Value.ClassName);
    }

    [Fact]
    public void GetAssociationHandler_Protocol_ReturnsCommand()
    {
        SetHkuProtocol("skype", @"""C:\apps\skype.exe"" %1");

        var handler = CreateReader().GetAssociationHandler("skype");

        Assert.NotNull(handler);
        Assert.Equal(@"""C:\apps\skype.exe"" %1", handler.Value.Command);
    }

    [Fact]
    public void GetAssociationHandler_NoEntry_ReturnsNull()
    {
        var handler = CreateReader().GetAssociationHandler(".xyz");

        Assert.Null(handler);
    }

    // --- Caching ---

    [Fact]
    public void GetInteractiveUserAssociations_ReturnsCachedResult()
    {
        SetHkuExtension(".txt", "txtfile");
        SetHklmCommand("txtfile", @"""C:\notepad.exe"" %1");

        var reader = CreateReader();
        var first = reader.GetInteractiveUserAssociations();

        // Modify registry after first call
        SetHkuExtension(".py", "pyfile");

        var second = reader.GetInteractiveUserAssociations();

        // Cache used — second call returns same instance
        Assert.Same(first, second);
    }

    // --- Description ---

    [Fact]
    public void Entry_Description_IsExeNameWithoutExtension()
    {
        SetHkuProtocol("mailto", @"""C:\apps\my-mail-client.exe"" %1");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal("my-mail-client", entries[0].Description);
    }

    [Fact]
    public void Entry_ClassBased_DescriptionIsClassName()
    {
        SetHkuExtension(".txt", "txtfile");
        SetHklmCommand("txtfile", @"""C:\notepad.exe"" %1");

        var entries = CreateReader().GetInteractiveUserAssociations();

        Assert.Single(entries);
        Assert.Equal("txtfile", entries[0].Description);
    }
}
