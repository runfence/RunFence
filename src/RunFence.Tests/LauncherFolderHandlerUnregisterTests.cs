using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Launch;
using RunFence.Launcher;
using Xunit;

namespace RunFence.Tests;

public class LauncherFolderHandlerUnregisterTests : IDisposable
{
    private const string ClassesRootRelativePath = @"Software\Classes";
    private const string ShellWindowsClsidRelativePath =
        @"CLSID\{9BA05972-F6A8-11CF-A442-00A0C90A8F39}";

    private readonly string _testRootPath;
    private readonly RegistryKey _testRoot;

    public LauncherFolderHandlerUnregisterTests()
    {
        _testRootPath = $@"Software\RunFenceTests\LauncherFolderHandler\{Guid.NewGuid():N}";
        _testRoot = Registry.CurrentUser.CreateSubKey(_testRootPath)
            ?? throw new InvalidOperationException("Failed to create test registry root.");
    }

    public void Dispose()
    {
        _testRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testRootPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Unregister_RemovesOnlyOwnedCommandLeaves_AndPreservesNonEmptyParents()
    {
        using (var ownedOpenCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"))
        {
            ownedOpenCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --open-folder ""%V""");
        }

        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
        }

        using (var folderCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Folder\shell\open\command"))
        {
            folderCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var folderOpenKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Folder\shell\open"))
        {
            folderOpenKey.SetValue("MuiVerb", "OtherOpen");
        }

        var handler = CreateHandler();

        var exitCode = handler.Unregister();

        Assert.Equal(0, exitCode);
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"));
        Assert.Equal("custom", _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell")!.GetValue(null) as string);
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Folder\shell\open\command"));
        Assert.Equal(
            @"""C:\Other\App.exe"" ""%1""",
            _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Folder\shell\open\command")!.GetValue(null) as string);
    }

    [Fact]
    public void Unregister_RestoresDirectoryShellFallback_WhenDefaultVerbIsOwned()
    {
        using (var ownedOpenCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"))
        {
            ownedOpenCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --open-folder ""%V""");
        }

        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }

        var handler = CreateHandler();

        var exitCode = handler.Unregister();

        Assert.Equal(0, exitCode);
        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.NotNull(shellKey);
        Assert.Equal("custom", shellKey.GetValue(null) as string);
        Assert.Null(shellKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_DeletesDirectoryShellDefault_WhenFallbackIsEmptyAndDefaultVerbIsOwned()
    {
        using (var ownedOpenCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"))
        {
            ownedOpenCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --open-folder ""%V""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, string.Empty);
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.True(shellKey == null || shellKey.GetValue(null) == null);
        Assert.Null(shellKey?.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"));
    }

    [Fact]
    public void Unregister_DeletesDirectoryShellFallbackWithoutRestoring_WhenDefaultVerbIsForeign()
    {
        using (var ownedOpenCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"))
        {
            ownedOpenCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --open-folder ""%V""");
        }

        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.NotNull(shellKey);
        Assert.Equal("custom", shellKey.GetValue(null) as string);
        Assert.Null(shellKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_DeletesStaleDirectoryShellFallback_WhenNoOwnedCommandRemains()
    {
        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.NotNull(shellKey);
        Assert.Equal("custom", shellKey.GetValue(null) as string);
        Assert.Null(shellKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_RestoresDirectoryShellFallback_WhenOpenDefaultCommandIsAlreadyMissing()
    {
        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.NotNull(shellKey);
        Assert.Equal("custom", shellKey.GetValue(null) as string);
        Assert.Null(shellKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_RestoresDirectoryShellFallback_WhenOpenDefaultCommandValueIsMissing()
    {
        using (var customVerbCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"))
        {
            customVerbCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command")?.Dispose();

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.NotNull(shellKey);
        Assert.Equal("custom", shellKey.GetValue(null) as string);
        Assert.Null(shellKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_DeletesDirectoryShellDefault_WhenOwnedDefaultHasNoFallback()
    {
        using (var ownedOpenCommand = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"))
        {
            ownedOpenCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --open-folder ""%V""");
        }

        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
        }

        var handler = CreateHandler();

        handler.Unregister();

        using var shellKey = _testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell");
        Assert.True(shellKey == null || shellKey.GetValue(null) == null);
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell\open\command"));
    }

    [Fact]
    public void Unregister_DeletesStaleDirectoryShellFallback_WhenDirectoryShellBecomesEmpty()
    {
        using (var directoryShellKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\Directory\shell"))
        {
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }

        var handler = CreateHandler();

        handler.Unregister();

        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\Directory\shell"));
    }

    [Fact]
    public void Unregister_RemovesOnlyOwnedClsidEntries_AndPreservesForeignOverride()
    {
        using (var ownedClsidCommand = _testRoot.CreateSubKey(
                   $@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}\shell\open\command"))
        {
            ownedClsidCommand.SetValue(null, @"""C:\Program Files\RunFence\RunFence.Launcher.exe"" --shell-windows");
        }

        using (var ownedLocalServer = _testRoot.CreateSubKey(
                   $@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}\LocalServer32"))
        {
            ownedLocalServer.SetValue(null, @"""C:\Program Files\RunFence\RunFence.ShellServer.exe""");
        }

        using (var clsidKey = _testRoot.CreateSubKey($@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}"))
        {
            clsidKey.SetValue("SharedValue", "keep");
        }

        using (var foreignLocalServer = _testRoot.CreateSubKey(
                   $@"{ClassesRootRelativePath}\CLSID\{{00000000-0000-0000-0000-000000000001}}\LocalServer32"))
        {
            foreignLocalServer.SetValue(null, @"""C:\Other\ShellServer.exe""");
        }

        var handler = CreateHandler();

        handler.Unregister();

        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}\shell\open\command"));
        Assert.Null(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}\LocalServer32"));
        Assert.NotNull(_testRoot.OpenSubKey($@"{ClassesRootRelativePath}\{ShellWindowsClsidRelativePath}"));
        Assert.NotNull(_testRoot.OpenSubKey(
            $@"{ClassesRootRelativePath}\CLSID\{{00000000-0000-0000-0000-000000000001}}\LocalServer32"));
    }

    private TestLauncherOpenFolderHandler CreateHandler()
    {
        return new TestLauncherOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            _testRootPath);
    }

    private sealed class TestLauncherOpenFolderHandler : OpenFolderHandler
    {
        private readonly string _rootPath;

        public TestLauncherOpenFolderHandler(
            ILauncherIpcCommandSender sender,
            ILauncherProcessStarter starter,
            string rootPath)
            : base(sender, starter)
        {
            _rootPath = rootPath;
        }

        protected override void NotifyShellAssociationsChanged()
        {
        }

        protected override string GetClassesRootPath() => $@"{_rootPath}\{ClassesRootRelativePath}";
    }
}
