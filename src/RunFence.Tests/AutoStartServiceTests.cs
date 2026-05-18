using Moq;
using System.Dynamic;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class AutoStartServiceTests
{
    private sealed class InMemoryShortcutDefinition : DynamicObject
    {
        public string? TargetPath { get; set; }
        public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? Description { get; set; }
        public int? WindowStyle { get; set; }
        public string? IconLocation { get; set; }
        public int SaveCallCount { get; private set; }

        public void Save() => SaveCallCount++;

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            if (string.Equals(binder.Name, nameof(TargetPath), StringComparison.OrdinalIgnoreCase))
            {
                TargetPath = value as string;
                return true;
            }

            if (string.Equals(binder.Name, nameof(Arguments), StringComparison.OrdinalIgnoreCase))
            {
                Arguments = value as string;
                return true;
            }

            if (string.Equals(binder.Name, nameof(WorkingDirectory), StringComparison.OrdinalIgnoreCase))
            {
                WorkingDirectory = value as string;
                return true;
            }

            if (string.Equals(binder.Name, nameof(Description), StringComparison.OrdinalIgnoreCase))
            {
                Description = value as string;
                return true;
            }

            if (string.Equals(binder.Name, nameof(WindowStyle), StringComparison.OrdinalIgnoreCase))
            {
                if (value == null)
                {
                    WindowStyle = null;
                }
                else if (value is int intValue)
                {
                    WindowStyle = intValue;
                }
                else
                {
                    try
                    {
                        WindowStyle = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return false;
                    }
                }

                return true;
            }

            if (string.Equals(binder.Name, nameof(IconLocation), StringComparison.OrdinalIgnoreCase))
            {
                IconLocation = value as string;
                return true;
            }

            return false;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (string.Equals(binder.Name, nameof(TargetPath), StringComparison.OrdinalIgnoreCase))
            {
                result = TargetPath;
                return true;
            }

            if (string.Equals(binder.Name, nameof(Arguments), StringComparison.OrdinalIgnoreCase))
            {
                result = Arguments;
                return true;
            }

            if (string.Equals(binder.Name, nameof(WorkingDirectory), StringComparison.OrdinalIgnoreCase))
            {
                result = WorkingDirectory;
                return true;
            }

            if (string.Equals(binder.Name, nameof(Description), StringComparison.OrdinalIgnoreCase))
            {
                result = Description;
                return true;
            }

            if (string.Equals(binder.Name, nameof(WindowStyle), StringComparison.OrdinalIgnoreCase))
            {
                result = WindowStyle;
                return true;
            }

            if (string.Equals(binder.Name, nameof(IconLocation), StringComparison.OrdinalIgnoreCase))
            {
                result = IconLocation;
                return true;
            }

            result = null;
            return false;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            if (string.Equals(binder.Name, nameof(Save), StringComparison.OrdinalIgnoreCase) &&
                args is { Length: 0 })
            {
                Save();
                result = null;
                return true;
            }

            result = null;
            return base.TryInvokeMember(binder, args, out result);
        }
    }

    private sealed class InMemoryShortcutComHelper : IShortcutComHelper
    {
        private readonly Dictionary<string, InMemoryShortcutDefinition> _shortcuts = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, InMemoryShortcutDefinition> Shortcuts => _shortcuts;
        public List<string> AccessedPaths { get; } = [];

        public InMemoryShortcutDefinition GetShortcut(string path) =>
            _shortcuts[path];

        public void SeedShortcut(string path, string targetPath)
        {
            GetOrCreate(path).TargetPath = targetPath;
        }

        public T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action)
        {
            AccessedPaths.Add(shortcutPath);
            dynamic shortcut = GetOrCreate(shortcutPath);
            return action(shortcut);
        }

        public void WithShortcut(string shortcutPath, Action<dynamic> action)
        {
            AccessedPaths.Add(shortcutPath);
            dynamic shortcut = GetOrCreate(shortcutPath);
            action(shortcut);
        }

        public ShortcutDefinition GetShortcutDefinition(string shortcutPath)
        {
            var shortcut = GetOrCreate(shortcutPath);
            return new ShortcutDefinition(
                shortcutPath,
                shortcut.TargetPath,
                shortcut.Arguments,
                shortcut.WorkingDirectory);
        }

        private InMemoryShortcutDefinition GetOrCreate(string path) =>
            _shortcuts.TryGetValue(path, out var shortcut)
                ? shortcut
                : _shortcuts[path] = new InMemoryShortcutDefinition();
    }

    // Fake shortcut store that works entirely in-memory, never touching the real Startup folder.
    private sealed class FakeShortcutStore : IAutoStartShortcutStore
    {
        private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _deletedFiles = [];

        public string RunFenceExePath { get; set; } = @"C:\RunFence\RunFence.exe";
        public string CmdWrapperPath { get; set; } = @"C:\RunFence\RunFence-autostart.cmd";
        public string PrimaryShortcutPath { get; set; } = @"C:\FakeStartup\RunFence.lnk";
        public IReadOnlyCollection<string> ShortcutPaths =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PrimaryShortcutPath };

        public IReadOnlyList<string> DeletedFiles => _deletedFiles;

        public void AddExistingFile(string path) => _existingFiles.Add(path);

        public bool FileExists(string path) => _existingFiles.Contains(path);

        public void DeleteFile(string path)
        {
            _existingFiles.Remove(path);
            _deletedFiles.Add(path);
        }
    }

    // Fake operation runner that simulates instant timeout without actually blocking.
    private sealed class TimeoutOperationRunner : IShortcutOperationRunner
    {
        public bool WasCalled { get; private set; }

        public T Run<T>(Func<T> operation, string operationName, T timeoutValue)
        {
            WasCalled = true;
            return timeoutValue;
        }

        public void Run(Action operation, string operationName)
        {
            WasCalled = true;
            throw new TimeoutException($"Simulated timeout for '{operationName}'.");
        }
    }

    // Fake operation runner that executes the operation normally (no timeout).
    private sealed class PassthroughOperationRunner : IShortcutOperationRunner
    {
        public T Run<T>(Func<T> operation, string operationName, T timeoutValue) => operation();

        public void Run(Action operation, string operationName) => operation();
    }

    private static AutoStartService BuildService(
        ILoggingService log,
        IShortcutComHelper shortcutComHelper,
        IShortcutOperationRunner operationRunner,
        IAutoStartShortcutStore shortcutStore) =>
        new(log, shortcutComHelper, operationRunner, shortcutStore);

    // ---- IsAutoStartEnabled ----

    [Fact]
    public async Task IsAutoStartEnabled_ReturnsFalse_WhenShortcutComOperationTimesOut()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var timeoutRunner = new TimeoutOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, timeoutRunner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
        Assert.True(timeoutRunner.WasCalled);
    }

    [Fact]
    public async Task IsAutoStartEnabled_ReturnsFalse_WhenNoShortcutFileExists()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
        Assert.Empty(shortcutHelper.AccessedPaths);
    }

    [Fact]
    public async Task IsAutoStartEnabled_ReturnsFalse_WhenShortcutTargetIsUnrelated()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();
        shortcutHelper.SeedShortcut(store.PrimaryShortcutPath, @"C:\Other\App.exe");

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
        Assert.Single(shortcutHelper.AccessedPaths);
        Assert.Equal(store.PrimaryShortcutPath, shortcutHelper.AccessedPaths[0]);
    }

    // ---- EnableAutoStart ----

    [Fact]
    public async Task EnableAutoStart_ThrowsTimeoutException_WhenShortcutComOperationTimesOut()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.CmdWrapperPath);
        var timeoutRunner = new TimeoutOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, timeoutRunner, store);

        await Assert.ThrowsAsync<TimeoutException>(() => service.EnableAutoStart());
        Assert.True(timeoutRunner.WasCalled);
        Assert.Empty(shortcutHelper.AccessedPaths);
    }

    [Fact]
    public async Task EnableAutoStart_LogsSuccess_WhenShortcutCreated()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.CmdWrapperPath);
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        await service.EnableAutoStart();

        var created = shortcutHelper.GetShortcut(store.PrimaryShortcutPath);
        Assert.Equal(store.CmdWrapperPath, created.TargetPath);
        Assert.Equal(string.Empty, created.Arguments);
        Assert.Equal(AppContext.BaseDirectory, created.WorkingDirectory);
        Assert.Equal("RunFence auto-start", created.Description);
        Assert.Equal(7, created.WindowStyle);
        Assert.Equal($"{store.RunFenceExePath},0", created.IconLocation);
        Assert.Equal(1, created.SaveCallCount);
        Assert.Single(shortcutHelper.AccessedPaths);
        Assert.Equal(store.PrimaryShortcutPath, shortcutHelper.AccessedPaths[0]);
        log.Verify(l => l.Info(It.Is<string>(s => s.Contains(store.PrimaryShortcutPath))), Times.Once);
    }

    [Fact]
    public async Task EnableAutoStart_ThrowsFileNotFoundException_WhenCmdWrapperMissing()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.EnableAutoStart());
        Assert.Empty(shortcutHelper.AccessedPaths);
    }

    // ---- DisableAutoStart ----

    [Fact]
    public async Task DisableAutoStart_DoesNotHang_WhenShortcutFilesAreMissing()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        await service.DisableAutoStart();

        Assert.Empty(store.DeletedFiles);
        log.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DisableAutoStart_DeletesExistingShortcutAndLogs()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        await service.DisableAutoStart();

        Assert.Contains(store.PrimaryShortcutPath, store.DeletedFiles, StringComparer.OrdinalIgnoreCase);
        log.Verify(l => l.Info(It.Is<string>(s => s.Contains(store.PrimaryShortcutPath))), Times.Once);
    }

    // ---- Backward-compatible shortcut target acceptance ----

    [Theory]
    [InlineData(true)] // target is CmdWrapperPath
    [InlineData(false)] // target is RunFenceExePath (legacy direct shortcut)
    public async Task IsAutoStartEnabled_AcceptsBothCmdWrapperAndLegacyExeTarget(bool targetIsCmd)
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();
        shortcutHelper.SeedShortcut(
            store.PrimaryShortcutPath,
            targetIsCmd ? store.CmdWrapperPath : store.RunFenceExePath);

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.True(result);
    }

    // ---- Store supplies paths; tests never touch real Startup folder ----

    [Fact]
    public async Task IsAutoStartEnabled_UsesShortcutPathsFromStore()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore
        {
            PrimaryShortcutPath = @"C:\TestStartup\RunFence.lnk"
        };

        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
        Assert.Empty(shortcutHelper.AccessedPaths);
    }

    [Fact]
    public async Task EnableAutoStart_CallsShortcutHelperAtPrimaryShortcutPath()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore
        {
            RunFenceExePath = @"C:\Custom\RunFence.exe",
            CmdWrapperPath = @"C:\Custom\RunFence-autostart.cmd",
            PrimaryShortcutPath = @"C:\TestStartup\RunFence.lnk"
        };
        store.AddExistingFile(store.CmdWrapperPath);

        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new InMemoryShortcutComHelper();

        var service = BuildService(log.Object, shortcutHelper, runner, store);

        await service.EnableAutoStart();

        Assert.Single(shortcutHelper.AccessedPaths);
        Assert.Equal(store.PrimaryShortcutPath, shortcutHelper.AccessedPaths[0]);
    }
}
