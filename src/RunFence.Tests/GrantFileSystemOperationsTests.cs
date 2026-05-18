using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class GrantFileSystemOperationsTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ExistingDir = @"C:\Existing\TestDir";

    private static readonly SavedRightsState ReadOnly =
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    private static FileSystemSecurity EmptySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    private static (GrantFileSystemOperations Operations, AppDatabase Database, Mock<IFileOwnerService> OwnerMock, Mock<IAclAccessor> AclAccessor) Build()
    {
        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>();
        var ownerMock = new Mock<IFileOwnerService>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var pathInfo = new TestFileSystemPathInfo();
        pathInfo.AddDirectory(Path.GetPathRoot(ExistingDir)!);
        pathInfo.AddDirectory(ExistingDir);
        aclAccessor.Setup(a => a.GetSecurity(ExistingDir)).Returns(EmptySecurity());

        var db = new AppDatabase();
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var grantAceService = new GrantAceService(aclAccessor.Object, pathInfo);
        var grantCore = new GrantCoreOperations(grantAceService, ownerMock.Object, dbAccessor, log.Object, pathInfo);
        var operations = new GrantFileSystemOperations(grantCore, grantAceService, ownerMock.Object,
            mandatoryLabelMock.Object, dbAccessor);
        return (operations, db, ownerMock, aclAccessor);
    }

    [Fact]
    public void AddGrant_WithOwnerSid_DelegatesOwnerChange()
    {
        var (operations, _, ownerMock, _) = Build();
        const string ownerSid = "S-1-5-21-9999-9999-9999-1001";

        operations.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, ownerSid: ownerSid);

        ownerMock.Verify(o => o.ChangeOwner(ExistingDir, ownerSid, false), Times.Once);
    }

    [Fact]
    public void RemoveGrant_SourceSid_DoesNotPerformLowIntegritySourceSync()
    {
        var (operations, db, _, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly);
        db.GetOrCreateAccount(AclHelper.LowIntegritySid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly,
            SourceSids = [UserSid]
        });

        var removed = operations.RemoveGrant(UserSid, ExistingDir, isDeny: false, updateFileSystem: false);

        Assert.True(removed);
        Assert.NotNull(db.GetAccount(AclHelper.LowIntegritySid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false } &&
                                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CheckGrantStatus_PathExistsWithoutMatchingAce_ReturnsBroken()
    {
        var (operations, _, _, _) = Build();

        var status = operations.CheckGrantStatus(ExistingDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, status);
    }
}
