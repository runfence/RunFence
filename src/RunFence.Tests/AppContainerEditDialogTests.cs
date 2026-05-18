using Autofac;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Startup;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerEditDialogTests
{
    [Fact]
    public void SessionScope_CanResolveAppContainerEditDialog()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
            using var pinKey = TestSecretFactory.Create(32);
            var session = new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(pinKey);

            using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
                foundationContainer,
                session,
                new StartupOptions(false, false));

            using var dialog = sessionScope.Resolve<AppContainerEditDialog>();
            dialog.Initialize(existing: null);

            Assert.NotNull(dialog);
        });
    }

    [Fact]
    public void OkButton_PendingAsyncCreate_DoesNotCloseUntilServiceCompletes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var createGate = new TaskCompletionSource<AppContainerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new FakeAppContainerEditService
            {
                CreateNewContainerAsync = (_, displayName, _, _, _, _) => createGate.Task
            };
            var notifier = new FakeNotifier();

            using var dialog = CreateCreateDialog(service, notifier, "Pending");
            ClickButton(dialog, "OK");
            StaTestHelper.PumpUntil(() => service.CreateCallCount == 1, timeoutMessage: "Timed out waiting for AppContainer create submission.");
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Assert.True(dialog.UseWaitCursor);

            createGate.SetResult(new AppContainerCreateResult(
                AppContainerOperationStatus.Succeeded,
                new AppContainerEntry { Name = "rfn_pending", DisplayName = "Pending" },
                null,
                []));

            StaTestHelper.PumpUntil(() => dialog.DialogResult == DialogResult.OK, timeoutMessage: "Timed out waiting for AppContainer dialog submit completion.");
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Equal(AppContainerOperationStatus.Succeeded, dialog.LastOperationStatus);
            Assert.NotNull(dialog.CreatedEntry);
        });
    }

    [Fact]
    public void OkButton_BlockingValidationFailure_ReenablesDialogAndKeepsItOpen()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var service = new FakeAppContainerEditService();
            var notifier = new FakeNotifier();
            using var dialog = CreateCreateDialog(service, notifier, string.Empty);

            ClickButton(dialog, "OK");
            StaTestHelper.PumpUntil(() => notifier.ValidationWarnings.Count == 1, timeoutMessage: "Timed out waiting for AppContainer validation warning.");

            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Assert.False(dialog.UseWaitCursor);
            Assert.Single(notifier.ValidationWarnings);
            Assert.Equal("Display name is required.", notifier.ValidationWarnings[0]);
            Assert.Equal(0, service.CreateCallCount);
            Assert.True(FindButton(dialog, "OK").Enabled);
        });
    }

    [Fact]
    public void DeleteButton_SetsDeleteRequestedAndClosesAsCancel()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var existing = new AppContainerEntry { Name = "rfn_existing", DisplayName = "Existing" };
            var service = new FakeAppContainerEditService();
            var notifier = new FakeNotifier();
            using var dialog = CreateEditDialog(service, notifier, existing);

            ClickButton(dialog, "Delete Container");

            Assert.True(dialog.DeleteRequested);
            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
        });
    }

    private static AppContainerEditDialog CreateCreateDialog(
        FakeAppContainerEditService service,
        FakeNotifier notifier,
        string displayName)
    {
        var dialog = CreateDialog(service, notifier);
        dialog.Initialize(existing: null);
        StaTestHelper.CreateControlTree(dialog);
        SetDisplayName(dialog, displayName);
        return dialog;
    }

    private static AppContainerEditDialog CreateEditDialog(
        FakeAppContainerEditService service,
        FakeNotifier notifier,
        AppContainerEntry existing)
    {
        var dialog = CreateDialog(service, notifier);
        dialog.Initialize(existing);
        StaTestHelper.CreateControlTree(dialog);
        return dialog;
    }

    private static AppContainerEditDialog CreateDialog(
        FakeAppContainerEditService service,
        FakeNotifier notifier)
    {
        return new AppContainerEditDialog(
            new AppContainerEditSubmitController(service),
            new AppContainerDialogStateAssembler(),
            new AppContainerCapabilitiesBinder(notifier),
            new AppContainerDialogResultPresenter(notifier));
    }

    private static void SetDisplayName(Control root, string displayName)
    {
        var layout = EnumerateControls(root).OfType<TableLayoutPanel>().Single();
        var displayNameBox = (TextBox)layout.GetControlFromPosition(1, 0)!;
        displayNameBox.Text = displayName;
    }

    private static Button FindButton(Control root, string text)
    {
        return EnumerateControls(root).OfType<Button>().Single(button => button.Text == text);
    }

    private static void ClickButton(Control root, string text)
    {
        StaTestHelper.ClickButton(FindButton(root, text));
    }

    private static IEnumerable<Control> EnumerateControls(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var grandchild in EnumerateControls(child))
                yield return grandchild;
        }
    }

    private sealed class FakeAppContainerEditService : IAppContainerEditService
    {
        public int CreateCallCount { get; private set; }

        public Func<AppContainerEntry, string, List<string>, bool, List<string>, bool, Task<AppContainerEditResult>>? ApplyEditChangesAsync { get; init; }

        public Func<string, string, bool, List<string>, bool, List<string>, Task<AppContainerCreateResult>>? CreateNewContainerAsync { get; init; }

        public Task<AppContainerEditResult> ApplyEditChanges(
            AppContainerEntry existing,
            string displayName,
            List<string> capabilities,
            bool loopback,
            List<string> newComClsids,
            bool isEphemeral)
        {
            if (ApplyEditChangesAsync != null)
                return ApplyEditChangesAsync(existing, displayName, capabilities, loopback, newComClsids, isEphemeral);

            return Task.FromResult(new AppContainerEditResult(
                AppContainerOperationStatus.Succeeded,
                existing,
                false,
                null,
                []));
        }

        public Task<AppContainerCreateResult> CreateNewContainer(
            string profileName,
            string displayName,
            bool isEphemeral,
            List<string> capabilities,
            bool loopback,
            List<string> comClsids)
        {
            CreateCallCount++;
            if (CreateNewContainerAsync != null)
                return CreateNewContainerAsync(profileName, displayName, isEphemeral, capabilities, loopback, comClsids);

            return Task.FromResult(new AppContainerCreateResult(
                AppContainerOperationStatus.Succeeded,
                new AppContainerEntry { Name = profileName, DisplayName = displayName },
                null,
                []));
        }
    }

    private sealed class FakeNotifier : IAppContainerEditDialogNotifier
    {
        public List<string> ValidationWarnings { get; } = [];

        public List<string> OperationErrors { get; } = [];

        public List<string> PersistenceWarnings { get; } = [];

        public List<IReadOnlyList<string>> ComAccessWarnings { get; } = [];

        public int RestartRequiredCount { get; private set; }

        public void ShowValidationWarning(IWin32Window owner, string message)
        {
            ValidationWarnings.Add(message);
        }

        public void ShowOperationError(IWin32Window owner, string message)
        {
            OperationErrors.Add(message);
        }

        public void ShowRestartRequired(IWin32Window owner)
        {
            RestartRequiredCount++;
        }

        public void ShowComAccessWarning(IWin32Window owner, IReadOnlyList<string> warnings)
        {
            ComAccessWarnings.Add(warnings);
        }

        public void ShowPersistenceWarning(IWin32Window owner, string message)
        {
            PersistenceWarnings.Add(message);
        }
    }
}
