using Moq;
using RunFence.Apps;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Tests.Helpers;
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
        mock.Setup(r => r.GetHandledAssociations(It.IsAny<string>())).Returns([]);
        mock.Setup(r => r.GetNonDefaultArguments(It.IsAny<string>(), It.IsAny<string>())).Returns((string?)null);
        return mock;
    }

    private static Mock<IInteractiveUserAssociationReader> MakeInteractiveMock()
    {
        var mock = new Mock<IInteractiveUserAssociationReader>();
        mock.Setup(r => r.GetAssociationHandler(It.IsAny<string>())).Returns((DirectHandlerEntry?)null);
        return mock;
    }

    // --- HandlerMappingAppModePresenter ---

    [Fact]
    public void AppPresenter_PopulateApps_SelectsFirstApp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var appCombo = new ComboBox();
            var keyCombo = new ComboBox();
            var templateBox = new TextBox();
            using var section = new CombinedPrefixesSection();

            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, appCombo, keyCombo, templateBox, section);

            var apps = new[] { MakeApp("a1", "Alpha"), MakeApp("a2", "Beta") };
            presenter.PopulateApps(apps);

            // Alphabetical: Alpha selected first
            Assert.NotNull(presenter.SelectedApp);
            Assert.Equal("a1", presenter.SelectedApp!.Id);
        });
    }

    [Fact]
    public void AppPresenter_PopulateAppsForEdit_SelectsCurrentApp()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var appCombo = new ComboBox();
            var keyCombo = new ComboBox();
            var templateBox = new TextBox();
            using var section = new CombinedPrefixesSection();

            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, appCombo, keyCombo, templateBox, section);

            var appA = MakeApp("a1", "Alpha");
            var appB = MakeApp("a2", "Beta");

            presenter.PopulateAppsForEdit([appA, appB], currentApp: appB);

            Assert.NotNull(presenter.SelectedApp);
            Assert.Equal("a2", presenter.SelectedApp!.Id);
        });
    }

    [Fact]
    public void AppPresenter_PopulateAppsForEdit_UnknownCurrentApp_SelectsFirst()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var appCombo = new ComboBox();
            var keyCombo = new ComboBox();
            var templateBox = new TextBox();
            using var section = new CombinedPrefixesSection();

            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, appCombo, keyCombo, templateBox, section);

            var appA = MakeApp("a1", "Alpha");
            var appB = MakeApp("a2", "Beta");

            presenter.PopulateAppsForEdit([appA, appB], currentApp: MakeApp("unknown", "Unknown"));

            // Falls back to first alphabetical app
            Assert.NotNull(presenter.SelectedApp);
            Assert.Equal("a1", presenter.SelectedApp!.Id);
        });
    }

    [Fact]
    public void AppPresenter_NormalizedTemplate_ReturnsNullForBlank()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var templateBox = new TextBox { Text = "   " };
            using var section = new CombinedPrefixesSection();

            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, new ComboBox(), new ComboBox(), templateBox, section);

            Assert.Null(presenter.NormalizedTemplate);
        });
    }

    [Fact]
    public void AppPresenter_NormalizedTemplate_TrimsNonBlankValue()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var templateBox = new TextBox { Text = "  \"%1\"  " };
            using var section = new CombinedPrefixesSection();

            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, new ComboBox(), new ComboBox(), templateBox, section);

            Assert.Equal("\"%1\"", presenter.NormalizedTemplate);
        });
    }

    [Fact]
    public void AppPresenter_LoadPrefixes_AndCollect_RoundTrip()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            using var section = new CombinedPrefixesSection();
            var presenter = new HandlerMappingAppModePresenter(
                reader.Object, new ComboBox(), new ComboBox(), new TextBox(), section);

            var appPrefixes = new[] { @"C:\Apps\" };
            var assocPrefixes = new[] { @"C:\Work\" };

            presenter.LoadPrefixes(appPrefixes, assocPrefixes, replacePrefixes: true);

            Assert.Equal(appPrefixes, presenter.AppPrefixes);
            Assert.Equal(assocPrefixes, presenter.AssociationPrefixes);
            Assert.True(presenter.IsReplace);
        });
    }

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
            var result = section.GetAppPrefixes();
            Assert.NotNull(result);
            Assert.Contains(@"C:\Alpha\", result);
        });
    }

    // --- HandlerMappingDirectModePresenter ---

    [Fact]
    public void DirectPresenter_HandlerValue_ReturnsNull_WhenTextBlank()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            var handlerBox = new TextBox { Text = "   " };

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, new ComboBox(), handlerBox);

            Assert.Null(presenter.HandlerValue);
        });
    }

    [Fact]
    public void DirectPresenter_HandlerValue_ReturnsTrimmedText()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mock = MakeInteractiveMock();
            var handlerBox = new TextBox { Text = "  Acrobat.Document.DC  " };

            var presenter = new HandlerMappingDirectModePresenter(mock.Object, new ComboBox(), handlerBox);

            Assert.Equal("Acrobat.Document.DC", presenter.HandlerValue);
        });
    }

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

    // --- Dialog result property collection ---

    [Fact]
    public void Initialize_ResultPropertiesAreDefaultBeforeOkAccepted()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.Initialize([MakeApp("a1", "MyApp")]);

            // Result properties are all null/default until OK is accepted
            Assert.False(dlg.IsDirectMode);
            Assert.Empty(dlg.ResolvedKeys);
            Assert.Null(dlg.SelectedApp);
            Assert.Null(dlg.ArgumentsTemplate);
            Assert.Null(dlg.AppPrefixes);
            Assert.Null(dlg.PathPrefixes);
            Assert.False(dlg.ReplacePrefixes);
            Assert.Null(dlg.DirectHandlerValue);

            dlg.Dispose();
        });
    }

    [Fact]
    public void InitializeForEditApp_SetsEditTitleWithKeyAndPreservesLayout()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            var app = MakeApp("a1", "MyApp");
            dlg.InitializeForEditApp(".pdf", [app], currentApp: app,
                currentTemplate: "\"%1\"",
                currentAppPrefixes: [@"C:\Apps\"],
                currentAssocPrefixes: [@"C:\Work\"],
                currentReplacePrefixes: false);

            // Edit title contains the key; form height set to AppEditHeight (app edit layout)
            Assert.Contains(".pdf", dlg.Text);
            Assert.Equal(AppEditHeight, dlg.ClientSize.Height);

            dlg.Dispose();
        });
    }

    [Fact]
    public void InitializeForEditDirect_SetsDirectEditTitleAndLayout()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.InitializeForEditDirect(".pdf", currentValue: "Acrobat.Document.DC");

            // Edit title contains key; form height set to DirectEditHeight (direct edit layout)
            Assert.Contains(".pdf", dlg.Text);
            Assert.Equal(DirectEditHeight, dlg.ClientSize.Height);
            // DirectHandlerValue is null until OK accepted
            Assert.Null(dlg.DirectHandlerValue);

            dlg.Dispose();
        });
    }

    // --- HandlerMappingLayoutController ---

    [Fact]
    public void Initialize_AddAppMode_DefaultClientHeightIs580()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.Initialize([MakeApp("a1", "MyApp")]);

            // App mode add layout: full height AppMappingHeight
            Assert.Equal(AppMappingHeight, dlg.ClientSize.Height);
            dlg.Dispose();
        });
    }

    [Fact]
    public void LayoutController_ApplyEditLayout_AppMode_AdjustsFormHeightTo500()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.InitializeForEditApp(".pdf", [MakeApp("a1", "MyApp")], null, null);

            Assert.Equal(AppEditHeight, dlg.ClientSize.Height);
            dlg.Dispose();
        });
    }

    [Fact]
    public void LayoutController_ApplyEditLayout_DirectMode_AdjustsFormHeightTo160()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.InitializeForEditDirect(".pdf", "SomeHandler");

            Assert.Equal(DirectEditHeight, dlg.ClientSize.Height);
            dlg.Dispose();
        });
    }

    // --- Edit-direct and edit-app accept flows ---

    [Fact]
    public void EditDirect_OnOkClose_UnchangedValue_DirectHandlerValueIsNull()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            dlg.InitializeForEditDirect(".pdf", currentValue: "Acrobat.Document.DC");

            // Force handle creation so that Close() fires FormClosing.
            _ = dlg.Handle;

            // Close with OK — handler text equals the original value
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();

            // Unchanged value → DirectHandlerValue is null (no change)
            Assert.Null(dlg.DirectHandlerValue);
            dlg.Dispose();
        });
    }

    [Fact]
    public void EditApp_OnOkClose_CollectsSelectedAppTemplateAndPrefixes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var reader = MakeReaderMock();
            var interactive = MakeInteractiveMock();
            var dlg = new HandlerMappingAddDialog(reader.Object, interactive.Object);

            var app = MakeApp("a1", "MyApp");
            dlg.InitializeForEditApp(".pdf", [app], currentApp: app,
                currentTemplate: "\"%1\"",
                currentAppPrefixes: [@"C:\Apps\"],
                currentAssocPrefixes: [@"C:\Work\"],
                currentReplacePrefixes: false);

            // Force handle creation so that Close() fires FormClosing.
            _ = dlg.Handle;

            // Accept
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();

            // Edit app mode: SelectedApp, ArgumentsTemplate, AppPrefixes, PathPrefixes, ReplacePrefixes
            Assert.NotNull(dlg.SelectedApp);
            Assert.Equal("a1", dlg.SelectedApp!.Id);
            Assert.Equal("\"%1\"", dlg.ArgumentsTemplate);
            Assert.NotNull(dlg.AppPrefixes);
            Assert.Contains(@"C:\Apps\", dlg.AppPrefixes!);
            Assert.NotNull(dlg.PathPrefixes);
            Assert.Contains(@"C:\Work\", dlg.PathPrefixes!);
            Assert.False(dlg.ReplacePrefixes);

            dlg.Dispose();
        });
    }
}
