using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
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
}

/// <summary>
/// Shared test helpers for RunAs-related unit tests.
/// </summary>
public static class RunAsTestHelpers
{
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
            new Mock<IInteractiveUserSidResolver>().Object,
            new Mock<ILoggingService>().Object),
        new Mock<IAppEditCommitService>().Object);
