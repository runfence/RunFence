using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class AutoStartServiceTests
{
    // Fake shortcut store that works entirely in-memory, never touching the real Startup folder.
    private sealed class FakeShortcutStore : IAutoStartShortcutStore
    {
        private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _deletedFiles = new();

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
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, timeoutRunner, store);

        // Act: shortcut file exists but reading its target times out
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
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
    }

    [Fact]
    public async Task IsAutoStartEnabled_ReturnsFalse_WhenShortcutTargetIsUnrelated()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();

        var shortcutHelper = new Mock<IShortcutComHelper>();
        shortcutHelper
            .Setup(h => h.WithShortcut(store.PrimaryShortcutPath, It.IsAny<Func<dynamic, string?>>()))
            .Returns((string _, Func<dynamic, string?> action) => @"C:\Other\App.exe");

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
    }

    // ---- EnableAutoStart ----

    [Fact]
    public async Task EnableAutoStart_ThrowsTimeoutException_WhenShortcutComOperationTimesOut()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.CmdWrapperPath);
        var timeoutRunner = new TimeoutOperationRunner();
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, timeoutRunner, store);

        // EnableAutoStart should not hang; it surfaces the TimeoutException quickly
        await Assert.ThrowsAsync<TimeoutException>(() => service.EnableAutoStart());
    }

    [Fact]
    public async Task EnableAutoStart_LogsSuccess_WhenShortcutCreated()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.CmdWrapperPath);
        var runner = new PassthroughOperationRunner();

        var shortcutHelper = new Mock<IShortcutComHelper>();
        shortcutHelper
            .Setup(h => h.WithShortcut(store.PrimaryShortcutPath, It.IsAny<Action<dynamic>>()));

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        await service.EnableAutoStart();

        log.Verify(l => l.Info(It.Is<string>(s => s.Contains(store.PrimaryShortcutPath))), Times.Once);
    }

    [Fact]
    public async Task EnableAutoStart_ThrowsFileNotFoundException_WhenCmdWrapperMissing()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        // CmdWrapperPath file does NOT exist
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.EnableAutoStart());
    }

    // ---- DisableAutoStart ----

    [Fact]
    public async Task DisableAutoStart_DoesNotHang_WhenShortcutFilesAreMissing()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        // Should complete immediately without hanging
        await service.DisableAutoStart();

        log.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DisableAutoStart_DeletesExistingShortcutAndLogs()
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        await service.DisableAutoStart();

        Assert.Contains(store.PrimaryShortcutPath, store.DeletedFiles, StringComparer.OrdinalIgnoreCase);
        log.Verify(l => l.Info(It.Is<string>(s => s.Contains(store.PrimaryShortcutPath))), Times.Once);
    }

    // ---- Backward-compatible shortcut target acceptance ----

    [Theory]
    [InlineData(true)]   // target is CmdWrapperPath
    [InlineData(false)]  // target is RunFenceExePath (legacy direct shortcut)
    public async Task IsAutoStartEnabled_AcceptsBothCmdWrapperAndLegacyExeTarget(bool targetIsCmd)
    {
        var log = new Mock<ILoggingService>();
        var store = new FakeShortcutStore();
        store.AddExistingFile(store.PrimaryShortcutPath);
        var runner = new PassthroughOperationRunner();

        var expectedTarget = targetIsCmd ? store.CmdWrapperPath : store.RunFenceExePath;
        var shortcutHelper = new Mock<IShortcutComHelper>();
        shortcutHelper
            .Setup(h => h.WithShortcut(store.PrimaryShortcutPath, It.IsAny<Func<dynamic, string?>>()))
            .Returns((string _, Func<dynamic, string?> _2) => expectedTarget);

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

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
        var shortcutHelper = new Mock<IShortcutComHelper>();

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        // No files exist in the fake store
        var result = await service.IsAutoStartEnabled();

        Assert.False(result);
        // Verify the shortcut helper was NOT called (file did not exist)
        shortcutHelper.Verify(
            h => h.WithShortcut(It.IsAny<string>(), It.IsAny<Func<dynamic, string?>>()),
            Times.Never);
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
        var shortcutHelper = new Mock<IShortcutComHelper>();
        shortcutHelper
            .Setup(h => h.WithShortcut(store.PrimaryShortcutPath, It.IsAny<Action<dynamic>>()));

        var service = BuildService(log.Object, shortcutHelper.Object, runner, store);

        await service.EnableAutoStart();

        shortcutHelper.Verify(
            h => h.WithShortcut(store.PrimaryShortcutPath, It.IsAny<Action<dynamic>>()),
            Times.Once);
    }
}
