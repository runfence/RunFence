using Moq;
using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.Tests;

public sealed class ManagedShortcutLifecycleServiceTests
{
    [Fact]
    public void DeleteManagedShortcutFile_DeleteFailure_DoesNotClearProtectionState()
    {
        var native = new Mock<IShortcutFilePersistenceNative>(MockBehavior.Strict);
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        native.Setup(n => n.DeleteExistingDestination(shortcutPath))
            .Throws(new IOException("deny"));
        var service = new ManagedShortcutLifecycleService(native.Object, Mock.Of<IShortcutWriteAccessService>());

        var ex = Assert.Throws<IOException>(() => service.DeleteManagedShortcutFile(shortcutPath));

        Assert.Equal("deny", ex.Message);
    }

    [Fact]
    public void DeleteManagedShortcutFile_DeleteSuccess_DoesNotTouchProtectionState()
    {
        var native = new Mock<IShortcutFilePersistenceNative>(MockBehavior.Strict);
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        native.Setup(n => n.DeleteExistingDestination(shortcutPath));
        var service = new ManagedShortcutLifecycleService(native.Object, Mock.Of<IShortcutWriteAccessService>());

        service.DeleteManagedShortcutFile(shortcutPath);
    }

    [Fact]
    public void RewriteManagedShortcutFile_WriteFailure_DoesNotClearProtectionState()
    {
        var writer = new Mock<IShortcutWriteAccessService>(MockBehavior.Strict);
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var mutation = new ShortcutMutation(@"C:\Apps\App.exe", "--args", @"C:\Apps", null, ShortcutIconUpdateMode.None, null, null, 1);
        writer.Setup(w => w.Save(shortcutPath, mutation, ShortcutDestinationMetadataMode.PreserveExisting, ShortcutContentMode.PreserveExisting))
            .Throws(new IOException("write failed"));
        var service = new ManagedShortcutLifecycleService(Mock.Of<IShortcutFilePersistenceNative>(), writer.Object);

        var ex = Assert.Throws<IOException>(() => service.RewriteManagedShortcutFile(
            shortcutPath,
            mutation,
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting));

        Assert.Equal("write failed", ex.Message);
    }

    [Fact]
    public void RewriteManagedShortcutFile_WriteSuccess_DoesNotTouchProtectionState()
    {
        var writer = new Mock<IShortcutWriteAccessService>(MockBehavior.Strict);
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var mutation = new ShortcutMutation(@"C:\Apps\App.exe", "--args", @"C:\Apps", null, ShortcutIconUpdateMode.None, null, null, 1);
        writer.Setup(w => w.Save(shortcutPath, mutation, ShortcutDestinationMetadataMode.PreserveExisting, ShortcutContentMode.PreserveExisting));
        var service = new ManagedShortcutLifecycleService(Mock.Of<IShortcutFilePersistenceNative>(), writer.Object);

        service.RewriteManagedShortcutFile(
            shortcutPath,
            mutation,
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);
    }
}
