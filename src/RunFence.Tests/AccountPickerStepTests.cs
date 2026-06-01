using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class AccountPickerStepTests
{
    private readonly Mock<ILocalGroupQueryService> _groupMembership = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly CredentialDisplayItemFactory _credentialDisplayItemFactory;
    private readonly CredentialFilterHelper _credentialFilterHelper;

    public AccountPickerStepTests()
    {
        _credentialDisplayItemFactory = new CredentialDisplayItemFactory(
            _sidResolver.Object, _profilePathResolver.Object);
        _credentialFilterHelper = new CredentialFilterHelper(_sidResolver.Object);
        _groupMembership.Setup(g => g.GetMembersOfGroup(It.IsAny<string>())).Returns([]);
        _localUserProvider.Setup(l => l.GetLocalUserAccounts()).Returns([]);
        _sidResolver.Setup(s => s.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
    }

    [Fact]
    public void Validate_WhenNoItemSelected_ReturnsError()
    {
        StaTestHelper.RunOnSta(() =>
        {
            bool setSelectionCalled = false;
            using var step = CreateStep((_, _) => setSelectionCalled = true);

            var error = step.Validate();

            Assert.NotNull(error);
            Assert.False(setSelectionCalled);
        });
    }

    [Fact]
    public void Validate_WhenCredentialDisplayItemWithNullSid_ReturnsError()
    {
        StaTestHelper.RunOnSta(() =>
        {
            bool setSelectionCalled = false;
            using var step = CreateStep((_, _) => setSelectionCalled = true);
            var listBox = FindListBox(step);

            var emptyCredential = new CredentialEntry { Id = Guid.NewGuid(), Sid = "" };
            var displayItem = _credentialDisplayItemFactory.Create(emptyCredential, sidNames: null, hasStoredCredential: false);
            listBox.Items.Add(displayItem);
            listBox.SelectedIndex = 0;

            var error = step.Validate();

            Assert.NotNull(error);
            Assert.Contains("valid", error, StringComparison.OrdinalIgnoreCase);
            Assert.False(setSelectionCalled);
        });
    }

    [Fact]
    public void Collect_WhenCredentialDisplayItemWithNullSid_DoesNotInvokeSetSelection()
    {
        StaTestHelper.RunOnSta(() =>
        {
            bool setSelectionCalled = false;
            using var step = CreateStep((_, _) => setSelectionCalled = true);
            var listBox = FindListBox(step);

            var emptyCredential = new CredentialEntry { Id = Guid.NewGuid(), Sid = "" };
            var displayItem = _credentialDisplayItemFactory.Create(emptyCredential, sidNames: null, hasStoredCredential: false);
            listBox.Items.Add(displayItem);
            listBox.SelectedIndex = 0;

            step.Collect();

            Assert.False(setSelectionCalled);
        });
    }

    [Fact]
    public void Collect_WhenCreateAccountItemSelected_InvokesSetSelectionWithNullSidAndIsCreateTrue()
    {
        StaTestHelper.RunOnSta(() =>
        {
            string? capturedSid = "unset";
            bool capturedIsCreate = false;
            using var step = CreateStep((sid, isCreate) =>
            {
                capturedSid = sid;
                capturedIsCreate = isCreate;
            });
            var listBox = FindListBox(step);

            listBox.Items.Add(new CreateAccountItem());
            listBox.SelectedIndex = 0;

            step.Collect();

            Assert.Null(capturedSid);
            Assert.True(capturedIsCreate);
        });
    }

    [Fact]
    public void Collect_WhenValidCredentialSelected_InvokesSetSelectionWithSid()
    {
        StaTestHelper.RunOnSta(() =>
        {
            const string sid = "S-1-5-21-100-200-300-1001";
            string? capturedSid = null;
            bool capturedIsCreate = true;
            using var step = CreateStep((s, c) =>
            {
                capturedSid = s;
                capturedIsCreate = c;
            });
            var listBox = FindListBox(step);

            var credential = new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };
            var displayItem = _credentialDisplayItemFactory.Create(credential, sidNames: null);
            listBox.Items.Add(displayItem);
            listBox.SelectedIndex = 0;

            step.Collect();

            Assert.Equal(sid, capturedSid);
            Assert.False(capturedIsCreate);
        });
    }

    [Fact]
    public void OnActivated_NewerActivationIgnoresStalePopulationResults()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var firstCallStarted = new ManualResetEventSlim(false);
            var firstCallGate = new ManualResetEventSlim(false);
            int callCount = 0;
            _groupMembership.Setup(g => g.GetMembersOfGroup("S-1-5-32-544"))
                .Returns(() =>
                {
                    var currentCall = Interlocked.Increment(ref callCount);
                    if (currentCall == 1)
                    {
                        firstCallStarted.Set();
                        firstCallGate.Wait();
                        return [new LocalUserAccount("stale", "S-1-5-21-stale")];
                    }

                    return [new LocalUserAccount("fresh", "S-1-5-21-fresh")];
                });
            _localUserProvider.Setup(l => l.GetLocalUserAccounts()).Returns(
            [
                new LocalUserAccount("stale", "S-1-5-21-stale"),
                new LocalUserAccount("fresh", "S-1-5-21-fresh")
            ]);

            string? collectedSid = null;
            using var step = CreateStep((sid, _) => collectedSid = sid);
            step.OnActivated();
            StaTestHelper.PumpUntil(
                () => firstCallStarted.IsSet,
                timeoutMessage: "Timed out waiting for the first AccountPickerStep population call to start.");
            step.OnActivated();
            firstCallGate.Set();

            StaTestHelper.PumpUntil(
                () => step.CanProceed && FindListBox(step).Items.OfType<CredentialDisplayItem>().Any(),
                timeoutMessage: "Timed out waiting for AccountPickerStep state change.");

            var listBox = FindListBox(step);
            var freshItem = listBox.Items.OfType<CredentialDisplayItem>()
                .Single(item => string.Equals(item.Credential.Sid, "S-1-5-21-fresh", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(listBox.Items.OfType<CredentialDisplayItem>(),
                item => string.Equals(item.Credential.Sid, "S-1-5-21-stale", StringComparison.OrdinalIgnoreCase));

            listBox.SelectedItem = freshItem;
            step.Collect();

            Assert.Equal("S-1-5-21-fresh", collectedSid);
        });
    }

    [Fact]
    public void OnActivated_DisposeDuringPopulation_DoesNotThrow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var gate = new ManualResetEventSlim(false);
            _groupMembership.Setup(g => g.GetMembersOfGroup(It.IsAny<string>()))
                .Callback(() => gate.Wait())
                .Returns([]);

            var step = CreateStep((_, _) => { });
            step.OnActivated();
            Application.DoEvents();

            step.Dispose();
            gate.Set();
            Application.DoEvents();
        });
    }

    private AccountPickerStep CreateStep(Action<string?, bool> setSelection)
    {
        var options = new AccountPickerStepOptions(
            Credentials: [],
            SidNames: new Dictionary<string, string>(),
            GroupSid: "S-1-5-32-544",
            StepTitle: "Pick account",
            InfoText: "Select an account.");

        return new AccountPickerStep(
            setSelection,
            _groupMembership.Object,
            _localUserProvider.Object,
            _credentialDisplayItemFactory,
            _credentialFilterHelper,
            options);
    }

    private static ListBox FindListBox(AccountPickerStep step)
        => step.Controls.OfType<ListBox>().First();
}
