using Moq;
using RunFence.Account.UI;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AccountCheckTimerServiceTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void OnTick_ReconciliationSuccess_ClearsGuardAndRestartsTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var service = CreateService(out var guard, out var runner, out var session, out var timerFactory);
            session.Database.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = @"C:\Apps", IsTraverseOnly = true });
            var reconciliationTcs = new TaskCompletionSource<GrantReconciliationService.ReconciliationResult>();
            var applied = false;

            runner.Setup(r => r.DetectGroupChanges())
                .Returns(Task.FromResult(new List<(string Sid, List<string> NewGroups)> { (TestSid, ["S-1-1-0"]) }));
            runner.Setup(r => r.ReconcileChangedSidsAsync(It.IsAny<List<(string Sid, List<string> NewGroups)>>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>>()))
                .Returns(reconciliationTcs.Task);
            runner.Setup(r => r.ApplyReconciliationResult(It.IsAny<GrantReconciliationService.ReconciliationResult>()))
                .Callback(() => applied = true);

            service.Start();
            var timer = Assert.Single(timerFactory.Timers);
            timer.Fire();
            StaTestHelper.PumpUntil(() => guard.IsInProgress);

            reconciliationTcs.SetResult(new GrantReconciliationService.ReconciliationResult([]));
            StaTestHelper.PumpUntil(() => applied && !guard.IsInProgress);
            Assert.Equal(2, timer.StartCallCount);
            Assert.Equal(1, timer.StopCallCount);
        });
    }

    [Fact]
    public void OnTick_ReconciliationThrows_ClearsGuardAndRestartsTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var service = CreateService(out var guard, out var runner, out _, out var timerFactory);
            runner.Setup(r => r.DetectGroupChanges())
                .Returns(Task.FromResult(new List<(string Sid, List<string> NewGroups)> { (TestSid, ["S-1-1-0"]) }));
            runner.Setup(r => r.ReconcileChangedSidsAsync(It.IsAny<List<(string Sid, List<string> NewGroups)>>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>>()))
                .Returns(Task.FromException<GrantReconciliationService.ReconciliationResult>(new InvalidOperationException("boom")));

            service.Start();
            var timer = Assert.Single(timerFactory.Timers);
            timer.Fire();
            StaTestHelper.PumpUntil(() => !guard.IsInProgress);
            Assert.Equal(2, timer.StartCallCount);
            Assert.Equal(1, timer.StopCallCount);
        });
    }

    [Fact]
    public void OnTick_ApplyThrows_ClearsGuardAndRestartsTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var service = CreateService(out var guard, out var runner, out _, out var timerFactory);
            runner.Setup(r => r.DetectGroupChanges())
                .Returns(Task.FromResult(new List<(string Sid, List<string> NewGroups)> { (TestSid, ["S-1-1-0"]) }));
            runner.Setup(r => r.ReconcileChangedSidsAsync(It.IsAny<List<(string Sid, List<string> NewGroups)>>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>>()))
                .ReturnsAsync(new GrantReconciliationService.ReconciliationResult([]));
            runner.Setup(r => r.ApplyReconciliationResult(It.IsAny<GrantReconciliationService.ReconciliationResult>()))
                .Throws(new InvalidOperationException("apply failed"));

            service.Start();
            var timer = Assert.Single(timerFactory.Timers);
            timer.Fire();
            StaTestHelper.PumpUntil(() => !guard.IsInProgress);
            Assert.Equal(2, timer.StartCallCount);
            Assert.Equal(1, timer.StopCallCount);
        });
    }

    [Fact]
    public void OnTick_WhenHidden_DoesNotRestartTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var service = CreateService(out var guard, out var runner, out _, out var timerFactory);
            var reconciliationTcs = new TaskCompletionSource<GrantReconciliationService.ReconciliationResult>();
            runner.Setup(r => r.DetectGroupChanges())
                .Returns(Task.FromResult(new List<(string Sid, List<string> NewGroups)> { (TestSid, ["S-1-1-0"]) }));
            runner.Setup(r => r.ReconcileChangedSidsAsync(It.IsAny<List<(string Sid, List<string> NewGroups)>>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>>()))
                .Returns(reconciliationTcs.Task);

            service.Start();
            var timer = Assert.Single(timerFactory.Timers);
            timer.Fire();
            StaTestHelper.PumpUntil(() => guard.IsInProgress);

            service.HandleVisibilityChanged(false);
            reconciliationTcs.SetResult(new GrantReconciliationService.ReconciliationResult([]));
            StaTestHelper.PumpUntil(() => !guard.IsInProgress);
            Assert.Equal(1, timer.StartCallCount);
        });
    }

    [Fact]
    public void OnTick_WhenDisposed_DoesNotRestartTimer()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var service = CreateService(out var guard, out var runner, out _, out var timerFactory);
            var reconciliationTcs = new TaskCompletionSource<GrantReconciliationService.ReconciliationResult>();
            runner.Setup(r => r.DetectGroupChanges())
                .Returns(Task.FromResult(new List<(string Sid, List<string> NewGroups)> { (TestSid, ["S-1-1-0"]) }));
            runner.Setup(r => r.ReconcileChangedSidsAsync(It.IsAny<List<(string Sid, List<string> NewGroups)>>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>>()))
                .Returns(reconciliationTcs.Task);

            service.Start();
            var timer = Assert.Single(timerFactory.Timers);
            timer.Fire();
            StaTestHelper.PumpUntil(() => guard.IsInProgress);

            service.Dispose();
            reconciliationTcs.SetResult(new GrantReconciliationService.ReconciliationResult([]));
            StaTestHelper.PumpUntil(() => !guard.IsInProgress);
            Assert.Equal(1, timer.StartCallCount);
        });
    }

    private static AccountCheckTimerService CreateService(
        out ReconciliationGuard guard,
        out Mock<IAccountGrantReconciliationRunner> runner,
        out SessionContext session,
        out ManualUiTimerFactory timerFactory)
    {
        guard = new ReconciliationGuard();
        runner = new Mock<IAccountGrantReconciliationRunner>();
        timerFactory = new ManualUiTimerFactory();
        session = new SessionContext { Database = new AppDatabase(), CredentialStore = new CredentialStore() };
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        return new AccountCheckTimerService(
            Mock.Of<ILocalUserProvider>(),
            sessionProvider.Object,
            Mock.Of<ILoggingService>(),
            guard,
            runner.Object,
            timerFactory);
    }
}
