using Moq;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclApplyExecutorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";

    [Fact]
    public async Task ExecuteAsync_RunsRemoveBeforeAddAndTraverse()
    {
        var calls = new List<string>();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Remove", false))
            .Callback(() => calls.Add("remove"));
        pathGrantService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\Add",
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Callback(() => calls.Add("add"));
        pathGrantService
            .Setup(p => p.AddTraverse(TestSid, @"C:\Traverse", null))
            .Callback(() => calls.Add("traverse"));

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Add", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) }],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\Remove", IsDeny = false }],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true }],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        Assert.Empty(result.Errors);
        Assert.False(result.WasCanceled);
        Assert.Equal(["remove", "add", "traverse"], calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailedOperation_IsTrackedWithStructuredError()
    {
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclRemove,
            @"C:\Fail",
            new InvalidOperationException("boom"));
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fail", false))
            .Throws(exception);

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false }],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        var error = Assert.Single(result.Errors);
        Assert.Equal(AclPendingOperationKind.GrantRemove, error.OperationKind);
        Assert.Equal(@"C:\Fail", error.Path);
        Assert.False(error.IsDeny);
        Assert.Same(exception, error.Exception);
        Assert.False(result.WasCanceled);
    }

    [Fact]
    public async Task ExecuteAsync_GrantAdd_UsesSelectedStore()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var executor = CreateExecutor(pathGrantService.Object, out var stores);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);

        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Add", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) }],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [new PendingConfigMove(new GrantedPathEntry { Path = @"C:\Add", IsDeny = false }, additionalStore.ConfigPath)],
            PendingTraverseConfigMoves: []);

        await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        pathGrantService.Verify(p => p.AddGrant(
            TestSid,
            @"C:\Add",
            false,
            It.IsAny<SavedRightsState?>(),
            null,
            additionalStore), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Modification_UsesSelectedStoreAndSwitchGrantMode()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var executor = CreateExecutor(pathGrantService.Object, out var stores);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Switch",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };

        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications:
            [
                new PendingModification(
                    entry,
                    WasIsDeny: false,
                    WasOwn: false,
                    NewIsDeny: true,
                    NewRights: SavedRightsState.DefaultForMode(true),
                    WasRights: entry.SavedRights)
            ],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [new PendingConfigMove(new GrantedPathEntry { Path = entry.Path, IsDeny = true }, additionalStore.ConfigPath)],
            PendingTraverseConfigMoves: []);

        await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        pathGrantService.Verify(p => p.SwitchGrantMode(
            TestSid,
            entry.Path,
            true,
            It.IsAny<SavedRightsState>(),
            It.Is<Func<bool>>(confirm => confirm()),
            additionalStore), Times.Once);
        pathGrantService.Verify(p => p.ResetOwner(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Modification_UsesCommittedModeKeyForSelectedStoreAndErrorIdentity()
    {
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\SwitchFallback",
            new InvalidOperationException("acl failed"));
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.SwitchGrantMode(
                TestSid,
                @"C:\SwitchFallback",
                true,
                It.IsAny<SavedRightsState>(),
                It.IsAny<Func<bool>>(),
                additionalStore))
            .Throws(exception);

        var executor = CreateExecutor(pathGrantService.Object, out var stores);
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\SwitchFallback",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };

        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications:
            [
                new PendingModification(
                    entry,
                    WasIsDeny: true,
                    WasOwn: false,
                    NewIsDeny: true,
                    NewRights: SavedRightsState.DefaultForMode(true),
                    WasRights: entry.SavedRights)
            ],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [new PendingConfigMove(new GrantedPathEntry { Path = entry.Path, IsDeny = entry.IsDeny }, additionalStore.ConfigPath)],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        var error = Assert.Single(result.Errors);
        Assert.Equal(AclPendingOperationKind.GrantModification, error.OperationKind);
        Assert.Equal(entry.Path, error.Path);
        Assert.False(error.IsDeny);
        Assert.Same(exception, error.Exception);
    }

    [Fact]
    public async Task ExecuteAsync_TraverseAdd_UsesSelectedStore()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var executor = CreateExecutor(pathGrantService.Object, out var stores);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);

        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true }],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: [new PendingConfigMove(new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true }, additionalStore.ConfigPath)]);

        await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        pathGrantService.Verify(p => p.AddTraverse(TestSid, @"C:\Traverse", additionalStore), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UntrackAndFixPhases_CallPersistedOperations()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var executor = CreateExecutor(pathGrantService.Object, out _);

        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [new GrantedPathEntry { Path = @"C:\FixGrant", IsDeny = false }],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [new GrantedPathEntry { Path = @"C:\FixTraverse", IsTraverseOnly = true }],
            PendingUntrackGrants: [new GrantedPathEntry { Path = @"C:\UntrackGrant", IsDeny = false }],
            PendingUntrackTraverse: [new GrantedPathEntry { Path = @"C:\UntrackTraverse", IsTraverseOnly = true }],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        pathGrantService.Verify(p => p.UntrackGrant(TestSid, @"C:\UntrackGrant", false), Times.Once);
        pathGrantService.Verify(p => p.UntrackTraverse(TestSid, @"C:\UntrackTraverse"), Times.Once);
        pathGrantService.Verify(p => p.FixGrantAcl(TestSid, @"C:\FixGrant", false), Times.Once);
        pathGrantService.Verify(p => p.FixTraverseAcl(TestSid, @"C:\FixTraverse"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GrantOperationFailureWithCleanupFailures_IsTrackedOnce()
    {
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\Modify",
            new InvalidOperationException("acl failed"));
        exception.AppendCleanupFailure(
            GrantApplyFailureStep.RevertIntentSave,
            @"C:\Modify",
            @"C:\Configs\extra.rfn",
            new InvalidOperationException("rollback save failed"));

        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.UpdateGrant(
                TestSid,
                @"C:\Modify",
                false,
                It.IsAny<SavedRightsState>(),
                null,
                null))
            .Throws(exception);

        var entry = new GrantedPathEntry
        {
            Path = @"C:\Modify",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [],
            PendingModifications:
            [
                new PendingModification(
                    entry,
                    WasIsDeny: false,
                    WasOwn: false,
                    NewIsDeny: false,
                    NewRights: entry.SavedRights! with { Execute = true },
                    WasRights: entry.SavedRights)
            ],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        var error = Assert.Single(result.Errors);
        Assert.Same(exception, error.Exception);
        Assert.Single(error.Exception.CleanupFailures);
    }

    [Fact]
    public async Task ExecuteAsync_GrantOperationFailure_LogsFormattedMessageAndRetainsExceptionObject()
    {
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclRemove,
            @"C:\LogFailure",
            new InvalidOperationException("boom"),
            @"C:\Configs\extra.rfn");
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\LogFailure", false))
            .Throws(exception);
        var log = new Mock<ILoggingService>();

        var executor = CreateExecutor(pathGrantService.Object, out _, log.Object);
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\LogFailure", IsDeny = false }],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        var error = Assert.Single(result.Errors);
        log.Verify(x => x.Error(
            GrantApplyFailureFormatter.Format(exception.Step, exception.Path, exception.ConfigPath, exception.Cause),
            exception), Times.Once);
        Assert.Same(exception, error.Exception);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesWarningsFromGrantAndTraverseOperations()
    {
        var removeWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantRemoveSave,
            @"C:\GrantWarning",
            null,
            new InvalidOperationException("grant save warning"));
        var addWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\AddWarning",
            @"C:\Configs\extra.rfn",
            new InvalidOperationException("add save warning"));
        var traverseWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostTraverseRemoveSave,
            @"C:\TraverseWarning",
            null,
            new InvalidOperationException("traverse save warning"));
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\GrantWarning", false))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [removeWarning]));
        pathGrantService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\AddWarning",
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [addWarning]));
        pathGrantService
            .Setup(p => p.RemoveTraverse(TestSid, @"C:\TraverseWarning"))
            .Returns(new GrantApplyResult(
                TraverseApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [traverseWarning]));

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\AddWarning", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) }],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\GrantWarning", IsDeny = false }],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [new GrantedPathEntry { Path = @"C:\TraverseWarning", IsTraverseOnly = true }],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        Assert.False(result.WasCanceled);
        Assert.Empty(result.Errors);
        Assert.Equal([removeWarning, traverseWarning, addWarning], result.Warnings);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_StopsCurrentApplyWithoutRecordingError()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\Cancel",
                true,
                It.IsAny<SavedRightsState?>(),
                It.IsAny<Func<bool>>(),
                null))
            .Throws(new OperationCanceledException("user canceled"));

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Cancel", IsDeny = true, SavedRights = SavedRightsState.DefaultForMode(true) }],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\SkippedTraverse", IsTraverseOnly = true }],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        Assert.True(result.WasCanceled);
        Assert.Empty(result.Errors);
        pathGrantService.Verify(p => p.AddTraverse(TestSid, @"C:\SkippedTraverse", It.IsAny<IGrantIntentStore?>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedFailure_CapturesFatalFailureAndStopsLaterPhases()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Completed", false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fatal", false))
            .Throws(new InvalidOperationException("boom"));

        var executor = CreateExecutor(pathGrantService.Object, out _);
        var plan = new AclApplyPlan(
            PendingAdds: [],
            PendingRemoves:
            [
                new GrantedPathEntry { Path = @"C:\Completed", IsDeny = false },
                new GrantedPathEntry { Path = @"C:\Fatal", IsDeny = false }
            ],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\SkippedTraverse", IsTraverseOnly = true }],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        Assert.False(result.WasCanceled);
        Assert.Empty(result.Errors);
        Assert.True(result.WasCompleted(AclPendingOperationKind.GrantRemove, @"C:\Completed", false));
        var fatal = Assert.IsType<AclApplyFatalFailure>(result.FatalFailure);
        Assert.Equal(AclPendingOperationKind.GrantRemove, fatal.OperationKind);
        Assert.Equal(@"C:\Fatal", fatal.Path);
        Assert.False(fatal.IsDeny);
        Assert.Equal(GrantApplyFailureStep.GrantAclRemove, fatal.Exception.Step);
        Assert.IsType<InvalidOperationException>(fatal.Exception.Cause);
        Assert.Equal("boom", fatal.Exception.Cause.Message);
        pathGrantService.Verify(p => p.AddTraverse(TestSid, @"C:\SkippedTraverse", It.IsAny<IGrantIntentStore?>()), Times.Never);
    }

    private static AclApplyExecutor CreateExecutor(
        IPathGrantService pathGrantService,
        out TestGrantIntentStoreProvider stores,
        ILoggingService? log = null)
    {
        stores = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        return new AclApplyExecutor(
            log ?? new Mock<ILoggingService>().Object,
            pathGrantService,
            stores);
    }

    private static GrantOperationException CreateGrantOperationException(
        GrantApplyFailureStep step,
        string path,
        Exception cause,
        string? configPath = null)
        => new(step, path, configPath, cause);
}
