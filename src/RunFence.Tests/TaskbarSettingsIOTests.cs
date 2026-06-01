using System.Text;
using PrefTrans.Services;
using PrefTrans.Services.IO;
using PrefTrans.Settings;
using Xunit;

namespace RunFence.Tests;

public class TaskbarSettingsIOTests
{
    private readonly TaskbarProfilePathPatcher _profilePathPatcher = new();

    [Fact]
    public void TaskbarSettingsIO_Read_MapsRegistryValuesAndPinnedShortcuts()
    {
        var registryStore = new RecordingTaskbarRegistryStore
        {
            ExplorerAdvancedToRead = new TaskbarExplorerAdvancedRegistryValues
            {
                TaskbarSmallIcons = 1,
                ShowTaskViewButton = 0,
                TaskbarAlignment = 1,
                ShowWidgets = 0,
                ButtonCombine = 2,
                MultiMonitorButtonCombine = 1,
                VirtualDesktopTaskbarFilter = 0
            },
            TaskbandToRead = new TaskbarTaskbandRegistryValues
            {
                Favorites = [0x10, 0x11],
                FavoritesResolve = [0x20, 0x21]
            },
            SearchboxTaskbarModeToRead = 2
        };
        var pinnedShortcuts = new RecordingPinnedShortcutTransferService
        {
            OnRead = taskbar =>
            {
                taskbar.PinnedShortcuts = ["Pinned.lnk"];
                taskbar.PinnedShortcutFiles = new Dictionary<string, byte[]>
                {
                    ["Pinned.lnk"] = [0x44]
                };
            }
        };
        var settingsIo = CreateTaskbarSettingsIo(registryStore, pinnedShortcuts);

        var taskbar = settingsIo.Read();

        Assert.Equal(1, taskbar.SmallIcons);
        Assert.Equal(0, taskbar.ShowTaskViewButton);
        Assert.Equal(1, taskbar.TaskbarAlignment);
        Assert.Equal(0, taskbar.ShowWidgets);
        Assert.Equal(2, taskbar.ButtonCombine);
        Assert.Equal(1, taskbar.MultiMonitorButtonCombine);
        Assert.Equal(0, taskbar.VirtualDesktopTaskbarFilter);
        Assert.Equal(2, taskbar.SearchboxTaskbarMode);
        Assert.Equal([0x10, 0x11], taskbar.Favorites);
        Assert.Equal([0x20, 0x21], taskbar.FavoritesResolve);
        Assert.Equal(["Pinned.lnk"], taskbar.PinnedShortcuts);
        Assert.Equal([0x44], taskbar.PinnedShortcutFiles!["Pinned.lnk"]);
        Assert.Equal(1, pinnedShortcuts.ReadCallCount);
        Assert.False(string.IsNullOrEmpty(taskbar.SourceProfilePath));
    }

    [Fact]
    public void TaskbarSettingsIO_Write_PerformsWritesInOrderAndBroadcasts()
    {
        var writeOrder = new List<string>();
        var broadcast = new RecordingBroadcastHelper();
        var registryStore = new RecordingTaskbarRegistryStore(writeOrder)
        {
            ExplorerAdvancedWriteResult = true,
            TaskbandWriteResult = true
        };
        var pinnedShortcuts = new RecordingPinnedShortcutTransferService(writeOrder)
        {
            WriteResult = true
        };
        var settingsIo = CreateTaskbarSettingsIo(registryStore, pinnedShortcuts, broadcast);

        settingsIo.Write(new TaskbarSettings { SearchboxTaskbarMode = 1 });

        Assert.Equal(
            ["WriteExplorerAdvancedValues", "WriteTaskbandValues", "WritePinnedShortcuts", "WriteSearchboxTaskbarMode"],
            writeOrder);
        Assert.Equal(1, broadcast.BroadcastCount);
    }

    [Fact]
    public void TaskbarSettingsIO_Write_DoesNotBroadcastWhenNoChanges()
    {
        var writeOrder = new List<string>();
        var broadcast = new RecordingBroadcastHelper();
        var registryStore = new RecordingTaskbarRegistryStore(writeOrder);
        var pinnedShortcuts = new RecordingPinnedShortcutTransferService(writeOrder);
        var settingsIo = CreateTaskbarSettingsIo(registryStore, pinnedShortcuts, broadcast);

        settingsIo.Write(new TaskbarSettings());

        Assert.Equal(
            ["WriteExplorerAdvancedValues", "WriteTaskbandValues", "WritePinnedShortcuts"],
            writeOrder);
        Assert.Equal(0, broadcast.BroadcastCount);
    }

    [Fact]
    public void TaskbarSettingsIO_Write_PatchesTaskbandValuesBeforeWriting()
    {
        var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(targetProfile))
            throw new InvalidOperationException("User profile path is missing in test environment.");

        var sourceProfile = targetProfile + ".Source";
        var taskbar = new TaskbarSettings
        {
            SourceProfilePath = sourceProfile,
            Favorites = BuildCountedPathBlob(sourceProfile.Length + 8, sourceProfile, @"\Taskbar\Favorite.lnk"),
            FavoritesResolve = BuildCountedPathBlob(sourceProfile.Length + 9, sourceProfile, @"\Taskbar\FavoriteResolve.lnk")
        };
        var registryStore = new RecordingTaskbarRegistryStore();
        var settingsIo = CreateTaskbarSettingsIo(registryStore);

        settingsIo.Write(taskbar);

        Assert.NotNull(registryStore.LastTaskbandValuesWritten);
        Assert.Equal(
            CreateExpectedCountedPathBlob(sourceProfile.Length + 8, sourceProfile, targetProfile, @"\Taskbar\Favorite.lnk"),
            registryStore.LastTaskbandValuesWritten!.Favorites);
        Assert.Equal(
            CreateExpectedCountedPathBlob(sourceProfile.Length + 9, sourceProfile, targetProfile, @"\Taskbar\FavoriteResolve.lnk"),
            registryStore.LastTaskbandValuesWritten!.FavoritesResolve);
    }

    [Fact]
    public void TaskbarSettingsIO_Write_WithMissingSourceProfileSkipsUnownedLegacyTaskbandData()
    {
        var taskbar = new TaskbarSettings
        {
            SourceProfilePath = string.Empty,
            Favorites = BuildCountedPathBlob(30, @"C:\Users\NotCurrent", @"\Taskbar\Favorite.lnk"),
            FavoritesResolve = BuildCountedPathBlob(31, @"D:\Users\NotCurrent", @"\Taskbar\FavoriteResolve.lnk")
        };
        var registryStore = new RecordingTaskbarRegistryStore();
        var settingsIo = CreateTaskbarSettingsIo(registryStore);

        settingsIo.Write(taskbar);

        Assert.NotNull(registryStore.LastTaskbandValuesWritten);
        Assert.Null(registryStore.LastTaskbandValuesWritten!.Favorites);
        Assert.Null(registryStore.LastTaskbandValuesWritten!.FavoritesResolve);
    }

    [Fact]
    public void TaskbarProfilePathPatcher_PatchProfilePath_ReplacesSameLengthProfilePath()
    {
        const string sourceProfile = @"C:\Users\Old";
        const string targetProfile = @"D:\Users\New";
        var blob = BuildCountedPathBlob(20, sourceProfile, @"\Taskbar\Same.lnk");

        var patched = _profilePathPatcher.PatchProfilePath(blob, sourceProfile, targetProfile);

        Assert.Equal(
            CreateExpectedCountedPathBlob(20, sourceProfile, targetProfile, @"\Taskbar\Same.lnk"),
            patched);
    }

    [Fact]
    public void TaskbarProfilePathPatcher_PatchProfilePath_ReplacesWithShorterProfilePathAndShrinksLengthPrefix()
    {
        const string sourceProfile = @"C:\Users\LongProfile";
        const string targetProfile = @"D:\Usr\New";
        var blob = BuildCountedPathBlob(40, sourceProfile, @"\Taskbar\Shorter.lnk");

        var patched = _profilePathPatcher.PatchProfilePath(blob, sourceProfile, targetProfile);

        Assert.Equal(
            CreateExpectedCountedPathBlob(40, sourceProfile, targetProfile, @"\Taskbar\Shorter.lnk"),
            patched);
    }

    [Fact]
    public void TaskbarProfilePathPatcher_PatchProfilePath_ReplacesWithLongerProfilePathAndGrowsLengthPrefix()
    {
        const string sourceProfile = @"C:\Users\Src";
        const string targetProfile = @"D:\Users\MuchLongerTargetProfile";
        var blob = BuildCountedPathBlob(25, sourceProfile, @"\Taskbar\Longer.lnk");

        var patched = _profilePathPatcher.PatchProfilePath(blob, sourceProfile, targetProfile);

        Assert.Equal(
            CreateExpectedCountedPathBlob(25, sourceProfile, targetProfile, @"\Taskbar\Longer.lnk"),
            patched);
    }

    [Fact]
    public void PinnedShortcutTransferService_ReadPinnedShortcuts_FiltersUserProfileAndWindowsAppsTargets()
    {
        var fileStore = new FakePinnedShortcutFileStore(
            @"C:\Pinned",
            new Dictionary<string, byte[]>
            {
                [@"C:\Pinned\Keep.lnk"] = [0x10, 0x11],
                [@"C:\Pinned\UserProfile.lnk"] = [0x20],
                [@"C:\Pinned\WindowsApps.lnk"] = [0x30]
            });
        var reader = new FakePinnedShortcutReader(new Dictionary<string, string?>
        {
            [@"C:\Pinned\Keep.lnk"] = @"C:\Program Files\App\app.exe",
            [@"C:\Pinned\UserProfile.lnk"] = @"C:\Users\Alice\AppData\Roaming\App\app.exe",
            [@"C:\Pinned\WindowsApps.lnk"] = @"C:\Program Files\WindowsApps\StoreApp\app.exe"
        });
        var userProfileFilter = new FakeUserProfileFilter(
            [@"C:\Users\Alice"],
            windowsAppsSubstring: @"C:\Program Files\WindowsApps");
        var service = CreatePinnedShortcutTransferService(fileStore, reader, userProfileFilter);
        var taskbar = new TaskbarSettings();

        service.ReadPinnedShortcuts(taskbar);

        Assert.Equal(["Keep.lnk"], taskbar.PinnedShortcuts);
        Assert.Equal([0x10, 0x11], taskbar.PinnedShortcutFiles!["Keep.lnk"]);
    }

    [Fact]
    public void PinnedShortcutTransferService_ReadPinnedShortcuts_KeepsReadableShortcutsWhenAnotherShortcutIsUnreadable()
    {
        var fileStore = new FakePinnedShortcutFileStore(
            @"C:\Pinned",
            new Dictionary<string, byte[]>
            {
                [@"C:\Pinned\Readable.lnk"] = [0x01, 0x02],
                [@"C:\Pinned\Unreadable.lnk"] = [0x03, 0x04]
            })
        {
            ReadFailures = { @"C:\Pinned\Unreadable.lnk" }
        };
        var reader = new FakePinnedShortcutReader(new Dictionary<string, string?>
        {
            [@"C:\Pinned\Readable.lnk"] = @"C:\Tools\Readable.exe",
            [@"C:\Pinned\Unreadable.lnk"] = @"C:\Tools\Unreadable.exe"
        });
        var service = CreatePinnedShortcutTransferService(fileStore, reader);
        var taskbar = new TaskbarSettings();

        service.ReadPinnedShortcuts(taskbar);

        Assert.Equal(["Readable.lnk", "Unreadable.lnk"], taskbar.PinnedShortcuts);
        Assert.Equal([0x01, 0x02], taskbar.PinnedShortcutFiles!["Readable.lnk"]);
        Assert.False(taskbar.PinnedShortcutFiles.ContainsKey("Unreadable.lnk"));
    }

    [Fact]
    public void PinnedShortcutTransferService_ReadPinnedShortcuts_CapturesRawShortcutBytes()
    {
        var rawBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var fileStore = new FakePinnedShortcutFileStore(
            @"C:\Pinned",
            new Dictionary<string, byte[]>
            {
                [@"C:\Pinned\RawCapture.lnk"] = rawBytes
            });
        var reader = new FakePinnedShortcutReader(new Dictionary<string, string?>
        {
            [@"C:\Pinned\RawCapture.lnk"] = @"C:\Program Files\Raw\App.exe"
        });
        var service = CreatePinnedShortcutTransferService(fileStore, reader);
        var taskbar = new TaskbarSettings();

        service.ReadPinnedShortcuts(taskbar);

        Assert.Equal(rawBytes, taskbar.PinnedShortcutFiles!["RawCapture.lnk"]);
    }

    [Fact]
    public void WritePinnedShortcuts_FiltersInvalidShortcutNamesAndWritesOnlyResolvedDestinations()
    {
        var fileStore = new FakePinnedShortcutFileStore(@"C:\Pinned", new Dictionary<string, byte[]>());
        var service = CreatePinnedShortcutTransferService(
            fileStore,
            new FakePinnedShortcutReader(new Dictionary<string, string?>()));
        var validShortcutName = "preftrans-valid.lnk";
        var taskbar = new TaskbarSettings
        {
            SourceProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            PinnedShortcutFiles = new Dictionary<string, byte[]>
            {
                ["..\\invalid.lnk"] = [0x00],
                ["bad|name.lnk"] = [0x01],
                [""] = [0x02],
                [validShortcutName] = [0x03, 0x04],
                ["folder\\name.lnk"] = [0x05]
            }
        };

        var wroteShortcut = service.WritePinnedShortcuts(taskbar);

        Assert.True(wroteShortcut);
        Assert.Equal([0x03, 0x04], fileStore.WrittenFiles[Path.Combine(@"C:\Pinned", validShortcutName)]);
        Assert.DoesNotContain(Path.Combine(@"C:\Pinned", "bad|name.lnk"), fileStore.WrittenFiles.Keys);
    }

    [Fact]
    public void WritePinnedShortcuts_CrossAccountSourceProfile_PatchesRawShortcutBytesBeforeWrite()
    {
        var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(targetProfile))
            throw new InvalidOperationException("User profile path is missing in test environment.");

        var sourceProfile = targetProfile + ".Imported";
        var shortcutName = "cross-account.lnk";
        var rawShortcutBytes = BuildCountedPathBlob(
            sourceProfile.Length + 12,
            sourceProfile,
            @"\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\cross-account.lnk");
        var fileStore = new FakePinnedShortcutFileStore(@"C:\Pinned", new Dictionary<string, byte[]>());
        var pinnedShortcuts = CreatePinnedShortcutTransferService(
            fileStore,
            new FakePinnedShortcutReader(new Dictionary<string, string?>()));
        var settingsIo = new TaskbarSettingsIO(
            new RecordingTaskbarRegistryStore(),
            pinnedShortcuts,
            new TaskbarLegacyOwnershipDetector(_profilePathPatcher),
            _profilePathPatcher,
            new RecordingBroadcastHelper());

        settingsIo.Write(new TaskbarSettings
        {
            SourceProfilePath = sourceProfile,
            PinnedShortcutFiles = new Dictionary<string, byte[]>
            {
                [shortcutName] = rawShortcutBytes
            }
        });

        Assert.Equal(
            CreateExpectedCountedPathBlob(
                sourceProfile.Length + 12,
                sourceProfile,
                targetProfile,
                @"\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\cross-account.lnk"),
            fileStore.WrittenFiles[Path.Combine(@"C:\Pinned", shortcutName)]);
    }

    [Fact]
    public void TryResolvePinnedShortcutDestinationPath_ValidShortcut_ReturnsPathUnderTaskbarFolder()
    {
        var taskbarFolder = @"C:\Users\Test\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        var result = _profilePathPatcher.TryResolvePinnedShortcutDestinationPath(taskbarFolder, "Claude Code.lnk", out var destinationPath);

        Assert.True(result);
        Assert.Equal(Path.Combine(taskbarFolder, "Claude Code.lnk"), destinationPath);
    }

    [Theory]
    [InlineData(@"bad.txt")]
    [InlineData(@"bad|name.lnk")]
    [InlineData(@"..\\invalid.lnk")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolvePinnedShortcutDestinationPath_InvalidName_ReturnsFalse(string importedName)
    {
        var taskbarFolder = @"C:\Users\Test\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        var result = _profilePathPatcher.TryResolvePinnedShortcutDestinationPath(taskbarFolder, importedName, out var destinationPath);

        Assert.False(result);
        Assert.Equal(string.Empty, destinationPath);
    }

    [Fact]
    public void IsOwnedByCurrentProfile_ReturnsTrueWhenEitherTaskbandBlobContainsTargetProfile()
    {
        var detector = new TaskbarLegacyOwnershipDetector(_profilePathPatcher);
        var profilePath = @"C:\Users\Test";
        var favorites = Encoding.Unicode.GetBytes($@"prefix {profilePath}\AppData");

        var result = detector.IsOwnedByCurrentProfile(favorites, null, profilePath);

        Assert.True(result);
    }

    private TaskbarSettingsIO CreateTaskbarSettingsIo(
        RecordingTaskbarRegistryStore? registryStore = null,
        RecordingPinnedShortcutTransferService? pinnedShortcuts = null,
        RecordingBroadcastHelper? broadcast = null)
    {
        return new TaskbarSettingsIO(
            registryStore ?? new RecordingTaskbarRegistryStore(),
            pinnedShortcuts ?? new RecordingPinnedShortcutTransferService(),
            new TaskbarLegacyOwnershipDetector(_profilePathPatcher),
            _profilePathPatcher,
            broadcast ?? new RecordingBroadcastHelper());
    }

    private PinnedShortcutTransferService CreatePinnedShortcutTransferService(
        FakePinnedShortcutFileStore fileStore,
        FakePinnedShortcutReader reader,
        IUserProfileFilter? userProfileFilter = null)
    {
        return new PinnedShortcutTransferService(
            new SwallowingSafeExecutor(),
            userProfileFilter ?? new FakeUserProfileFilter([], windowsAppsSubstring: null),
            _profilePathPatcher,
            new FixedPinnedShortcutFolderProvider(fileStore.Folder),
            reader,
            fileStore);
    }

    private static byte[] BuildCountedPathBlob(int characterCount, string profilePath, string suffix)
    {
        var bytes = new List<byte>
        {
            (byte)(characterCount & 0xFF),
            (byte)((characterCount >> 8) & 0xFF)
        };
        bytes.AddRange(Encoding.Unicode.GetBytes(profilePath));
        bytes.AddRange(Encoding.Unicode.GetBytes(suffix));
        return bytes.ToArray();
    }

    private static byte[] CreateExpectedCountedPathBlob(
        int originalCharacterCount,
        string sourceProfile,
        string targetProfile,
        string suffix)
    {
        return BuildCountedPathBlob(
            originalCharacterCount + (targetProfile.Length - sourceProfile.Length),
            targetProfile,
            suffix);
    }

    private sealed class SwallowingSafeExecutor : ISafeExecutor
    {
        public void Try(Action action, string operation)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }
    }

    private sealed class RecordingTaskbarRegistryStore : ITaskbarRegistryStore
    {
        private readonly List<string>? _callOrder;

        public RecordingTaskbarRegistryStore(List<string>? callOrder = null)
        {
            _callOrder = callOrder;
        }

        public TaskbarExplorerAdvancedRegistryValues ExplorerAdvancedToRead { get; set; } = new();
        public TaskbarTaskbandRegistryValues TaskbandToRead { get; set; } = new();
        public int? SearchboxTaskbarModeToRead { get; set; }
        public bool ExplorerAdvancedWriteResult { get; set; }
        public bool TaskbandWriteResult { get; set; }
        public bool SearchboxTaskbarModeWriteResult { get; set; }
        public TaskbarExplorerAdvancedRegistryValues? LastExplorerAdvancedValuesWritten { get; private set; }
        public TaskbarTaskbandRegistryValues? LastTaskbandValuesWritten { get; private set; }
        public int? LastSearchboxTaskbarModeWritten { get; private set; }

        public TaskbarExplorerAdvancedRegistryValues ReadExplorerAdvancedValues() => ExplorerAdvancedToRead;

        public bool WriteExplorerAdvancedValues(TaskbarExplorerAdvancedRegistryValues values)
        {
            _callOrder?.Add("WriteExplorerAdvancedValues");
            LastExplorerAdvancedValuesWritten = values;
            return ExplorerAdvancedWriteResult;
        }

        public TaskbarTaskbandRegistryValues ReadTaskbandValues() => TaskbandToRead;

        public bool WriteTaskbandValues(TaskbarTaskbandRegistryValues values)
        {
            _callOrder?.Add("WriteTaskbandValues");
            LastTaskbandValuesWritten = values;
            return TaskbandWriteResult;
        }

        public int? ReadSearchboxTaskbarMode() => SearchboxTaskbarModeToRead;

        public bool WriteSearchboxTaskbarMode(int value)
        {
            _callOrder?.Add("WriteSearchboxTaskbarMode");
            LastSearchboxTaskbarModeWritten = value;
            return SearchboxTaskbarModeWriteResult;
        }
    }

    private sealed class RecordingPinnedShortcutTransferService : IPinnedShortcutTransferService
    {
        private readonly List<string>? _callOrder;

        public RecordingPinnedShortcutTransferService(List<string>? callOrder = null)
        {
            _callOrder = callOrder;
        }

        public bool WriteResult { get; set; }
        public int ReadCallCount { get; private set; }
        public Action<TaskbarSettings>? OnRead { get; set; }

        public void ReadPinnedShortcuts(TaskbarSettings taskbar)
        {
            ReadCallCount++;
            OnRead?.Invoke(taskbar);
        }

        public bool WritePinnedShortcuts(TaskbarSettings taskbar)
        {
            _callOrder?.Add("WritePinnedShortcuts");
            return WriteResult;
        }
    }

    private sealed class RecordingBroadcastHelper : IBroadcastHelper
    {
        public int BroadcastCount { get; private set; }

        public void Broadcast()
        {
            BroadcastCount++;
        }

        public void BroadcastIntl()
        {
        }
    }

    private sealed class FixedPinnedShortcutFolderProvider(string folder) : IPinnedShortcutFolderProvider
    {
        public string GetPinnedShortcutFolder() => folder;
    }

    private sealed class FakePinnedShortcutReader(IReadOnlyDictionary<string, string?> targets) : IPinnedShortcutReader
    {
        public string? ReadTargetPath(string shortcutPath)
        {
            return targets.TryGetValue(shortcutPath, out var target) ? target : null;
        }
    }

    private sealed class FakePinnedShortcutFileStore : IPinnedShortcutFileStore
    {
        private readonly Dictionary<string, byte[]> _files;

        public FakePinnedShortcutFileStore(string folder, IDictionary<string, byte[]> files)
        {
            Folder = folder;
            _files = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
        }

        public string Folder { get; }
        public HashSet<string> ReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> WrittenFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> EnsuredDirectories { get; } = [];

        public IReadOnlyList<string> EnumerateShortcutFiles(string folder)
        {
            if (!string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase))
                return [];

            return _files.Keys.Concat(WrittenFiles.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public byte[] ReadAllBytes(string path)
        {
            if (ReadFailures.Contains(path))
                throw new IOException("Unreadable shortcut.");

            if (WrittenFiles.TryGetValue(path, out var written))
                return written;

            return _files[path];
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            WrittenFiles[path] = bytes;
        }

        public void EnsureDirectory(string folder)
        {
            EnsuredDirectories.Add(folder);
        }
    }

    private sealed class FakeUserProfileFilter(
        string[] profilePaths,
        string? windowsAppsSubstring) : IUserProfileFilter
    {
        public string[] GetUserProfilePaths() => profilePaths;

        public bool ContainsUserProfilePath(string? value, string[] paths)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return paths.Any(path => value.Contains(path, StringComparison.OrdinalIgnoreCase));
        }

        public bool ContainsWindowsAppsPath(string? value)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(windowsAppsSubstring) &&
                   value.Contains(windowsAppsSubstring, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsUwpProgId(string? progId) => false;
    }
}
