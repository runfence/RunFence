using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class WizardAccountPickerStepFactoryTests
{
    [Fact]
    public void CreatePickerStep_ReturnsAccountPickerStepThatUsesCommitAction()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var progress = new Mock<IWizardProgressReporter>();
            bool commitCalled = false;
            var factory = CreateFactory();

            using var step = factory.CreatePickerStep(
                setSelection: (_, _) => { },
                options: new AccountPickerStepOptions(
                    Credentials: [],
                    SidNames: new Dictionary<string, string>(),
                    GroupSid: GroupFilterHelper.UsersSid,
                    StepTitle: "Accounts",
                    InfoText: "Select an account."),
                followingStepsFactory: null,
                commitAction: reporter =>
                {
                    Assert.Same(progress.Object, reporter);
                    commitCalled = true;
                    return Task.CompletedTask;
                });

            Assert.IsType<AccountPickerStep>(step);

            step.OnCommitBeforeNextAsync(progress.Object).GetAwaiter().GetResult();

            Assert.True(commitCalled);
        });
    }

    [Fact]
    public void TryResolveName_ReturnsResolverResult()
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryResolveName("S-1-5-21-1000")).Returns("User A");
        var factory = CreateFactory(sidResolver.Object);

        var result = factory.TryResolveName("S-1-5-21-1000");

        Assert.Equal("User A", result);
    }

    private static WizardAccountPickerStepFactory CreateFactory(ISidResolver? sidResolver = null) =>
        new(
            Mock.Of<ILocalGroupMembershipService>(),
            Mock.Of<ILocalUserProvider>(),
            sidResolver ?? Mock.Of<ISidResolver>(),
            new CredentialFilterHelper(Mock.Of<ISidResolver>()),
            new CredentialDisplayItemFactory(Mock.Of<ISidResolver>(), Mock.Of<IProfilePathResolver>()));
}
