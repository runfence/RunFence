using Moq;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPhaseExecutorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";

    [Fact]
    public async Task ExecutePhaseAsync_GrantAdd_UsesResolvedStore_ReportsProgress_AndMarksCompleted()
    {
        var log = new Mock<ILoggingService>();
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var storeProvider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        storeProvider.AddLoadedStore(additionalStore);
        var executor = new AclApplyPhaseExecutor(
            log.Object,
            grantMutatorService.Object,
            traverseService.Object,
            new AclApplySelectedStoreResolver(storeProvider));
        var progressCalls = new List<(int current, int total)>();
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Grant",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var plan = new AclApplyPlan(
            PendingAdds: [entry],
            PendingRemoves: [],
            PendingModifications: [],
            PendingGrantFixes: [],
            PendingTraverseAdds: [],
            PendingTraverseRemoves: [],
            PendingTraverseFixes: [],
            PendingUntrackGrants: [],
            PendingUntrackTraverse: [],
            PendingConfigMoves: [],
            PendingTraverseConfigMoves: []);
        var result = new AclApplyExecutionResult();
        var context = new AclApplyPhaseExecutionContext(
            plan,
            TestSid,
            new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer())
            {
                [(Path.GetFullPath(entry.Path), entry.IsDeny)] = additionalStore.ConfigPath
            },
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            result,
            new SynchronousProgress<(int current, int total)>(value => progressCalls.Add(value)),
            Total: 4);

        var current = await executor.ExecutePhaseAsync(
            new AclApplyPhaseDescriptor(AclApplyPhase.GrantAdd, AclPendingOperationKind.GrantAdd),
            context,
            current: 0);

        Assert.Equal(1, current);
        Assert.True(result.WasCompleted(AclPendingOperationKind.GrantAdd, entry.Path, entry.IsDeny));
        Assert.Equal([(1, 4)], progressCalls);
        grantMutatorService.Verify(service => service.AddGrant(
            TestSid,
            entry.Path,
            entry.IsDeny,
            entry.SavedRights,
            null,
            additionalStore), Times.Once);
    }

    [Fact]
    public async Task ExecutePhaseAsync_GrantModification_FallsBackToCommittedModeStore()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var storeProvider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var committedStore = new TestGrantIntentStore(@"C:\Configs\committed.rfn");
        storeProvider.AddLoadedStore(committedStore);
        var executor = new AclApplyPhaseExecutor(
            new Mock<ILoggingService>().Object,
            grantMutatorService.Object,
            new Mock<ITraverseService>().Object,
            new AclApplySelectedStoreResolver(storeProvider));
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Switch",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var modification = new PendingModification(
            entry,
            WasIsDeny: true,
            WasOwn: false,
            NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(true),
            WasRights: entry.SavedRights);
        var context = new AclApplyPhaseExecutionContext(
            new AclApplyPlan(
                PendingAdds: [],
                PendingRemoves: [],
                PendingModifications: [modification],
                PendingGrantFixes: [],
                PendingTraverseAdds: [],
                PendingTraverseRemoves: [],
                PendingTraverseFixes: [],
                PendingUntrackGrants: [],
                PendingUntrackTraverse: [],
                PendingConfigMoves: [],
                PendingTraverseConfigMoves: []),
            TestSid,
            new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer())
            {
                [(Path.GetFullPath(entry.Path), entry.IsDeny)] = committedStore.ConfigPath
            },
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            new AclApplyExecutionResult(),
            new Progress<(int current, int total)>(),
            Total: 1);

        await executor.ExecutePhaseAsync(
            new AclApplyPhaseDescriptor(AclApplyPhase.GrantModification, AclPendingOperationKind.GrantModification),
            context,
            current: 0);

        grantMutatorService.Verify(service => service.SwitchGrantMode(
            TestSid,
            entry.Path,
            true,
            It.IsAny<SavedRightsState>(),
            It.Is<Func<bool>>(confirm => confirm()),
            committedStore), Times.Once);
    }

    [Fact]
    public async Task ExecutePhaseAsync_UnexpectedFailure_SetsFatalFailureForPhaseOperation()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        grantMutatorService
            .Setup(service => service.FixGrantAcl(TestSid, @"C:\Fatal", false))
            .Throws(new InvalidOperationException("boom"));
        var executor = new AclApplyPhaseExecutor(
            new Mock<ILoggingService>().Object,
            grantMutatorService.Object,
            new Mock<ITraverseService>().Object,
            new AclApplySelectedStoreResolver(new TestGrantIntentStoreProvider(new TestGrantIntentStore())));
        var context = new AclApplyPhaseExecutionContext(
            new AclApplyPlan(
                PendingAdds: [],
                PendingRemoves: [],
                PendingModifications: [],
                PendingGrantFixes: [new GrantedPathEntry { Path = @"C:\Fatal", IsDeny = false }],
                PendingTraverseAdds: [],
                PendingTraverseRemoves: [],
                PendingTraverseFixes: [],
                PendingUntrackGrants: [],
                PendingUntrackTraverse: [],
                PendingConfigMoves: [],
                PendingTraverseConfigMoves: []),
            TestSid,
            new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer()),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            new AclApplyExecutionResult(),
            new Progress<(int current, int total)>(),
            Total: 1);

        var current = await executor.ExecutePhaseAsync(
            new AclApplyPhaseDescriptor(AclApplyPhase.GrantFix, AclPendingOperationKind.GrantFix),
            context,
            current: 0);

        Assert.Equal(0, current);
        var fatal = Assert.IsType<AclApplyFatalFailure>(context.Result.FatalFailure);
        Assert.Equal(AclPendingOperationKind.GrantFix, fatal.OperationKind);
        Assert.Equal(@"C:\Fatal", fatal.Path);
        Assert.Equal(GrantApplyFailureStep.FixGrantAclApply, fatal.Exception.Step);
    }

    [Fact]
    public async Task ExecutePhaseAsync_GrantOperationFailure_AddsStructuredErrorAndContinuesCounting()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var firstFailure = new GrantOperationException(
            GrantApplyFailureStep.GrantAclRemove,
            @"C:\Fail",
            null,
            new InvalidOperationException("fail"));
        grantMutatorService
            .Setup(service => service.RemoveGrant(TestSid, @"C:\Fail", false))
            .Throws(firstFailure);
        grantMutatorService
            .Setup(service => service.RemoveGrant(TestSid, @"C:\Ok", false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var executor = new AclApplyPhaseExecutor(
            new Mock<ILoggingService>().Object,
            grantMutatorService.Object,
            new Mock<ITraverseService>().Object,
            new AclApplySelectedStoreResolver(new TestGrantIntentStoreProvider(new TestGrantIntentStore())));
        var context = new AclApplyPhaseExecutionContext(
            new AclApplyPlan(
                PendingAdds: [],
                PendingRemoves:
                [
                    new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false },
                    new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false }
                ],
                PendingModifications: [],
                PendingGrantFixes: [],
                PendingTraverseAdds: [],
                PendingTraverseRemoves: [],
                PendingTraverseFixes: [],
                PendingUntrackGrants: [],
                PendingUntrackTraverse: [],
                PendingConfigMoves: [],
                PendingTraverseConfigMoves: []),
            TestSid,
            new Dictionary<(string Path, bool IsDeny), string?>(new GrantPathKeyComparer()),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            new AclApplyExecutionResult(),
            new Progress<(int current, int total)>(),
            Total: 2);

        var current = await executor.ExecutePhaseAsync(
            new AclApplyPhaseDescriptor(AclApplyPhase.GrantRemove, AclPendingOperationKind.GrantRemove),
            context,
            current: 0);

        Assert.Equal(2, current);
        var error = Assert.Single(context.Result.Errors);
        Assert.Same(firstFailure, error.Exception);
        Assert.True(context.Result.WasCompleted(AclPendingOperationKind.GrantRemove, @"C:\Ok", false));
    }
    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
