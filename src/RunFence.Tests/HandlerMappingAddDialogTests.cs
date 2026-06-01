using Moq;
using System.Runtime.InteropServices;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;
using static RunFence.Apps.UI.Forms.HandlerMappingAddDialog;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="HandlerMappingAddDialog"/> add/edit flows via its extracted presenters
/// and the dialog's result collection logic.
/// All WinForms scenarios run on an STA thread.
/// </summary>
public class HandlerMappingAddDialogTests
{
    private static AppEntry MakeApp(string id, string name, string exePath = @"C:\App\app.exe") =>
        new() { Id = id, Name = name, ExePath = exePath };

    private static Mock<IExeAssociationRegistryReader> MakeReaderMock()
    {
        var mock = new Mock<IExeAssociationRegistryReader>();
        mock.Setup(r => r.GetHandledAssociations(It.IsAny<string>(), It.IsAny<string?>())).Returns([]);
        mock.Setup(r => r.GetNonDefaultArguments(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns((string?)null);
        mock.Setup(r => r.IsRegisteredProgId(".pdf", "Acrobat.Document.DC")).Returns(true);
        return mock;
    }

    private static Mock<IInteractiveUserAssociationReader> MakeInteractiveMock()
    {
        var mock = new Mock<IInteractiveUserAssociationReader>();
        mock.Setup(r => r.GetAssociationHandler(It.IsAny<string>())).Returns((DirectHandlerEntry?)null);
        return mock;
    }

    private static HandlerMappingDialogHelper MakeDialogHelper(Mock<IExeAssociationRegistryReader> reader)
    {
        var mappingService = new Mock<IHandlerMappingService>();
        return new HandlerMappingDialogHelper(reader.Object, mappingService.Object);
    }

    private sealed record DialogTestContext(
        AppDatabase Database,
        AppEntry App,
        Mock<IMessageBoxService> MessageBoxService,
        HandlerMappingDialogSubmissionCoordinator SubmissionCoordinator,
        FakeHandlerMappingDialogPersistence Persistence);

    private static DialogTestContext CreateDialogTestContext(
        Mock<IExeAssociationRegistryReader> reader,
        Action<AppDatabase, AppEntry>? seed = null,
        Action? saveDatabase = null)
    {
        var database = new AppDatabase();
        var app = MakeApp("a1", "MyApp");
        database.Apps.Add(app);
        seed?.Invoke(database, app);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(service => service.GetAllHandlerMappings(database))
            .Returns(() =>
            {
                var result = new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in database.Settings.HandlerMappings ?? [])
                    result[entry.Key] = [entry.Value];
                return result;
            });
        handlerMappingService.Setup(service => service.GetEffectiveHandlerMappings(database))
            .Returns(() => database.Settings.HandlerMappings != null
                ? new Dictionary<string, HandlerMappingEntry>(database.Settings.HandlerMappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));
        handlerMappingService.Setup(service => service.GetEffectiveDirectHandlerMappings(database))
            .Returns(() => database.Settings.DirectHandlerMappings != null
                ? new Dictionary<string, DirectHandlerEntry>(database.Settings.DirectHandlerMappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));
        handlerMappingService.Setup(service => service.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database))
            .Callback((string key, HandlerMappingEntry entry, AppDatabase _) =>
            {
                database.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
                database.Settings.HandlerMappings[key] = entry;
            });
        handlerMappingService.Setup(service => service.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database))
            .Callback((string key, string appId, AppDatabase _) =>
            {
                if (database.Settings.HandlerMappings == null)
                    return;

                if (database.Settings.HandlerMappings.TryGetValue(key, out var existing) &&
                    string.Equals(existing.AppId, appId, StringComparison.OrdinalIgnoreCase))
                {
                    database.Settings.HandlerMappings.Remove(key);
                    if (database.Settings.HandlerMappings.Count == 0)
                        database.Settings.HandlerMappings = null;
                }
            });
        handlerMappingService.Setup(service => service.SetDirectHandlerMapping(It.IsAny<string>(), It.IsAny<DirectHandlerEntry>(), database))
            .Callback((string key, DirectHandlerEntry entry, AppDatabase _) =>
            {
                database.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
                database.Settings.DirectHandlerMappings[key] = entry;
            });
        handlerMappingService.Setup(service => service.RemoveDirectHandlerMapping(It.IsAny<string>(), database))
            .Callback((string key, AppDatabase _) =>
            {
                if (database.Settings.DirectHandlerMappings == null)
                    return;

                database.Settings.DirectHandlerMappings.Remove(key);
                if (database.Settings.DirectHandlerMappings.Count == 0)
                    database.Settings.DirectHandlerMappings = null;
            });

        var registrationService = new Mock<IAppHandlerRegistrationService>();
        var autoSetService = new Mock<IAssociationAutoSetService>();
        autoSetService.Setup(service => service.AutoSetForAllUsers()).Returns(default(AssociationAutoSetResult)!);
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        var syncService = new HandlerMappingSyncService(
            handlerMappingService.Object,
            registrationService.Object,
            autoSetService.Object,
            databaseProvider);
        var submitTransaction = new HandlerMappingSubmitTransaction(
            handlerMappingService.Object,
            syncService);
        var mutationHandler = new HandlerMappingMutationHandler(handlerMappingService.Object);
        var messageBoxService = new Mock<IMessageBoxService>();
        messageBoxService.Setup(service => service.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()))
            .Returns(DialogResult.OK);
        var persistence = new FakeHandlerMappingDialogPersistence(database, saveDatabase);

        var submissionCoordinator = new HandlerMappingDialogSubmissionCoordinator(
            new HandlerMappingDialogHelper(reader.Object, handlerMappingService.Object),
            mutationHandler,
            submitTransaction,
            Mock.Of<ILoggingService>());

        return new DialogTestContext(
            database,
            app,
            messageBoxService,
            submissionCoordinator,
            persistence);
    }

    private static HandlerMappingAddDialog CreateDialog(
        Mock<IExeAssociationRegistryReader> reader,
        Mock<IInteractiveUserAssociationReader> interactive,
        DialogTestContext? context = null)
    {
        context ??= CreateDialogTestContext(reader);
        return new HandlerMappingAddDialog(
            reader.Object,
            interactive.Object,
            MakeDialogHelper(reader),
            context.SubmissionCoordinator,
            new HandlerMappingAddDialogSubmissionState(),
            context.MessageBoxService.Object);
    }

    // --- HandlerMappingAppModePresenter ---

    [Fact]
    public void AppPresenter_OnAppSelectionChanged_LoadsAppPrefixesIntoSection()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var appCombo = new ComboBox();
            using var section = new CombinedPrefixesSection();
            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, appCombo, new ComboBox(), new TextBox(), section);

            var appWithPrefixes = MakeApp("a1", "Alpha");
            appWithPrefixes.PathPrefixes = [@"C:\Alpha\"];
            presenter.PopulateApps([appWithPrefixes]);

            // Trigger selection-changed
            presenter.OnAppSelectionChanged();

            // App prefixes should now be loaded into the section
            var result = section.GetAppPrefixes() ?? [];
            Assert.Contains(@"C:\Alpha\", result);
        });
    }

    // --- HandlerMappingDirectModePresenter ---

    [Fact]
    public void DirectPresenter_TryAutoFillHandler_WithBlankKey_DoesNotCallReader()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            var keyCombo = new ComboBox { Text = "" };

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, keyCombo, new TextBox());
            presenter.TryAutoFillHandler();

            mock.Verify(r => r.GetAssociationHandler(It.IsAny<string>()), Times.Never);
        });
    }

    [Fact]
    public void DirectPresenter_TryAutoFillHandler_WithValidKey_FillsHandlerTextFromInteractiveReader()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            mock.Setup(r => r.GetAssociationHandler(".pdf"))
                .Returns(new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" });

            var keyCombo = new ComboBox { Text = ".pdf" };
            var handlerBox = new TextBox();

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, keyCombo, handlerBox);
            presenter.TryAutoFillHandler();

            Assert.Equal("Acrobat.Document.DC", handlerBox.Text);
        });
    }

    [Fact]
    public void DirectPresenter_TryAutoFillHandler_WhenNoInteractiveHandler_LeavesTextUnchanged()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            mock.Setup(r => r.GetAssociationHandler(".xyz")).Returns((DirectHandlerEntry?)null);

            var keyCombo = new ComboBox { Text = ".xyz" };
            var handlerBox = new TextBox { Text = "existing" };

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, keyCombo, handlerBox);
            presenter.TryAutoFillHandler();

            Assert.Equal("existing", handlerBox.Text);
        });
    }

    [Fact]
    public void DirectPresenter_TryAutoFillHandler_NoHandlerForNewKey_ClearsPreviousAutoFill()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            mock.Setup(r => r.GetAssociationHandler(".pdf"))
                .Returns(new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" });
            mock.Setup(r => r.GetAssociationHandler(".xyz"))
                .Returns((DirectHandlerEntry?)null);

            var keyCombo = new ComboBox { Text = ".pdf" };
            var handlerBox = new TextBox();

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, keyCombo, handlerBox);
            presenter.TryAutoFillHandler();

            keyCombo.Text = ".xyz";
            presenter.TryAutoFillHandler();

            Assert.Equal(string.Empty, handlerBox.Text);
        });
    }

    // --- Dialog result property collection ---

    // --- Edit-direct and edit-app accept flows ---

    [Fact]
    public void CaptureAcceptedValues_EditDirectWithUnchangedValue_LeavesDirectHandlerValueNull()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var capture = MakeDialogHelper(reader).CaptureAcceptedValues(new HandlerMappingAddCaptureRequest(
                IsEditMode: true,
                IsDirectEditMode: true,
                EditKey: ".pdf",
                OriginalDirectValue: "Acrobat.Document.DC",
                RawKey: null,
                IsDirectModeSelected: true,
                SelectedApp: null,
                DirectHandlerValue: "Acrobat.Document.DC",
                ArgumentsTemplate: null,
                AppPrefixes: null,
                AssociationPrefixes: null,
                ReplacePrefixes: false));

            Assert.Null(capture.ValidationError);
            Assert.Null(capture.DirectHandlerValue);
        });
    }

    [Fact]
    public void CaptureAcceptedValues_EditApp_CollectsSelectedAppTemplateAndPrefixes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var app = MakeApp("a1", "MyApp");
            app.AllowPassingArguments = true;
            var capture = MakeDialogHelper(reader).CaptureAcceptedValues(new HandlerMappingAddCaptureRequest(
                IsEditMode: true,
                IsDirectEditMode: false,
                EditKey: ".pdf",
                OriginalDirectValue: string.Empty,
                RawKey: null,
                IsDirectModeSelected: false,
                SelectedApp: app,
                DirectHandlerValue: null,
                ArgumentsTemplate: "\"%1\"",
                AppPrefixes: [@"C:\Apps\"],
                AssociationPrefixes: [@"C:\Work\"],
                ReplacePrefixes: false));

            Assert.Null(capture.ValidationError);
            Assert.NotNull(capture.SelectedApp);
            Assert.Equal("a1", capture.SelectedApp!.Id);
            Assert.Equal("\"%1\"", capture.ArgumentsTemplate);
            Assert.NotNull(capture.AppPrefixes);
            Assert.Contains(@"C:\Apps\", capture.AppPrefixes!);
            Assert.NotNull(capture.PathPrefixes);
            Assert.Contains(@"C:\Work\", capture.PathPrefixes!);
            Assert.False(capture.ReplacePrefixes);
        });
    }

    [Fact]
    public void ValidateKeys_InvalidKey_ReturnsOnlyInvalidEntry()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var validation = MakeDialogHelper(reader).ValidateKeys(["bad key"]);

            Assert.Empty(validation.Valid);
            Assert.Equal(["bad key"], validation.Invalid);
        });
    }

    [Fact]
    public void AddDialog_AppMappingWithArgumentForwardingDisabled_ReturnsValidationSuccessAndAutoEnableFlag()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var app = MakeApp("a1", "MyApp");
            app.AllowPassingArguments = false;
            var validation = MakeDialogHelper(reader).ValidateAppMapping(
                [".pdf"],
                app,
                "\"%1\"",
                appPrefixes: null,
                pathPrefixes: null,
                replacePrefixes: false);

            Assert.True(validation.IsValid);
            Assert.True(validation.RequiresAllowPassingArgumentsEnable);
            Assert.Null(validation.ErrorMessage);
        });
    }

    [Fact]
    public void AddDialog_AppMappingWithArgumentForwardingAlreadyEnabled_ReturnsNoAutoEnableFlag()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var app = MakeApp("a1", "MyApp");
            app.AllowPassingArguments = true;
            var validation = MakeDialogHelper(reader).ValidateAppMapping(
                [".pdf"],
                app,
                "\"%1\"",
                appPrefixes: null,
                pathPrefixes: null,
                replacePrefixes: false);

            Assert.True(validation.IsValid);
            Assert.False(validation.RequiresAllowPassingArgumentsEnable);
            Assert.Null(validation.ErrorMessage);
        });
    }

    [Fact]
    public void HandlerAssociationsSection_RightClickEmptySpace_ClearsPreviousTarget()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new Form();
            using var section = new HandlerAssociationsSection();
            host.ClientSize = new Size(800, 500);
            section.Dock = DockStyle.Fill;
            host.Controls.Add(section);
            StaTestHelper.CreateControlTree(host);
            Application.DoEvents();
            host.PerformLayout();
            section.SetAssociations([new HandlerAssociationItem(".txt", null)]);

            var grid = FindControls<DataGridView>(section).Single();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.Rows[0].Selected = true;

            SendRightClick(grid, new Point(grid.Width - 5, grid.Height - 5));
            Application.DoEvents();

            Assert.Null(grid.CurrentCell);
            Assert.Empty(grid.SelectedRows.Cast<DataGridViewRow>());
        });
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static void SendRightClick(Control control, Point clientPoint)
    {
        var lParam = clientPoint.X | (clientPoint.Y << 16);
        _ = SendMessage(control.Handle, 0x0204, IntPtr.Zero, (IntPtr)lParam);
        _ = SendMessage(control.Handle, 0x0205, IntPtr.Zero, (IntPtr)lParam);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
