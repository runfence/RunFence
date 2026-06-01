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
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = grantMutatorService.As<ITraverseService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Remove", false))
            .Callback(() => calls.Add("grant-remove"));
        traverseService
            .Setup(p => p.RemoveTraverse(TestSid, @"C:\TraverseRemove"))
            .Callback(() => calls.Add("traverse-remove"));
        grantMutatorService
            .Setup(p => p.UntrackGrant(TestSid, @"C:\GrantUntrack", false))
            .Callback(() => calls.Add("grant-untrack"));
        traverseService
            .Setup(p => p.UntrackTraverse(TestSid, @"C:\TraverseUntrack"))
            .Callback(() => calls.Add("traverse-untrack"));
        grantMutatorService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\Add",
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Callback(() => calls.Add("grant-add"));
        grantMutatorService
            .Setup(p => p.UpdateGrant(
                TestSid,
                @"C:\Modify",
                false,
                It.IsAny<SavedRightsState>(),
                null,
                null))
            .Callback(() => calls.Add("grant-modify"));
        traverseService
            .Setup(p => p.AddTraverse(TestSid, @"C:\Traverse", null))
            .Callback(() => calls.Add("traverse-add"));
        grantMutatorService
            .Setup(p => p.FixGrantAcl(TestSid, @"C:\GrantFix", false))
            .Callback(() => calls.Add("grant-fix"));
        traverseService
            .Setup(p => p.FixTraverseAcl(TestSid, @"C:\TraverseFix"))
            .Callback(() => calls.Add("traverse-fix"));

        var executor = CreateExecutor(grantMutatorService.Object, traverseService.Object, out _);
        var modifiedEntry = new GrantedPathEntry { Path = @"C:\Modify", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) };
        var plan = new AclApplyPlan(
            PendingAdds: [new GrantedPathEntry { Path = @"C:\Add", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) }],
            PendingRemoves: [new GrantedPathEntry { Path = @"C:\Remove", IsDeny = false }],
            PendingModifications:
            [
                new PendingModification(
                    modifiedEntry,
                    WasIsDeny: false,
                    WasOwn: false,
                    NewIsDeny: false,
                    NewRights: modifiedEntry.SavedRights)
            ],
            PendingGrantFixes: [new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false }],
            PendingTraverseAdds: [new GrantedPathEntry { Path = @"C:\Traverse", IsTraverseOnly = true }],
            PendingTraverseRemoves: [new GrantedPathEntry { Path = @"C:\TraverseRemove", IsTraverseOnly = true }],
            PendingTraverseFixes: [new GrantedPathEntry { Path = @"C:\TraverseFix", IsTraverseOnly = true }],
            PendingUntrackGrants: [new GrantedPathEntry { Path = @"C:\GrantUntrack", IsDeny = false }],
            PendingUntrackTraverse: [new GrantedPathEntry { Path = @"C:\TraverseUntrack", IsTraverseOnly = true }],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);

        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        Assert.Empty(result.Errors);
        Assert.False(result.WasCanceled);
        Assert.Equal(
        [
            "grant-remove",
            "traverse-remove",
            "grant-untrack",
            "traverse-untrack",
            "grant-add",
            "grant-modify",
            "traverse-add",
            "grant-fix",
            "traverse-fix"
        ], calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailedOperation_IsTrackedWithStructuredError()
    {
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclRemove,
            @"C:\Fail",
            new InvalidOperationException("boom"));
        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fail", false))
            .Throws(exception);

        var executor = CreateExecutor(grantMutatorService.Object, new Mock<ITraverseService>().Object, out _);
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
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var executor = CreateExecutor(grantMutatorService.Object, new Mock<ITraverseService>().Object, out var stores);
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

        grantMutatorService.Verify(p => p.AddGrant(
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
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var executor = CreateExecutor(grantMutatorService.Object, new Mock<ITraverseService>().Object, out var stores);
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

        grantMutatorService.Verify(p => p.SwitchGrantMode(
            TestSid,
            entry.Path,
            true,
            It.IsAny<SavedRightsState>(),
            It.Is<Func<bool>>(confirm => confirm()),
            additionalStore), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Modification_UsesCommittedModeKeyForSelectedStoreAndErrorIdentity()
    {
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        var exception = CreateGrantOperationException(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\SwitchFallback",
            new InvalidOperationException("acl failed"));
        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
            .Setup(p => p.SwitchGrantMode(
                TestSid,
                @"C:\SwitchFallback",
                true,
                It.IsAny<SavedRightsState>(),
                It.IsAny<Func<bool>>(),
                additionalStore))
            .Throws(exception);

        var executor = CreateExecutor(grantMutatorService.Object, new Mock<ITraverseService>().Object, out var stores);
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
        var traverseService = new Mock<ITraverseService>();
        var executor = CreateExecutor(new Mock<IGrantMutatorService>().Object, traverseService.Object, out var stores);
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

        traverseService.Verify(p => p.AddTraverse(TestSid, @"C:\Traverse", additionalStore), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UntrackAndFixPhases_CallPersistedOperations()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = grantMutatorService.As<ITraverseService>();
        var executor = CreateExecutor(grantMutatorService.Object, traverseService.Object, out _);

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

        grantMutatorService.Verify(p => p.UntrackGrant(TestSid, @"C:\UntrackGrant", false), Times.Once);
        traverseService.Verify(p => p.UntrackTraverse(TestSid, @"C:\UntrackTraverse"), Times.Once);
        grantMutatorService.Verify(p => p.FixGrantAcl(TestSid, @"C:\FixGrant", false), Times.Once);
        traverseService.Verify(p => p.FixTraverseAcl(TestSid, @"C:\FixTraverse"), Times.Once);
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

        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
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

        var executor = CreateExecutor(grantMutatorService.Object, new Mock<ITraverseService>().Object, out _);
        var result = await executor.ExecuteAsync(plan, TestSid, isContainer: false, new Progress<(int current, int total)>());

        var error = Assert.Single(result.Errors);
        Assert.Same(exception, error.Exception);
        Assert.Single(error.Exception.CleanupFailures);
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
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = grantMutatorService.As<ITraverseService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\GrantWarning", false))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [removeWarning]));
        grantMutatorService
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
        traverseService
            .Setup(p => p.RemoveTraverse(TestSid, @"C:\TraverseWarning"))
            .Returns(new GrantApplyResult(
                TraverseApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [traverseWarning]));

        var executor = CreateExecutor(grantMutatorService.Object, traverseService.Object, out _);
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
        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\Cancel",
                true,
                It.IsAny<SavedRightsState?>(),
                It.IsAny<Func<bool>>(),
                null))
            .Throws(new OperationCanceledException("user canceled"));

        var traverseService = new Mock<ITraverseService>();
        var executor = CreateExecutor(grantMutatorService.Object, traverseService.Object, out _);
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
        traverseService.Verify(p => p.AddTraverse(TestSid, @"C:\SkippedTraverse", It.IsAny<IGrantIntentStore?>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedFailure_CapturesFatalFailureAndStopsLaterPhases()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Completed", false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fatal", false))
            .Throws(new InvalidOperationException("boom"));

        var traverseService = new Mock<ITraverseService>();
        var executor = CreateExecutor(grantMutatorService.Object, traverseService.Object, out _);
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
        traverseService.Verify(p => p.AddTraverse(TestSid, @"C:\SkippedTraverse", It.IsAny<IGrantIntentStore?>()), Times.Never);
    }

    private static AclApplyExecutor CreateExecutor(
        IGrantMutatorService grantMutatorService,
        ITraverseService traverseService,
        out TestGrantIntentStoreProvider stores,
        ILoggingService? log = null)
    {
        stores = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var phaseCatalog = new AclApplyPhaseCatalog();
        return new AclApplyExecutor(
            phaseCatalog,
            new AclApplyPhaseExecutor(
                log ?? new Mock<ILoggingService>().Object,
                grantMutatorService,
                traverseService,
                new AclApplySelectedStoreResolver(stores)));
    }

    private static GrantOperationException CreateGrantOperationException(
        GrantApplyFailureStep step,
        string path,
        Exception cause,
        string? configPath = null)
        => new(step, path, configPath, cause);
}
