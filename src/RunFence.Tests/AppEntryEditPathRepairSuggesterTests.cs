using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public sealed class AppEntryEditPathRepairSuggesterTests
{
    [Fact]
    public void SuggestIfNeeded_MissingPathOutsideTrustedLocation_YesUpdatesControls()
    {
        var existingApp = new AppEntry
        {
            ExePath = @"D:\Apps\Slack\app-4.50.121\Slack.exe",
            AccountSid = "S-1-5-21-1"
        };
        var receiver = new TestReceiver();
        var messageBox = new Mock<IMessageBoxService>();
        messageBox.Setup(service => service.Show(
                It.Is<string>(text =>
                    text.Contains(existingApp.ExePath, StringComparison.Ordinal) &&
                    text.Contains(@"D:\Apps\Slack\app-4.51.0\Slack.exe", StringComparison.Ordinal) &&
                    text.Contains("click Apply or OK", StringComparison.Ordinal)),
                "RunFence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question))
            .Returns(DialogResult.Yes);

        var suggester = CreateSuggester(
            new TestBackupIntentFileSystem()
                .WithMissingFile(existingApp.ExePath)
                .WithExistingDirectory(@"D:\Apps\Slack")
                .WithEnumeratedDirectories(@"D:\Apps\Slack", [@"D:\Apps\Slack\app-4.51.0"])
                .WithExistingFile(@"D:\Apps\Slack\app-4.51.0\Slack.exe"),
            messageBox.Object);

        var updated = suggester.SuggestIfNeeded(existingApp, receiver);

        Assert.True(updated);
        Assert.Equal(@"D:\Apps\Slack\app-4.51.0\Slack.exe", receiver.FilePath);
        Assert.False(receiver.IsFolder);
        messageBox.VerifyAll();
    }

    [Fact]
    public void SuggestIfNeeded_MissingPathOutsideTrustedLocation_NoLeavesControlsUnchanged()
    {
        var existingApp = new AppEntry
        {
            ExePath = @"D:\Apps\Slack\app-4.50.121\Slack.exe",
            AccountSid = "S-1-5-21-1"
        };
        var receiver = new TestReceiver();
        var messageBox = new Mock<IMessageBoxService>();
        messageBox.Setup(service => service.Show(
                It.IsAny<string>(),
                "RunFence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question))
            .Returns(DialogResult.No);

        var suggester = CreateSuggester(
            new TestBackupIntentFileSystem()
                .WithMissingFile(existingApp.ExePath)
                .WithExistingDirectory(@"D:\Apps\Slack")
                .WithEnumeratedDirectories(@"D:\Apps\Slack", [@"D:\Apps\Slack\app-4.51.0"])
                .WithExistingFile(@"D:\Apps\Slack\app-4.51.0\Slack.exe"),
            messageBox.Object);

        var updated = suggester.SuggestIfNeeded(existingApp, receiver);

        Assert.False(updated);
        Assert.Null(receiver.FilePath);
        messageBox.Verify(service => service.Show(
            It.IsAny<string>(),
            "RunFence",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question), Times.Once);
    }

    [Fact]
    public void SuggestIfNeeded_AutoRepairTrustedCandidate_DoesNotPromptOrRewriteControls()
    {
        var existingApp = new AppEntry
        {
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        var receiver = new TestReceiver();
        var messageBox = new Mock<IMessageBoxService>(MockBehavior.Strict);

        var suggester = CreateSuggester(
            new TestBackupIntentFileSystem()
                .WithMissingFile(existingApp.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
            messageBox.Object,
            programFilesRoots: [@"C:\Program Files"]);

        var updated = suggester.SuggestIfNeeded(existingApp, receiver);

        Assert.False(updated);
        Assert.Null(receiver.FilePath);
    }

    [Fact]
    public void SuggestIfNeeded_NoCandidate_DoesNotPrompt()
    {
        var existingApp = new AppEntry
        {
            ExePath = @"D:\Apps\Slack\app-4.50.121\Slack.exe",
            AccountSid = "S-1-5-21-1"
        };
        var receiver = new TestReceiver();
        var messageBox = new Mock<IMessageBoxService>(MockBehavior.Strict);

        var suggester = CreateSuggester(
            new TestBackupIntentFileSystem()
                .WithMissingFile(existingApp.ExePath)
                .WithExistingDirectory(@"D:\Apps\Slack")
                .WithEnumeratedDirectories(@"D:\Apps\Slack", [@"D:\Apps\Slack\app-4.51.0"]),
            messageBox.Object);

        var updated = suggester.SuggestIfNeeded(existingApp, receiver);

        Assert.False(updated);
        Assert.Null(receiver.FilePath);
    }

    private static AppEntryEditPathRepairSuggester CreateSuggester(
        IBackupIntentFileSystem fileSystem,
        IMessageBoxService messageBoxService,
        IReadOnlyList<string>? programFilesRoots = null,
        IReadOnlyDictionary<string, string>? profilePaths = null)
    {
        var programFilesProvider = new Mock<IProgramFilesPathProvider>();
        programFilesProvider.Setup(provider => provider.GetProgramFilesRoots())
            .Returns(programFilesRoots ?? []);

        var profilePathResolver = new Mock<IProfilePathResolver>();
        profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(It.IsAny<string>()))
            .Returns<string>(sid =>
                profilePaths != null && profilePaths.TryGetValue(sid, out var path) ? path : null);

        return new AppEntryEditPathRepairSuggester(
            new VersionedPathRepairer(fileSystem),
            new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object),
            new VersionedPathRepairOptionsBuilder(profilePathResolver.Object),
            messageBoxService);
    }

    private sealed class TestReceiver : IAppEditBrowseResultReceiver
    {
        public string? FilePath { get; private set; }

        public bool IsFolder { get; private set; }

        public string GetAppName() => string.Empty;

        public void SetFilePath(string path) => FilePath = path;

        public void SetAppName(string name)
        {
        }

        public void SetFolderMode(bool isFolder) => IsFolder = isFolder;

        public void SetWorkingDir(string path)
        {
        }

        public void SetDefaultArgs(string args)
        {
        }

        public bool CanSuggestBasicPrivilegeLevel() => false;

        public string? GetSelectedAccountSid() => null;

        public void SetPrivilegeLevel(PrivilegeLevel? level)
        {
        }
    }

    private sealed class TestBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public TestBackupIntentFileSystem WithExistingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public TestBackupIntentFileSystem WithMissingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Missing;
            return this;
        }

        public TestBackupIntentFileSystem WithExistingDirectory(string path)
        {
            _directoryStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public TestBackupIntentFileSystem WithEnumeratedDirectories(string path, IReadOnlyList<string> directories)
        {
            _enumerations[Normalize(path)] = directories.Select(Normalize).ToArray();
            return this;
        }

        public BackupIntentPathState GetFileState(string path)
            => _fileStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public BackupIntentPathState GetDirectoryState(string path)
            => _directoryStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
        {
            directories = _enumerations.GetValueOrDefault(Normalize(path), []);
            return true;
        }

        public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
        {
            lastWriteTimeUtc = default;
            return false;
        }

        private static string Normalize(string path) => Path.GetFullPath(path);
    }
}
