using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsPermissionApplierTests
{
    private const string CredentialSid = "S-1-5-21-1000-1000-1000-1001";
    private const string ContainerSid = "S-1-15-2-1-2-3-4-5-6-7";
    private const string GrantPath = @"C:\Data";

    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IQuickAccessPinService> _quickAccessPinService = new();

    private RunAsPermissionApplier CreateApplier()
        => new(_pathGrantService.Object, _quickAccessPinService.Object);

    private static AncestorPermissionResult MakeGrant()
        => new(GrantPath, FileSystemRights.ReadAndExecute);

    [Fact]
    public void ApplyContainerGrant_EmptySid_DoesNotCallEnsureAccess()
    {
        var applier = CreateApplier();

        var result = applier.ApplyContainerGrant(MakeGrant(), "");

        Assert.False(result.DatabaseModified);
        _pathGrantService.Verify(p => p.EnsureAccess(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<string, string, bool>?>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void ApplyContainerGrant_GrantThrows_PropagatesException()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(ContainerSid, GrantPath, FileSystemRights.ReadAndExecute,
                null, false))
            .Throws(CreateGrantFailure(GrantApplyFailureStep.GrantIntentSave, "save failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            CreateApplier().ApplyContainerGrant(MakeGrant(), ContainerSid));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
    }

    [Fact]
    public void ApplyCredentialGrant_GrantApplied_PinsFolders()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute,
                null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = CreateApplier().ApplyCredentialGrant(MakeGrant(), CredentialSid);

        Assert.True(result.GrantApplied);
        _quickAccessPinService.Verify(q => q.PinFolders(CredentialSid,
            It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == GrantPath)),
            Times.Once);
    }

    [Fact]
    public void ApplyCredentialGrant_TraverseOnly_DoesNotPinFolders()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(CredentialSid, GrantPath, FileSystemRights.ReadAndExecute,
                null, false))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = CreateApplier().ApplyCredentialGrant(MakeGrant(), CredentialSid);

        Assert.True(result.TraverseApplied);
        _quickAccessPinService.Verify(q => q.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void ApplyCredentialGrant_GrantThrows_PropagatesExceptionAndDoesNotPin()
    {
        _pathGrantService
            .Setup(p => p.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(CreateGrantFailure(GrantApplyFailureStep.GrantAclApply, "Access denied"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            CreateApplier().ApplyCredentialGrant(MakeGrant(), CredentialSid));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        _quickAccessPinService.Verify(q => q.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    private static GrantOperationException CreateGrantFailure(GrantApplyFailureStep step, string message)
        => new(step, GrantPath, null, new InvalidOperationException(message));
}
