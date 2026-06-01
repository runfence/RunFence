using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclManagerApplyOrchestratorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private static readonly AclApplyPhaseCatalog PhaseCatalog = new();

    [Fact]
    public async Task Apply_RemovePhase_CallsPersistedRemoveGrant()
    {
        var (orchestrator, grantMutatorService, _) = CreateOrchestrator();
        var pending = new AclManagerPendingChanges();
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\ToRemove", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        grantMutatorService.Verify(p => p.RemoveGrant(TestSid, @"C:\ToRemove", false), Times.Once);
    }

[Fact]
    public async Task Apply_AddPhase_CallsPersistedAddGrant()
    {
        var (orchestrator, grantMutatorService, _) = CreateOrchestrator();
        var rights = SavedRightsState.DefaultForMode(false);
        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry
        {
            Path = @"C:\ToAdd",
            IsDeny = false,
            SavedRights = rights
        });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        grantMutatorService.Verify(p => p.AddGrant(TestSid, @"C:\ToAdd", false, rights, null, null), Times.Once);
    }

[Fact]
    public async Task Apply_Modifications_UsePersistedGrantOperationsWithoutDirectResetOwner()
    {
        var (orchestrator, grantMutatorService, _) = CreateOrchestrator();
        var allowEntry = new GrantedPathEntry
        {
            Path = @"C:\AllowOwn",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };
        var switchEntry = new GrantedPathEntry
        {
            Path = @"C:\SwitchOwn",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };

        var pending = new AclManagerPendingChanges();
        pending.Grants.ModifyGrant(allowEntry, new PendingModification(
            allowEntry,
            WasIsDeny: false,
            WasOwn: true,
            NewIsDeny: false,
            NewRights: allowEntry.SavedRights! with { Own = false },
            WasRights: allowEntry.SavedRights));
        pending.Grants.ModifyGrant(switchEntry, new PendingModification(
            switchEntry,
            WasIsDeny: false,
            WasOwn: true,
            NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(true),
            WasRights: switchEntry.SavedRights));
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        grantMutatorService.Verify(p => p.UpdateGrant(
            TestSid,
            allowEntry.Path,
            false,
            It.Is<SavedRightsState>(rights => rights.Own == false),
            null,
            null), Times.Once);
        grantMutatorService.Verify(p => p.SwitchGrantMode(
            TestSid,
            switchEntry.Path,
            true,
            It.IsAny<SavedRightsState>(),
            It.Is<Func<bool>>(confirm => confirm()),
            null), Times.Once);
    }

    [Fact]
    public async Task Apply_AllSuccess_ClearsPendingState()
    {
        var (orchestrator, _, _) = CreateOrchestrator();
        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\Cleared", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        Assert.False(pending.HasPendingChanges);
    }

    [Fact]
    public async Task Apply_SaveConfigThrows_KeepsCommittedStateAndReturnsWarning()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var sessionSaver = new Mock<ISessionSaver>();
        sessionSaver.Setup(saver => saver.SaveConfig()).Throws(new InvalidOperationException("config save failed"));
        var log = new Mock<ILoggingService>();
        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            loggingService: log.Object,
            sessionSaver: sessionSaver.Object);

        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry
        {
            Path = @"C:\RetryAdd",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        });
        pending.Traverse.AddTraverse(new GrantedPathEntry
        {
            Path = @"C:\RetryTraverse",
            IsTraverseOnly = true
        });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var outcome = await RunApplyOutcomeAsync(orchestrator);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Errors);
        var warning = Assert.Single(outcome.Warnings);
        Assert.Equal(GrantApplyFailureStep.PostGrantMutationSave, warning.Step);
        Assert.Equal("config save failed", warning.Cause.Message);
        Assert.False(pending.Grants.IsPendingAdd(@"C:\RetryAdd", false));
        Assert.False(pending.Traverse.IsPendingTraverseAdd(@"C:\RetryTraverse"));
        Assert.False(pending.HasPendingChanges);
        sessionSaver.Verify(saver => saver.SaveConfig(), Times.Once);
        log.Verify(logger => logger.Error(
            "ACL Manager apply succeeded in memory but failed to save config",
            It.Is<InvalidOperationException>(ex => ex.Message == "config save failed")), Times.Once);
    }

    [Fact]
    public async Task Apply_PartialFailure_KeepsOnlyFailedEntriesPending()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fail", false))
            .Throws(CreateGrantOperationException(GrantApplyFailureStep.GrantAclRemove, @"C:\Fail", "Simulated failure"));
        var orchestrator = CreateOrchestrator(grantMutatorService.Object, traverseService.Object);

        var pending = new AclManagerPendingChanges();
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false });
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.False(result);
        Assert.True(pending.Grants.IsPendingRemove(@"C:\Fail", false));
        Assert.False(pending.Grants.IsPendingRemove(@"C:\Ok", false));
    }

    [Fact]
    public async Task Apply_TraverseAndUntrackPhases_CallPersistedOperations()
    {
        var (orchestrator, grantMutatorService, traverseService) = CreateOrchestrator();
        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrantFix(new GrantedPathEntry { Path = @"C:\GrantFix", IsDeny = false });
        pending.Traverse.AddTraverse(new GrantedPathEntry { Path = @"C:\TraverseAdd", IsTraverseOnly = true });
        pending.Traverse.MarkTraverseForRemoval(new GrantedPathEntry { Path = @"C:\TraverseRemove", IsTraverseOnly = true });
        pending.Traverse.AddTraverseFix(new GrantedPathEntry { Path = @"C:\TraverseFix", IsTraverseOnly = true });
        pending.Grants.UntrackGrant(new GrantedPathEntry { Path = @"C:\GrantUntrack", IsDeny = false });
        pending.Traverse.UntrackTraverse(new GrantedPathEntry { Path = @"C:\TraverseUntrack", IsTraverseOnly = true });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        grantMutatorService.Verify(p => p.FixGrantAcl(TestSid, @"C:\GrantFix", false), Times.Once);
        traverseService.Verify(p => p.AddTraverse(TestSid, @"C:\TraverseAdd", null), Times.Once);
        traverseService.Verify(p => p.RemoveTraverse(TestSid, @"C:\TraverseRemove"), Times.Once);
        traverseService.Verify(p => p.FixTraverseAcl(TestSid, @"C:\TraverseFix"), Times.Once);
        grantMutatorService.Verify(p => p.UntrackGrant(TestSid, @"C:\GrantUntrack", false), Times.Once);
        traverseService.Verify(p => p.UntrackTraverse(TestSid, @"C:\TraverseUntrack"), Times.Once);
    }

    [Fact]
    public async Task Apply_ConfigMoveOnly_MovesGrantIntentMembershipAndSavesAffectedStores()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var mainStore = new TestGrantIntentStore();
        var dbEntry = new GrantedPathEntry { Path = @"C:\ConfigMove", IsDeny = false };
        mainStore.AddEntry(TestSid, dbEntry);
        var grantIntentStoreProvider = new TestGrantIntentStoreProvider(mainStore);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        grantIntentStoreProvider.AddLoadedStore(additionalStore);

        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            grantIntentStoreProvider: grantIntentStoreProvider);

        var pending = new AclManagerPendingChanges();
        pending.Grants.MoveGrantConfig(dbEntry, additionalStore.ConfigPath);
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(TestSid));
        Assert.Single(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public async Task Apply_ContainerSharedTraverseConfigMove_MovesSharedTraverseGrantIntent()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var sharedEntry = new GrantedPathEntry { Path = @"C:\SharedTraverse", IsTraverseOnly = true };
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, sharedEntry);
        var grantIntentStoreProvider = new TestGrantIntentStoreProvider(mainStore);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        grantIntentStoreProvider.AddLoadedStore(additionalStore);

        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            grantIntentStoreProvider: grantIntentStoreProvider);

        var pending = new AclManagerPendingChanges();
        pending.Traverse.MoveTraverseConfig(sharedEntry, additionalStore.ConfigPath);
        orchestrator.Initialize(pending, ContainerSid, isContainer: true);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid));
        Assert.Single(additionalStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid));
    }

    [Fact]
    public async Task Apply_AddGrantAllowPhase_PinsToQuickAccess()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();

        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            quickAccessPinService: quickAccessPinService.Object);

        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry
        {
            Path = @"C:\PinFolder",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.True(result);
        quickAccessPinService.Verify(
            q => q.PinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Contains(@"C:\PinFolder"))),
            Times.Once);
    }

    [Fact]
    public async Task Apply_PartialFailure_QuickAccessUpdatesOnlySuccessfulEntries()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.AddGrant(TestSid, @"C:\PinFail", false, It.IsAny<SavedRightsState?>(), null, null))
            .Throws(CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, @"C:\PinFail", "add failed"));
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\UnpinFail", false))
            .Throws(CreateGrantOperationException(GrantApplyFailureStep.GrantAclRemove, @"C:\UnpinFail", "remove failed"));

        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            quickAccessPinService: quickAccessPinService.Object);

        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\PinFail", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) });
        pending.Grants.AddGrant(new GrantedPathEntry { Path = @"C:\PinOk", IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) });
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\UnpinFail", IsDeny = false });
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\UnpinOk", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyAsync(orchestrator);

        Assert.False(result);
        quickAccessPinService.Verify(
            q => q.PinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l.Contains(@"C:\PinOk"))),
            Times.Once);
        quickAccessPinService.Verify(
            q => q.UnpinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l.Contains(@"C:\UnpinOk"))),
            Times.Once);
        Assert.True(pending.Grants.IsPendingAdd(@"C:\PinFail", false));
        Assert.False(pending.Grants.IsPendingAdd(@"C:\PinOk", false));
        Assert.True(pending.Grants.IsPendingRemove(@"C:\UnpinFail", false));
        Assert.False(pending.Grants.IsPendingRemove(@"C:\UnpinOk", false));
    }

    [Fact]
    public async Task Apply_Canceled_RetainsPendingInput()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.AddGrant(
                TestSid,
                @"C:\Cancel",
                true,
                It.IsAny<SavedRightsState?>(),
                It.IsAny<Func<bool>>(),
                null))
            .Throws(new OperationCanceledException("user canceled"));

        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var orchestrator = CreateOrchestrator(
            grantMutatorService.Object,
            traverseService.Object,
            quickAccessPinService: quickAccessPinService.Object);

        var pending = new AclManagerPendingChanges();
        pending.Grants.AddGrant(new GrantedPathEntry
        {
            Path = @"C:\Cancel",
            IsDeny = true,
            SavedRights = SavedRightsState.DefaultForMode(true)
        });
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        var result = await RunApplyOutcomeAsync(orchestrator);

        Assert.False(result.Succeeded);
        Assert.True(pending.Grants.IsPendingAdd(@"C:\Cancel", true));
        quickAccessPinService.Verify(
            q => q.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Apply_UnexpectedExecutorFailure_ReturnsStructuredFailureAndRefreshesGridsExactlyOnce()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Completed", false))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: true));
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Boom", false))
            .Throws(new InvalidOperationException("boom"));
        var orchestrator = CreateOrchestrator(grantMutatorService.Object, traverseService.Object);
        var pending = new AclManagerPendingChanges();
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\Completed", IsDeny = false });
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\Boom", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);
        int refreshCount = 0;

        var outcome = await orchestrator.ApplyAsync(
            new Progress<(int current, int total)>(),
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () =>
            {
                refreshCount++;
                Assert.False(pending.Grants.IsPendingRemove(@"C:\Completed", false));
                Assert.True(pending.Grants.IsPendingRemove(@"C:\Boom", false));
            });

        Assert.False(outcome.Succeeded);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal(GrantApplyFailureStep.GrantAclRemove, error.Step);
        Assert.Equal(@"C:\Boom", error.Path);
        Assert.Equal("boom", error.Cause.Message);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task Apply_StructuredFailure_RefreshesGridsExactlyOnce()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\StructuredFail", false))
            .Throws(CreateGrantOperationException(GrantApplyFailureStep.GrantAclRemove, @"C:\StructuredFail", "structured failure"));
        var orchestrator = CreateOrchestrator(grantMutatorService.Object, traverseService.Object);
        var pending = new AclManagerPendingChanges();
        pending.Grants.MarkGrantForRemoval(new GrantedPathEntry { Path = @"C:\StructuredFail", IsDeny = false });
        orchestrator.Initialize(pending, TestSid, isContainer: false);
        int refreshCount = 0;

        var outcome = await orchestrator.ApplyAsync(
            new Progress<(int current, int total)>(),
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () => refreshCount++);

        Assert.False(outcome.Succeeded);
        Assert.Single(outcome.Errors);
        Assert.Equal(1, refreshCount);
    }

    private static AclManagerApplyOrchestrator CreateOrchestrator(
        IGrantMutatorService grantMutatorService,
        ITraverseService traverseService,
        IQuickAccessPinService? quickAccessPinService = null,
        ILoggingService? loggingService = null,
        TestGrantIntentStoreProvider? grantIntentStoreProvider = null,
        ISessionSaver? sessionSaver = null)
    {
        var log = loggingService ?? new Mock<ILoggingService>().Object;
        var resolvedStores = grantIntentStoreProvider ?? new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var planBuilder = new AclApplyPlanBuilder();
        var executor = new AclApplyExecutor(
            PhaseCatalog,
            new AclApplyPhaseExecutor(
                log,
                grantMutatorService,
                traverseService,
                new AclApplySelectedStoreResolver(resolvedStores)));
        var postProcessor = new AclApplyPostProcessor(
            log,
            new GrantIntentRepository(resolvedStores),
            resolvedStores,
            quickAccessPinService ?? new Mock<IQuickAccessPinService>().Object,
            new TraverseGrantOwnerResolver(),
            PhaseCatalog,
            new AclApplyPostProcessingPolicy(PhaseCatalog));
        return new AclManagerApplyOrchestrator(
            planBuilder,
            executor,
            postProcessor,
            PhaseCatalog,
            sessionSaver ?? new Mock<ISessionSaver>().Object,
            log);
    }

    private static AclManagerApplyOrchestrator CreateOrchestrator(
        IGrantMutatorService grantMutatorService,
        ITraverseService traverseService)
        => CreateOrchestrator(grantMutatorService, traverseService, null, null, null);

    private static (AclManagerApplyOrchestrator Orchestrator, Mock<IGrantMutatorService> GrantMutatorService, Mock<ITraverseService> TraverseService)
        CreateOrchestrator()
    {
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        return (CreateOrchestrator(grantMutatorService.Object, traverseService.Object), grantMutatorService, traverseService);
    }

    private static async Task<bool> RunApplyAsync(AclManagerApplyOrchestrator orchestrator)
    {
        var outcome = await orchestrator.ApplyAsync(
            new Progress<(int current, int total)>(),
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () => { });
        return outcome.Succeeded;
    }

    private static Task<AclApplyOutcome> RunApplyOutcomeAsync(AclManagerApplyOrchestrator orchestrator)
        => orchestrator.ApplyAsync(
            new Progress<(int current, int total)>(),
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () => { });

    private static GrantOperationException CreateGrantOperationException(
        GrantApplyFailureStep step,
        string path,
        string message)
        => new(step, path, configPath: null, new InvalidOperationException(message));
}
