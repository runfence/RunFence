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
using RunFence.Security;
using System.Security.Cryptography;

namespace RunFence.Tests;

/// <summary>
/// Lightweight lambda-based adapters for service interfaces used in unit tests.
/// </summary>
public sealed class LambdaSessionProvider(Func<SessionContext> getSession) : ISessionProvider
{
    public SessionContext GetSession() => getSession();
}

public static class SessionContextTestExtensions
{
    public static SessionContext WithOwnedPinDerivedKey(this SessionContext session, SecureSecret pinKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pinKey);
        session.ReplacePinDerivedKey(TestSecretFactory.Clone(pinKey));
        return session;
    }
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

public sealed class ByteArrayCredentialEncryptionSpanAdapter(IByteArrayCredentialEncryptionService inner)
    : ICredentialEncryptionSpanService
{
    public byte[] Encrypt(ProtectedString password, ReadOnlySpan<byte> pinDerivedKey)
    {
        var keyCopy = pinDerivedKey.ToArray();
        try
        {
            return inner.Encrypt(password, keyCopy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }

    public ProtectedString Decrypt(byte[] encryptedPassword, ReadOnlySpan<byte> pinDerivedKey)
    {
        var keyCopy = pinDerivedKey.ToArray();
        try
        {
            return inner.Decrypt(encryptedPassword, keyCopy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }
}

public static class TestSecretFactory
{
    public static SecureSecret Create(int length, byte fillValue = 0)
        => new(length, data => data.Fill(fillValue));

    public static SecureSecret FromBytes(params byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new SecureSecret(bytes.Length, data => bytes.CopyTo(data));
    }

    public static SecureSecret Clone(SecureSecret source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] bytes = source.TransformSnapshot(data => data.ToArray());
        try
        {
            return FromBytes(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

public sealed class TestRunFenceLauncherPathProvider(string launcherPath, bool exists) : IRunFenceLauncherPathProvider
{
    public string GetLauncherPath() => launcherPath;

    public bool Exists() => exists;
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
            PrivilegeLevel: PrivilegeLevel.Isolated,
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
public sealed class FakeRunAsAppEditDialogHandler : RunAsAppEditDialogHandler, IDisposable
{
    private readonly SessionContext _handlerSession;
    private readonly SessionContext _shortcutSession;

    public FakeRunAsAppEditDialogHandler(SecureSecret pinKey)
        : this(CreateState(pinKey))
    {
    }

    private FakeRunAsAppEditDialogHandler(State state)
        : base(
            new Mock<IAppStateProvider>().Object,
            new Mock<IAppEntryLauncher>().Object,
            state.HandlerSession,
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
                new LambdaSessionProvider(() => state.ShortcutSession),
                new Mock<IInteractiveUserSidResolver>().Object,
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                new Mock<ILoggingService>().Object),
            new Mock<IAppEditCommitService>().Object)
    {
        _handlerSession = state.HandlerSession;
        _shortcutSession = state.ShortcutSession;
    }

    public void Dispose()
    {
        _shortcutSession.Dispose();
        _handlerSession.Dispose();
    }

    private static State CreateState(SecureSecret pinKey)
        => new(CreateSession(pinKey), CreateSession(pinKey));

    private static SessionContext CreateSession(SecureSecret pinKey)
        => new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithOwnedPinDerivedKey(pinKey);

    private sealed record State(SessionContext HandlerSession, SessionContext ShortcutSession);
}
