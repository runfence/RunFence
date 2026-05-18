using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AutoStartShortcutStoreTests
{
    [Fact]
    public void PrimaryShortcutPath_UsesInjectedInteractiveUserProfilePath_WhenResolverReturnsSid()
    {
        const string interactiveSid = "S-1-5-21-9-9-9-1001";
        const string profilePath = @"C:\Users\Interactive";
        var profileResolver = new Mock<IProfilePathResolver>();
        var interactiveUserSidResolver = new Mock<IInteractiveUserSidResolver>();
        profileResolver.Setup(r => r.TryGetProfilePath(interactiveSid)).Returns((string?)profilePath);
        interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);

        var store = new AutoStartShortcutStore(profileResolver.Object, interactiveUserSidResolver.Object);

        var expectedStartupFolder = Path.Combine(
            profilePath,
            @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");

        Assert.StartsWith(expectedStartupFolder, store.PrimaryShortcutPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrimaryShortcutPath_FallsBackToCurrentStartupFolder_WhenResolverReturnsNull()
    {
        var profileResolver = new Mock<IProfilePathResolver>();
        var interactiveUserSidResolver = new Mock<IInteractiveUserSidResolver>();
        interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var store = new AutoStartShortcutStore(profileResolver.Object, interactiveUserSidResolver.Object);

        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            store.PrimaryShortcutPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
