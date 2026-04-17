using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.RunAs.UI;

namespace RunFence.Tests;

/// <summary>
/// Lightweight lambda-based adapters for service interfaces used in unit tests.
/// </summary>
public sealed class LambdaSessionProvider(Func<SessionContext> getSession) : ISessionProvider
{
    public SessionContext GetSession() => getSession();
}

public sealed class InlineUiThreadInvoker(Action<Action> invoke) : IUiThreadInvoker
{
    public T Invoke<T>(Func<T> func)
    {
        T result = default!;
        invoke(() => result = func());
        return result;
    }

    // void Invoke(Action) — uses default interface impl (forwards to Invoke<T> via VoidStruct)
    public void BeginInvoke(Action action) => invoke(action);
    public void RunOnUiThread(Action action) => invoke(action);
}

/// <summary>
/// Shared test helpers for RunAs-related unit tests.
/// </summary>
public static class RunAsTestHelpers
{
    /// <summary>
    /// Creates a stub <see cref="IShortcutDiscoveryService"/> that returns an empty traversal cache.
    /// Shared across RunAs test classes that need to construct <see cref="RunAsAppEntryManager"/>.
    /// </summary>
    public static IShortcutDiscoveryService StubShortcutDiscovery()
    {
        var shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(() => new ShortcutTraversalCache([]));
        return shortcutDiscovery.Object;
    }

    /// <summary>
    /// Builds a <see cref="RunAsAppEntryManager"/> with all-mock dependencies suitable for tests
    /// that need to pass it as a constructor argument but never actually exercise it.
    /// The <paramref name="pinKey"/> lifetime is owned by the caller.
    /// </summary>
    public static RunAsAppEntryManager CreateFakeAppEntryManager(ProtectedBuffer pinKey)
    {
        var db = new AppDatabase();
        var appState = new Mock<IAppStateProvider>();
        appState.Setup(a => a.Database).Returns(db);
        var session = new SessionContext { Database = db, CredentialStore = new CredentialStore(), PinDerivedKey = pinKey };
        var log = new Mock<ILoggingService>();
        var shortcutService = new Mock<IShortcutService>();
        var sidNameCache = new Mock<ISidNameCacheService>();
        var sessionProvider = new LambdaSessionProvider(() => session);
        var shortcutCreator = new RunAsAppShortcutCreator(
            new Mock<IIconService>().Object,
            sidNameCache.Object,
            shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            sessionProvider,
            log.Object);
        return new RunAsAppEntryManager(
            appState.Object,
            new Mock<IUiThreadInvoker>().Object,
            new Mock<IDataChangeNotifier>().Object,
            log.Object,
            session,
            new Mock<IAppConfigService>().Object,
            new Mock<IAclService>().Object,
            new AppEntryEnforcementHelper(
                new Mock<IAclService>().Object,
                shortcutService.Object,
                new Mock<IBesideTargetShortcutService>().Object,
                new Mock<IIconService>().Object,
                sidNameCache.Object,
                new Mock<IInteractiveUserDesktopProvider>().Object,
                new Mock<ILoggingService>().Object),
            StubShortcutDiscovery(),
            new Mock<ILicenseService>().Object,
            shortcutCreator);
    }

    /// <summary>
    /// Constructs a <see cref="RunAsDialogResult"/> representing a container selection.
    /// Covers both the <c>createAppEntryOnly</c> dispatch path (used in dispatcher tests) and
    /// the <c>permissionGrant</c> path (used in processor tests).
    /// </summary>
    public static RunAsDialogResult MakeContainerResult(
        AppContainerEntry container,
        AppEntry? existingApp = null,
        bool createAppEntryOnly = false,
        AncestorPermissionResult? permissionGrant = null)
        => new(
            Credential: null,
            SelectedContainer: container,
            PermissionGrant: permissionGrant,
            CreateAppEntryOnly: createAppEntryOnly,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: existingApp);
}

/// <summary>
/// Minimal stub that satisfies constructors requiring <see cref="RunAsAppEditDialogHandler"/>
/// without opening any WinForms dialogs. The dialog factory throws
/// <see cref="InvalidOperationException"/> to prove the dialog path was entered when needed.
/// The <paramref name="pinKey"/> lifetime is owned by the caller.
/// </summary>
public sealed class FakeRunAsAppEditDialogHandler(ProtectedBuffer pinKey)
    : RunAsAppEditDialogHandler(
        new Mock<IAppStateProvider>().Object,
        new Mock<IAppEntryLauncher>().Object,
        new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
            PinDerivedKey = pinKey
        },
        () => throw new InvalidOperationException("AppEditDialog must not be opened in unit tests"),
        new AppEntryPermissionPrompter(
            new Mock<ILoggingService>().Object,
            new Mock<IAclPermissionService>().Object,
            new Mock<IPathGrantService>().Object,
            new LambdaDatabaseProvider(() => new AppDatabase()),
            new Mock<IQuickAccessPinService>().Object),
        new Mock<IModalCoordinator>().Object,
        new Mock<IRunAsLaunchErrorHandler>().Object,
        new RunAsAppShortcutCreator(
            new Mock<IIconService>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IBesideTargetShortcutService>().Object,
            new LambdaSessionProvider(() => new SessionContext
            {
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
                PinDerivedKey = pinKey
            }),
            new Mock<ILoggingService>().Object),
        new Mock<IAppEditCommitService>().Object);
