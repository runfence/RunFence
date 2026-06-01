using System.Security.AccessControl;
using System.Dynamic;
using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.Tests;

public class ShortcutWriteAccessServiceTests
{
    [Fact]
    public void PersistShortcut_PublishFailure_CleansDestinationWithoutRetrying()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_PublishFailure");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "original");
        var helper = new TempWritingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative
        {
            ThrowOnPublish = true
        };
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        var ex = Assert.Throws<IOException>(() =>
            service.PersistShortcut(shortcutPath, new ShortcutMutation(
                @"C:\Apps\App.exe",
                "--args",
                @"C:\Apps",
                @"C:\Icons\App.ico,0",
                ShortcutIconUpdateMode.Set,
                "description",
                "CTRL+ALT+R",
                7),
                ShortcutDestinationMetadataMode.PreserveExisting,
                ShortcutContentMode.PreserveExisting));

        Assert.Equal("Injected copy failure.", ex.Message);
        Assert.Equal(1, helper.SaveCount);
        Assert.Equal(1, native.PublishAttempts);
        Assert.Equal(2, native.DeleteAttempts);
        Assert.False(File.Exists(shortcutPath));
        Assert.Empty(Directory.GetFiles(trustedTempRoot, "*.lnk", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void PersistShortcut_PublishCreatesDestinationThenFails_CleansDestination()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_PublishFailure_Cleanup");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "original");
        var native = new RecordingShortcutFilePersistenceNative
        {
            ThrowOnPublish = true,
            CreateDestinationBeforeThrow = true
        };
        var service = new ShortcutFilePersistenceService(new TempWritingShortcutComHelper(), native, trustedTempRoot);

        Assert.Throws<IOException>(() =>
            service.PersistShortcut(
                shortcutPath,
                new ShortcutMutation(
                    @"C:\Apps\App.exe",
                    "--args",
                    @"C:\Apps",
                    null,
                    ShortcutIconUpdateMode.None,
                    "description",
                    null,
                    1),
                ShortcutDestinationMetadataMode.PreserveExisting,
                ShortcutContentMode.PreserveExisting));

        Assert.Equal(1, native.PublishAttempts);
        Assert.Equal(2, native.DeleteAttempts);
        Assert.False(File.Exists(shortcutPath));
    }

    [Fact]
    public void PersistShortcut_ExistingShortcut_RestoresCapturedMetadata()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_Metadata");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "original");
        var originalCreationTime = new DateTime(2022, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var originalWriteTime = new DateTime(2022, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var originalAccessTime = new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetCreationTimeUtc(shortcutPath, originalCreationTime);
        File.SetLastWriteTimeUtc(shortcutPath, originalWriteTime);
        File.SetLastAccessTimeUtc(shortcutPath, originalAccessTime);
        var security = new FileSecurity();
        new FileInfo(shortcutPath).SetAccessControl(security);

        var helper = new TempWritingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "--args",
            @"C:\Apps",
            null,
            ShortcutIconUpdateMode.None,
            "description",
            null,
            1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.NotNull(native.RestoredMetadata);
        Assert.Equal(originalCreationTime, File.GetCreationTimeUtc(shortcutPath));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(shortcutPath));
        Assert.Equal(originalAccessTime, File.GetLastAccessTimeUtc(shortcutPath));
    }

    [Fact]
    public void PersistShortcut_PreserveExistingContent_CopiesOriginalShortcutPayload()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_SourceCopy");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "existing-shortcut-payload");
        var helper = new SourceCopyObservingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "--args",
            @"C:\Apps",
            null,
            ShortcutIconUpdateMode.None,
            "description",
            null,
            1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.True(helper.PathExistedBeforeEdit);
        Assert.Equal("existing-shortcut-payload", helper.ContentBeforeEdit);
    }

    [Fact]
    public void PersistShortcut_RecreateCanonicalContent_SkipsOriginalShortcutPayload()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_SourceCopy_Recreate");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "existing-shortcut-payload");
        var helper = new SourceCopyObservingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "--args",
            @"C:\Apps",
            null,
            ShortcutIconUpdateMode.None,
            "description",
            null,
            1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.RecreateCanonical);

        Assert.False(helper.PathExistedBeforeEdit);
        Assert.Null(helper.ContentBeforeEdit);
    }

    [Fact]
    public void PersistShortcut_PreserveExistingContent_ClearsReadOnlyOnTrustedTempCopyBeforeEditing()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_SourceCopy_ReadOnly");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "existing-shortcut-payload");
        File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) | FileAttributes.ReadOnly);
        var helper = new SourceCopyObservingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "--args",
            @"C:\Apps",
            null,
            ShortcutIconUpdateMode.None,
            "description",
            null,
            1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.True(helper.PathExistedBeforeEdit);
        Assert.True(helper.WasWritableBeforeEdit);
    }

    [Fact]
    public void PersistShortcut_PreserveExistingParseFailure_FallsBackToCanonicalRewrite()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_PreserveExisting_ParseFailure");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "not-a-shortcut");
        var helper = new ParseFailingShortcutComHelper("not-a-shortcut", blockCleanup: false);
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(
            shortcutPath,
            new ShortcutMutation(
                @"C:\Apps\App.exe",
                "--args",
                @"C:\Apps",
                null,
                ShortcutIconUpdateMode.None,
                "description",
                null,
                1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.Equal(2, helper.WithShortcutCount);
        Assert.Contains(@"C:\Apps\App.exe", File.ReadAllText(shortcutPath));
        Assert.DoesNotContain("not-a-shortcut", File.ReadAllText(shortcutPath));
    }

    [Fact]
    public void PersistShortcut_PreserveExistingParseFailureWhenTempCleanupFails_DoesNotPublishStaleTemp()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_PreserveExisting_CleanupFailure");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "not-a-shortcut");
        var helper = new ParseFailingShortcutComHelper("not-a-shortcut", blockCleanup: true);
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        var ex = Assert.Throws<IOException>(() =>
            service.PersistShortcut(
                shortcutPath,
                new ShortcutMutation(
                    @"C:\Apps\App.exe",
                    "--args",
                    @"C:\Apps",
                    null,
                    ShortcutIconUpdateMode.None,
                    "description",
                    null,
                    1),
                ShortcutDestinationMetadataMode.PreserveExisting,
                ShortcutContentMode.PreserveExisting));

        Assert.Contains("before canonical rewrite", ex.Message);
        Assert.Equal(0, native.PublishAttempts);
        Assert.Equal("not-a-shortcut", File.ReadAllText(shortcutPath));
        Assert.Single(Directory.GetFileSystemEntries(trustedTempRoot));
    }

    [Fact]
    public void PersistShortcut_PreserveExistingOpenFailure_FallsBackToCanonicalRewrite()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_PreserveExisting_OpenFailure");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "existing-shortcut");
        var helper = new FirstExistingOpenFailingShortcutComHelper();
        var native = new RecordingShortcutFilePersistenceNative();
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

        service.PersistShortcut(
            shortcutPath,
            new ShortcutMutation(
                @"C:\Apps\App.exe",
                "--args",
                @"C:\Apps",
                null,
                ShortcutIconUpdateMode.None,
                "description",
                null,
                1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.Equal(2, helper.WithShortcutCount);
        Assert.Contains(@"C:\Apps\App.exe", File.ReadAllText(shortcutPath));
    }

    [Fact]
    public void PersistShortcut_CallsMetadataCaptureAndDeleteWithoutFileExistsPrecondition()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_NoFileExistsGuard");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "missing.lnk");
        var native = new RecordingShortcutFilePersistenceNative
        {
            NoOpDelete = true
        };
        var service = new ShortcutFilePersistenceService(new TempWritingShortcutComHelper(), native, trustedTempRoot);

        service.PersistShortcut(
            shortcutPath,
            new ShortcutMutation(
                @"C:\Apps\App.exe",
                "--args",
                @"C:\Apps",
                null,
                ShortcutIconUpdateMode.None,
                "description",
                null,
                1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.RecreateCanonical);

        Assert.Equal(1, native.MetadataCaptureAttempts);
        Assert.Equal(1, native.DeleteAttempts);
    }

    private sealed class RecordingShortcutFilePersistenceService : IShortcutFilePersistenceService
    {
        public ShortcutMutation? PreparedMutation { get; private set; }

        public void PersistShortcut(
            string shortcutPath,
            ShortcutMutation mutation,
            ShortcutDestinationMetadataMode metadataMode,
            ShortcutContentMode contentMode)
            => PreparedMutation = mutation;
    }

    private sealed class TempWritingShortcutComHelper : IShortcutComHelper
    {
        public int SaveCount { get; private set; }

        public void RecordSave() => SaveCount++;

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            var shortcut = new TempShortcutObject(path, this);
            return action((dynamic)shortcut);
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            var shortcut = new TempShortcutObject(path, this);
            action((dynamic)shortcut);
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => throw new NotSupportedException();
    }

    private sealed class SourceCopyObservingShortcutComHelper : IShortcutComHelper
    {
        public bool PathExistedBeforeEdit { get; private set; }
        public string? ContentBeforeEdit { get; private set; }
        public bool WasWritableBeforeEdit { get; private set; }

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            PathExistedBeforeEdit = File.Exists(path);
            ContentBeforeEdit = PathExistedBeforeEdit ? File.ReadAllText(path) : null;
            WasWritableBeforeEdit = PathExistedBeforeEdit &&
                                    (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0;
            return action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            PathExistedBeforeEdit = File.Exists(path);
            ContentBeforeEdit = PathExistedBeforeEdit ? File.ReadAllText(path) : null;
            WasWritableBeforeEdit = PathExistedBeforeEdit &&
                                    (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0;
            action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => throw new NotSupportedException();
    }

    private sealed class RecordingShortcutFilePersistenceNative : IShortcutFilePersistenceNative
    {
        public bool ThrowOnPublish { get; set; }
        public bool CreateDestinationBeforeThrow { get; set; }
        public bool NoOpDelete { get; set; }
        public int MetadataCaptureAttempts { get; private set; }
        public int PublishAttempts { get; private set; }
        public int DeleteAttempts { get; private set; }
        public ShortcutFileMetadata? RestoredMetadata { get; private set; }

        public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath)
        {
            MetadataCaptureAttempts++;
            if (!File.Exists(shortcutPath))
                return null;

            return new ShortcutFileMetadata(
                new FileInfo(shortcutPath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                File.GetAttributes(shortcutPath),
                File.GetCreationTimeUtc(shortcutPath),
                File.GetLastWriteTimeUtc(shortcutPath),
                File.GetLastAccessTimeUtc(shortcutPath));
        }

        public void DeleteExistingDestination(string shortcutPath)
        {
            DeleteAttempts++;
            if (NoOpDelete)
                return;

            if (!File.Exists(shortcutPath))
                return;

            File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) & ~FileAttributes.ReadOnly);
            File.Delete(shortcutPath);
        }

        public void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata)
        {
            PublishAttempts++;
            if (ThrowOnPublish)
            {
                if (CreateDestinationBeforeThrow)
                {
                    using var destination = new FileStream(shortcutPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    destination.WriteByte(0x42);
                }

                throw new ShortcutPublishFailureException(
                    "Injected copy failure.",
                    new IOException("Injected copy failure."));
            }

            using (var destination = new FileStream(shortcutPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var source = new FileStream(tempShortcutPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                source.CopyTo(destination);
            }

            if (metadata != null)
            {
                RestoredMetadata = metadata;
                new FileInfo(shortcutPath).SetAccessControl(metadata.Security);
                File.SetCreationTimeUtc(shortcutPath, metadata.CreationTimeUtc);
                File.SetLastWriteTimeUtc(shortcutPath, metadata.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(shortcutPath, metadata.LastAccessTimeUtc);
                File.SetAttributes(shortcutPath, metadata.Attributes);
            }
        }
    }

    private sealed class ParseFailingShortcutComHelper(
        string invalidExistingContent,
        bool blockCleanup) : IShortcutComHelper
    {
        public int WithShortcutCount { get; private set; }

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            WithShortcutCount++;
            FailIfInvalidExistingContent(path);

            return action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            WithShortcutCount++;
            FailIfInvalidExistingContent(path);

            action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => throw new NotSupportedException();

        private void FailIfInvalidExistingContent(string path)
        {
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), invalidExistingContent, StringComparison.Ordinal))
                return;

            if (blockCleanup)
            {
                File.Delete(path);
                Directory.CreateDirectory(path);
            }

            throw new InvalidDataException("Existing destination is not a shortcut.");
        }
    }

    private sealed class FirstExistingOpenFailingShortcutComHelper : IShortcutComHelper
    {
        private bool _failedOnce;

        public int WithShortcutCount { get; private set; }

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            WithShortcutCount++;
            FailOnceForExistingTemp(path);
            return action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            WithShortcutCount++;
            FailOnceForExistingTemp(path);
            action(new TempShortcutObject(path, new TempWritingShortcutComHelper()));
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => throw new NotSupportedException();

        private void FailOnceForExistingTemp(string path)
        {
            if (_failedOnce || !File.Exists(path))
                return;

            _failedOnce = true;
            throw new UnauthorizedAccessException("Failed to open preserved shortcut.");
        }
    }

    private sealed class TempShortcutObject(string path, TempWritingShortcutComHelper owner) : DynamicObject
    {
        private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            ["TargetPath"] = null,
            ["Arguments"] = null,
            ["WorkingDirectory"] = null,
            ["IconLocation"] = null,
            ["Description"] = null,
            ["Hotkey"] = null,
            ["WindowStyle"] = 1
        };

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
            => values.TryGetValue(binder.Name, out result);

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            values[binder.Name] = value;
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            if (binder.Name == nameof(Save))
            {
                Save();
                result = null;
                return true;
            }

            result = null;
            return false;
        }

        private void Save()
        {
            owner.RecordSave();
            File.WriteAllText(
                path,
                string.Join("|", [
                    values["TargetPath"] as string ?? "",
                    values["Arguments"] as string ?? "",
                    values["WorkingDirectory"] as string ?? "",
                    values["IconLocation"] as string ?? "",
                    values["Description"] as string ?? "",
                    values["Hotkey"] as string ?? "",
                    Convert.ToString(values["WindowStyle"], System.Globalization.CultureInfo.InvariantCulture) ?? ""
                ]));
        }
    }
}
