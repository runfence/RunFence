using System.Security.AccessControl;
using System.Dynamic;
using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.Tests;

public class ShortcutWriteAccessServiceTests
{
    [Fact]
    public void Save_NewShortcut_BuildsPreparedStateWithoutReadingExistingShortcut()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_New");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        var persistence = new RecordingShortcutFilePersistenceService();
        var helper = new FakeShortcutComHelper(new FakeShortcut());
        var service = new ShortcutWriteAccessService(persistence);

        service.Save(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "--args",
            @"C:\Apps",
            @"C:\Icons\App.ico,0",
            ShortcutIconUpdateMode.Set,
            null,
            null,
            1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.RecreateCanonical);

        Assert.Equal(0, helper.WithShortcutCount);
        Assert.NotNull(persistence.PreparedMutation);
        Assert.Equal(@"C:\Apps\App.exe", persistence.PreparedMutation!.TargetPath);
        Assert.Equal("--args", persistence.PreparedMutation.Arguments);
        Assert.Equal(@"C:\Apps", persistence.PreparedMutation.WorkingDirectory);
        Assert.Equal(@"C:\Icons\App.ico,0", persistence.PreparedMutation.IconLocation);
        Assert.Equal(ShortcutIconUpdateMode.Set, persistence.PreparedMutation.IconUpdateMode);
    }

    [Fact]
    public void Save_ExistingShortcut_ReadsCurrentStateOnceAndPreservesUntouchedFields()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_Existing");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);
        var helper = new FakeShortcutComHelper(new FakeShortcut
        {
            TargetPath = @"D:\Old\RunFence.Launcher.exe",
            Arguments = "app-id --old",
            WorkingDirectory = @"D:\Old",
            IconLocation = @"D:\Old\icon.ico,0",
            Description = "Pinned shortcut",
            Hotkey = "CTRL+ALT+R",
            WindowStyle = 7
        });
        var persistence = new RecordingShortcutFilePersistenceService();
        var service = new ShortcutWriteAccessService(persistence);

        service.Save(shortcutPath, new ShortcutMutation(
            @"C:\Apps\App.exe",
            "app-id --new",
            @"C:\Apps",
            null,
            ShortcutIconUpdateMode.None,
            "Pinned shortcut",
            "CTRL+ALT+R",
            7),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        Assert.Equal(0, helper.WithShortcutCount);
        Assert.NotNull(persistence.PreparedMutation);
        Assert.Equal(@"C:\Apps\App.exe", persistence.PreparedMutation!.TargetPath);
        Assert.Equal("app-id --new", persistence.PreparedMutation.Arguments);
        Assert.Equal(@"C:\Apps", persistence.PreparedMutation.WorkingDirectory);
        Assert.Null(persistence.PreparedMutation.IconLocation);
        Assert.Equal("Pinned shortcut", persistence.PreparedMutation.Description);
        Assert.Equal("CTRL+ALT+R", persistence.PreparedMutation.Hotkey);
        Assert.Equal(7, persistence.PreparedMutation.WindowStyle);
        Assert.Equal(ShortcutIconUpdateMode.None, persistence.PreparedMutation.IconUpdateMode);
    }

    [Fact]
    public void PersistShortcut_RetriesCopyWithoutRebuildingTrustedTempShortcut()
    {
        using var tempDir = new TempDirectory("ShortcutWriteAccessService_Retry");
        var trustedTempRoot = Path.Combine(tempDir.Path, "trusted-temp");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllText(shortcutPath, "original");
        var helper = new TempWritingShortcutComHelper();
        var native = new RetryingShortcutFilePersistenceNative
        {
            FailCopyAttempts = 1
        };
        var service = new ShortcutFilePersistenceService(helper, native, trustedTempRoot);

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
            ShortcutContentMode.PreserveExisting);

        Assert.Equal(1, helper.SaveCount);
        Assert.Equal(2, native.PublishAttempts);
        Assert.Equal(3, native.DeleteAttempts);
        Assert.True(File.Exists(shortcutPath));
        Assert.Contains(@"C:\Apps\App.exe", File.ReadAllText(shortcutPath));
        Assert.Empty(Directory.GetFiles(trustedTempRoot, "*.lnk", SearchOption.TopDirectoryOnly));
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
        var native = new RetryingShortcutFilePersistenceNative();
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
        var native = new RetryingShortcutFilePersistenceNative();
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
        var native = new RetryingShortcutFilePersistenceNative();
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
        var native = new RetryingShortcutFilePersistenceNative();
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

    private sealed class FakeShortcut
    {
        public string? TargetPath { get; set; }
        public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? IconLocation { get; set; }
        public string? Description { get; set; }
        public string? Hotkey { get; set; }
        public int WindowStyle { get; set; } = 1;
    }

    private sealed class FakeShortcutComHelper(FakeShortcut shortcut) : IShortcutComHelper
    {
        public int WithShortcutCount { get; private set; }

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            WithShortcutCount++;
            return action(shortcut);
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            WithShortcutCount++;
            action(shortcut);
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => new(path, shortcut.TargetPath, shortcut.Arguments, shortcut.WorkingDirectory);
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

    private sealed class RetryingShortcutFilePersistenceNative : IShortcutFilePersistenceNative
    {
        public int FailCopyAttempts { get; set; }
        public int PublishAttempts { get; private set; }
        public int DeleteAttempts { get; private set; }
        public ShortcutFileMetadata? RestoredMetadata { get; private set; }

        public ShortcutFileMetadata? TryCaptureExistingMetadata(string shortcutPath)
        {
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
            if (!File.Exists(shortcutPath))
                return;

            File.SetAttributes(shortcutPath, File.GetAttributes(shortcutPath) & ~FileAttributes.ReadOnly);
            File.Delete(shortcutPath);
        }

        public void PublishPreparedShortcut(string shortcutPath, string tempShortcutPath, ShortcutFileMetadata? metadata)
        {
            PublishAttempts++;
            {
                using var destination = new FileStream(shortcutPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                if (PublishAttempts <= FailCopyAttempts)
                {
                    throw new ShortcutPublishRetryableException(
                        "Injected copy failure.",
                        new IOException("Injected copy failure."));
                }

                using var source = new FileStream(tempShortcutPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
