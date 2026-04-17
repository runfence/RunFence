using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsPermissionApplierTests : IDisposable
{
    private const string CredentialSid = "S-1-5-21-1000-1000-1000-1001";
    private const string ContainerSid = "S-1-15-2-1-2-3-4-5-6-7";
    private const string GrantPath = @"C:\Data";

    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IQuickAccessPinService> _quickAccessPinService = new();

    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public RunAsPermissionApplierTests()
    {
        _appState.Setup(a => a.Database).Returns(_database);
    }

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession() => new()
    {
        Database = _database,
        CredentialStore = _credentialStore,
        PinDerivedKey = _pinKey
    };

    private RunAsPermissionApplier CreateApplier()
        => new(_pathGrantService.Object, _databaseService.Object, CreateSession(),
            _appState.Object, _log.Object, _quickAccessPinService.Object);

    private static AncestorPermissionResult MakeGrant()
        => new(GrantPath, FileSystemRights.ReadAndExecute);

    // ── ApplyContainerGrant ────────────────────────────────────────────────

    [Fact]
    public void ApplyContainerGrant_EmptySid_DoesNotCallEnsureAccess()
    {
        var applier = CreateApplier();

        applier.ApplyContainerGrant(MakeGrant(), "");

        _pathGrantService.Verify(p => p.EnsureAccess(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void ApplyContainerGrant_EnsureAccessSucceeds_SavesConfigWhenDatabaseModified()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(ContainerSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: true));
        var applier = CreateApplier();

        applier.ApplyContainerGrant(MakeGrant(), ContainerSid);

        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void ApplyContainerGrant_EnsureAccessSucceeds_SkipsSaveWhenNotModified()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(ContainerSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult());
        var applier = CreateApplier();

        applier.ApplyContainerGrant(MakeGrant(), ContainerSid);

        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void ApplyContainerGrant_EnsureAccessThrows_LogsAndSwallows()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        var applier = CreateApplier();

        var ex = Record.Exception(() => applier.ApplyContainerGrant(MakeGrant(), ContainerSid));

        Assert.Null(ex);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    // ── ApplyCredentialGrant ───────────────────────────────────────────────

    [Fact]
    public void ApplyCredentialGrant_GrantAdded_PinsFolders()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: false));
        var applier = CreateApplier();

        applier.ApplyCredentialGrant(MakeGrant(), CredentialSid);

        _quickAccessPinService.Verify(q => q.PinFolders(CredentialSid,
            It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == GrantPath)),
            Times.Once);
    }

    [Fact]
    public void ApplyCredentialGrant_NoGrantAdded_SkipsPinFolders()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: false));
        var applier = CreateApplier();

        applier.ApplyCredentialGrant(MakeGrant(), CredentialSid);

        _quickAccessPinService.Verify(q => q.PinFolders(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Fact]
    public void ApplyCredentialGrant_EnsureAccessThrows_LogsAndSwallows()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        var applier = CreateApplier();

        var ex = Record.Exception(() => applier.ApplyCredentialGrant(MakeGrant(), CredentialSid));

        Assert.Null(ex);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void ApplyCredentialGrant_EnsureAccessSucceeds_SavesConfigWhenDatabaseModified()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: true));
        var applier = CreateApplier();

        applier.ApplyCredentialGrant(MakeGrant(), CredentialSid);

        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void ApplyCredentialGrant_EnsureAccessSucceeds_SkipsSaveWhenNotModified()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: false));
        var applier = CreateApplier();

        applier.ApplyCredentialGrant(MakeGrant(), CredentialSid);

        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Never);
    }
}
