using RunFence.Infrastructure;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public sealed class VersionedPathRepairerTests
{
    [Fact]
    public void TryRepair_HighestValidSiblingSelection_IgnoresHigherVersionWithoutFinalTarget()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithEnumeratedDirectories(@"C:\Apps\Slack",
            [
                @"C:\Apps\Slack\app-4.50.121",
                @"C:\Apps\Slack\app-4.51.0",
                @"C:\Apps\Slack\app-4.52.0"
            ])
            .WithExistingFile(@"C:\Apps\Slack\app-4.51.0\Slack.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Slack\app-4.51.0\Slack.exe", result.Value.RepairedPath);
    }

    [Fact]
    public void TryRepair_CanChooseLowerThanCurrentVersionWhenItIsHighestValidCandidate()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithEnumeratedDirectories(@"C:\Apps\Slack",
            [
                @"C:\Apps\Slack\app-4.48.0",
                @"C:\Apps\Slack\app-4.49.0"
            ])
            .WithExistingFile(@"C:\Apps\Slack\app-4.49.0\Slack.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Slack\app-4.49.0\Slack.exe", result.Value.RepairedPath);
    }

    [Fact]
    public void TryRepair_EqualVersionFolders_UsesDirectoryWriteTimeTieBreak()
    {
        var stalePath = @"C:\Apps\Tool\tool-1.2.3\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Tool")
            .WithEnumeratedDirectories(@"C:\Apps\Tool",
            [
                @"C:\Apps\Tool\tool-1.2.3-buildA",
                @"C:\Apps\Tool\tool-1.2.3-buildB"
            ])
            .WithExistingFile(@"C:\Apps\Tool\tool-1.2.3-buildA\tool.exe")
            .WithExistingFile(@"C:\Apps\Tool\tool-1.2.3-buildB\tool.exe")
            .WithDirectoryWriteTime(@"C:\Apps\Tool\tool-1.2.3-buildA", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithDirectoryWriteTime(@"C:\Apps\Tool\tool-1.2.3-buildB", new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Tool\tool-1.2.3-buildB\tool.exe", result.Value.RepairedPath);
    }

    [Fact]
    public void TryRepair_UnreadableWriteTime_FallsBackToOrdinalFolderName()
    {
        var stalePath = @"C:\Apps\Tool\tool-1.2.3\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Tool")
            .WithEnumeratedDirectories(@"C:\Apps\Tool",
            [
                @"C:\Apps\Tool\tool-1.2.3-alpha",
                @"C:\Apps\Tool\tool-1.2.3-beta"
            ])
            .WithExistingFile(@"C:\Apps\Tool\tool-1.2.3-alpha\tool.exe")
            .WithExistingFile(@"C:\Apps\Tool\tool-1.2.3-beta\tool.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Tool\tool-1.2.3-beta\tool.exe", result.Value.RepairedPath);
    }

    [Theory]
    [InlineData(true, @"C:\Apps\Game\game-1.0")]
    [InlineData(false, @"C:\Apps\Game\game-1.0\game.exe")]
    public void TryRepair_RequiresFinalTargetExistence(bool isFolder, string stalePath)
    {
        var parent = isFolder ? @"C:\Apps\Game" : @"C:\Apps\Game";
        var candidateDir = @"C:\Apps\Game\game-1.1";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithState(stalePath, BackupIntentPathState.Missing, isFolder)
            .WithExistingDirectory(parent)
            .WithEnumeratedDirectories(parent, [candidateDir]));

        var result = repairer.TryRepair(stalePath, isFolder, VersionedPathRepairOptions.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void TryRepair_OriginalUnknownState_DoesNotRepair()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithUnknownFile(stalePath));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_UnknownCandidateTarget_IsSkipped()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithEnumeratedDirectories(@"C:\Apps\Slack",
            [
                @"C:\Apps\Slack\app-4.51.0"
            ])
            .WithUnknownFile(@"C:\Apps\Slack\app-4.51.0\Slack.exe"));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Theory]
    [InlineData(@"tool.exe")]
    [InlineData(@"C:tool-1.0\tool.exe")]
    [InlineData(@"%LOCALAPPDATA%\tool.exe")]
    [InlineData(@"https://example.com")]
    [InlineData(@"shell:AppsFolder")]
    [InlineData(@"\\server\share\tool-1.0\tool.exe")]
    [InlineData(@"\\?\C:\Apps\tool-1.0\tool.exe")]
    public void TryRepair_NonRootedOrNonLocalPaths_DoNotRepair(string path)
    {
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem());

        Assert.Null(repairer.TryRepair(path, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_NearestVersionedAncestorWinsBeforeHigherAncestor()
    {
        var stalePath = @"C:\Apps\Outer-1.0\Inner-2.0\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Outer-1.0")
            .WithExistingDirectory(@"C:\Apps")
            .WithEnumeratedDirectories(@"C:\Apps\Outer-1.0",
            [
                @"C:\Apps\Outer-1.0\Inner-2.1"
            ])
            .WithEnumeratedDirectories(@"C:\Apps",
            [
                @"C:\Apps\Outer-1.1"
            ])
            .WithExistingFile(@"C:\Apps\Outer-1.0\Inner-2.1\tool.exe")
            .WithExistingFile(@"C:\Apps\Outer-1.1\Inner-2.0\tool.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Outer-1.0\Inner-2.1\tool.exe", result.Value.RepairedPath);
        Assert.Equal(@"C:\Apps\Outer-1.0\Inner-2.0", result.Value.OriginalAncestorPath);
    }

    [Fact]
    public void TryRepair_StableSuffixIdentityMustMatch()
    {
        var stalePath = @"C:\Program Files\WindowsApps\Vendor.Tool_1.0.0.0_x64__publisher\Tool\Tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Program Files\WindowsApps")
            .WithEnumeratedDirectories(@"C:\Program Files\WindowsApps",
            [
                @"C:\Program Files\WindowsApps\Vendor.Tool_1.1.0.0_x86__publisher",
                @"C:\Program Files\WindowsApps\Vendor.Tool_1.1.0.0_x64__publisher"
            ])
            .WithExistingFile(@"C:\Program Files\WindowsApps\Vendor.Tool_1.1.0.0_x86__publisher\Tool\Tool.exe")
            .WithExistingFile(@"C:\Program Files\WindowsApps\Vendor.Tool_1.1.0.0_x64__publisher\Tool\Tool.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Program Files\WindowsApps\Vendor.Tool_1.1.0.0_x64__publisher\Tool\Tool.exe", result.Value.RepairedPath);
    }

    [Fact]
    public void TryRepair_OpaqueSuffixCanChangeWhenStableIdentityMatches()
    {
        var stalePath = @"C:\Apps\Slack\slack-v4.50.121_oldhash\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithEnumeratedDirectories(@"C:\Apps\Slack",
            [
                @"C:\Apps\Slack\slack-v4.51.0_newhash"
            ])
            .WithExistingFile(@"C:\Apps\Slack\slack-v4.51.0_newhash\Slack.exe"));

        var result = repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty);

        Assert.NotNull(result);
        Assert.Equal(@"C:\Apps\Slack\slack-v4.51.0_newhash\Slack.exe", result.Value.RepairedPath);
    }

    [Fact]
    public void TryRepair_NoValidCandidate_ReturnsNull()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithEnumeratedDirectories(@"C:\Apps\Slack", [@"C:\Apps\Slack\app-4.51.0"]));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_TryEnumerateDirectoriesFailure_ReturnsNull()
    {
        var stalePath = @"C:\Apps\Slack\app-4.50.121\Slack.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps\Slack")
            .WithExistingDirectory(@"C:\Apps")
            .WithEnumeratedDirectories(@"C:\Apps", [@"C:\Apps\Slack-5.0"])
            .WithEnumerationFailure(@"C:\Apps\Slack"));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_NearerAncestorParentUnknown_ReturnsNullInsteadOfClimbingToHigherAncestor()
    {
        var stalePath = @"C:\Apps\Outer-1.0\Inner-2.0\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Apps")
            .WithState(@"C:\Apps\Outer-1.0", BackupIntentPathState.Unknown, isDirectory: true)
            .WithEnumeratedDirectories(@"C:\Apps", [@"C:\Apps\Outer-1.1"])
            .WithExistingFile(@"C:\Apps\Outer-1.1\Inner-2.0\tool.exe"));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_DoesNotParseProgramFilesX86BoundaryAsVersionedAncestor()
    {
        var stalePath = @"C:\Program Files (x86)\Vendor\App.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Program Files")
            .WithEnumeratedDirectories(@"C:\Program Files", [@"C:\Program Files\Program Files (x86)-2.0"])
            .WithExistingFile(@"C:\Program Files\Program Files (x86)-2.0\Vendor\App.exe"));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_DoesNotParseUsersProfileDirectoryNameAsVersionedAncestor()
    {
        var stalePath = @"C:\Users\Alice2026.1\App\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"C:\Users")
            .WithEnumeratedDirectories(@"C:\Users", [@"C:\Users\Alice2026.2"])
            .WithExistingFile(@"C:\Users\Alice2026.2\App\tool.exe"));

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, VersionedPathRepairOptions.Empty));
    }

    [Fact]
    public void TryRepair_DoesNotParseCustomBoundaryProfileRootAsVersionedAncestor()
    {
        var stalePath = @"D:\Profiles\User2026.1\App\tool.exe";
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem()
            .WithMissingFile(stalePath)
            .WithExistingDirectory(@"D:\Profiles")
            .WithEnumeratedDirectories(@"D:\Profiles", [@"D:\Profiles\User2026.2"])
            .WithExistingFile(@"D:\Profiles\User2026.2\App\tool.exe"));

        var options = new VersionedPathRepairOptions([@"D:\Profiles\User2026.1"]);

        Assert.Null(repairer.TryRepair(stalePath, isFolder: false, options));
    }

    private sealed class TestBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _enumerationFailures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _directoryWriteTimes = new(StringComparer.OrdinalIgnoreCase);

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

        public TestBackupIntentFileSystem WithUnknownFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Unknown;
            return this;
        }

        public TestBackupIntentFileSystem WithExistingDirectory(string path)
        {
            _directoryStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public TestBackupIntentFileSystem WithState(string path, BackupIntentPathState state, bool isDirectory)
        {
            if (isDirectory)
                _directoryStates[Normalize(path)] = state;
            else
                _fileStates[Normalize(path)] = state;
            return this;
        }

        public TestBackupIntentFileSystem WithEnumeratedDirectories(string path, IReadOnlyList<string> directories)
        {
            _enumerations[Normalize(path)] = directories.Select(Normalize).ToArray();
            return this;
        }

        public TestBackupIntentFileSystem WithEnumerationFailure(string path)
        {
            _enumerationFailures.Add(Normalize(path));
            return this;
        }

        public TestBackupIntentFileSystem WithDirectoryWriteTime(string path, DateTime lastWriteTimeUtc)
        {
            _directoryWriteTimes[Normalize(path)] = lastWriteTimeUtc;
            return this;
        }

        public BackupIntentPathState GetFileState(string path)
            => _fileStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public BackupIntentPathState GetDirectoryState(string path)
            => _directoryStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
        {
            var normalized = Normalize(path);
            if (_enumerationFailures.Contains(normalized))
            {
                directories = [];
                return false;
            }

            directories = _enumerations.GetValueOrDefault(normalized, []);
            return true;
        }

        public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
            => _directoryWriteTimes.TryGetValue(Normalize(path), out lastWriteTimeUtc);

        private static string Normalize(string path) => Path.GetFullPath(path);
    }
}
