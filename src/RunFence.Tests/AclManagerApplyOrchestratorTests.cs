using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
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

    private static (AclManagerApplyOrchestrator Orchestrator, Mock<IPathGrantService> PathGrantService)
        CreateOrchestrator()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var databaseProvider = new Mock<IDatabaseProvider>();
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new AclManagerApplyOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            new Mock<IGrantConfigTracker>().Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, new Mock<IQuickAccessPinService>().Object);

        return (orchestrator, pathGrantService);
    }

    private static async Task ApplyModificationAsync(
        AclManagerApplyOrchestrator orchestrator, PendingModification mod)
    {
        var pending = new AclManagerPendingChanges();
        pending.PendingModifications[(mod.Entry.Path, mod.WasIsDeny)] = mod;

        var owner = new Mock<IWin32Window>();
        owner.Setup(o => o.Handle).Returns(IntPtr.Zero);
        using var progressBar = new ToolStripProgressBar();

        orchestrator.Initialize(pending, TestSid, isContainer: false, owner.Object);

        await orchestrator.ApplyAsync(
            progressBar,
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () => { });
    }

    [Fact]
    public async Task AllowOwnToDeny_ShouldResetOwner()
    {
        var (orchestrator, pathGrantService) = CreateOrchestrator();

        var entry = new GrantedPathEntry
        {
            Path = @"C:\TestFolder",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };
        var mod = new PendingModification(
            Entry: entry, WasIsDeny: false, WasOwn: true, NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(isDeny: true, own: false));

        await ApplyModificationAsync(orchestrator, mod);

        pathGrantService.Verify(p => p.ResetOwner(@"C:\TestFolder", false), Times.Once);
    }

    [Fact]
    public async Task AllowOwnToAllowNoOwn_ShouldResetOwner()
    {
        var (orchestrator, pathGrantService) = CreateOrchestrator();

        var entry = new GrantedPathEntry
        {
            Path = @"C:\TestFolder2",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };
        var mod = new PendingModification(
            Entry: entry, WasIsDeny: false, WasOwn: true, NewIsDeny: false,
            NewRights: new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false));

        await ApplyModificationAsync(orchestrator, mod);

        pathGrantService.Verify(p => p.ResetOwner(@"C:\TestFolder2", false), Times.Once);
    }

    [Fact]
    public void AllowOwnToDeny_NewRightsOwnIsFalse()
    {
        var denyRights = SavedRightsState.DefaultForMode(isDeny: true, own: false);
        Assert.False(denyRights.Own);
    }
}
