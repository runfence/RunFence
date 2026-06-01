using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Launch;
using RunFence.Launcher;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class LauncherFolderHandlerUnregisterTests : IDisposable
{
    private const string ClassesRootRelativePath = @"Software\Classes";
    private const string ShellWindowsClsidRelativePath =
        @"CLSID\{9BA05972-F6A8-11CF-A442-00A0C90A8F39}";

    private readonly InMemoryRegistryKey _testRoot;

    public LauncherFolderHandlerUnregisterTests()
    {
        _testRoot = InMemoryRegistryKey.CreateRoot("LauncherFolderHandler");
    }

    public void Dispose()
    {
        _testRoot.Dispose();
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

    [Fact]
    public void Unregister_DisposesOpenedRegistryRoot()
    {
        var root = new TrackingRegistryRoot(_testRoot);
        var handler = new TestLauncherOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            new RecordingNotifier(),
            root);

        handler.Unregister();

        Assert.True(root.Disposed);
    }

    [Fact]
    public void Unregister_DisposesOpenedRegistryRoot_WhenShellNotificationFails()
    {
        var root = new TrackingRegistryRoot(_testRoot);
        var handler = new TestLauncherOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            new RecordingNotifier(),
            root)
        {
            ThrowOnShellNotification = true
        };

        handler.Unregister();

        Assert.True(root.Disposed);
    }

    private TestLauncherOpenFolderHandler CreateHandler()
    {
        return new TestLauncherOpenFolderHandler(
            new RecordingLauncherIpcCommandSender(),
            new TrackingProcessStarter(),
            new RecordingNotifier(),
            _testRoot);
    }

    private sealed class TestLauncherOpenFolderHandler : OpenFolderHandler
    {
        private readonly IRegistryKey _root;

        public TestLauncherOpenFolderHandler(
            ILauncherIpcCommandSender sender,
            ILauncherProcessStarter starter,
            ILauncherUserNotifier notifier,
            IRegistryKey root)
            : base(sender, starter, notifier)
        {
            _root = root;
        }

        public bool ThrowOnShellNotification { get; init; }

        protected override void NotifyShellAssociationsChanged()
        {
            if (ThrowOnShellNotification)
                throw new InvalidOperationException("launcher test failure");
        }

        protected override IRegistryKey OpenRegistryRoot() => _root;

        protected override string GetClassesRootPath() => ClassesRootRelativePath;
    }

    private sealed class TrackingRegistryRoot(IRegistryKey inner) : IRegistryKey
    {
        public bool Disposed { get; private set; }

        public string Name => inner.Name;

        public int SubKeyCount => inner.SubKeyCount;

        public int ValueCount => inner.ValueCount;

        public IRegistryKey? OpenSubKey(string name, bool writable = false)
            => inner.OpenSubKey(name, writable);

        public IRegistryKey CreateSubKey(string subkey)
            => inner.CreateSubKey(subkey);

        public void DeleteSubKey(string subkey, bool throwOnMissingSubKey = true)
            => inner.DeleteSubKey(subkey, throwOnMissingSubKey);

        public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = true)
            => inner.DeleteSubKeyTree(subkey, throwOnMissingSubKey);

        public object? GetValue(string? name)
            => inner.GetValue(name);

        public RegistryValueKind GetValueKind(string? name)
            => inner.GetValueKind(name);

        public string[] GetValueNames()
            => inner.GetValueNames();

        public string[] GetSubKeyNames()
            => inner.GetSubKeyNames();

        public void SetValue(string? name, object value, RegistryValueKind valueKind = RegistryValueKind.String)
            => inner.SetValue(name, value, valueKind);

        public void DeleteValue(string? name, bool throwOnMissingValue = true)
            => inner.DeleteValue(name, throwOnMissingValue);

        public void Flush()
            => inner.Flush();

        public void Dispose()
        {
            Disposed = true;
            inner.Dispose();
        }
    }
}
