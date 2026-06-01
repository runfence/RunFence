using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingsDialogTests
{
    private const string AppId = "app01";

    [Fact]
    public void AddClick_UnresolvedFailureRefreshesOnlyAfterChildDialogCloses()
    {
        StaTestHelper.RunOnSta(() =>
        {
            TestContext? context = null;
            var refreshCountBeforeClose = 0;
            var childDialog = new FakeHandlerMappingAddDialog
            {
                DialogResultToReturn = DialogResult.Cancel,
                HasUnresolvedSubmitFailure = true
            };

            context = CreateContext(createAddDialog: () =>
            {
                childDialog.OnShowDialog = () =>
                {
                    refreshCountBeforeClose = CountGridRefreshCalls(context!.HandlerMappingService);
                };
                return childDialog;
            });

            StaTestHelper.CreateControlTree(context.Dialog);
            FindToolStripButton(context.Dialog, "Add association...").PerformClick();

            Assert.Same(context.Persistence, childDialog.Persistence);
            Assert.Equal(refreshCountBeforeClose + 1, CountGridRefreshCalls(context.HandlerMappingService));
        });
    }

    [Fact]
    public void AddClick_CancelWithoutUnresolvedFailureDoesNotRefreshParent()
    {
        StaTestHelper.RunOnSta(() =>
        {
            TestContext? context = null;
            var refreshCountBeforeClose = 0;
            var childDialog = new FakeHandlerMappingAddDialog
            {
                DialogResultToReturn = DialogResult.Cancel,
                HasUnresolvedSubmitFailure = false
            };

            context = CreateContext(createAddDialog: () =>
            {
                childDialog.OnShowDialog = () =>
                {
                    refreshCountBeforeClose = CountGridRefreshCalls(context!.HandlerMappingService);
                };
                return childDialog;
            });

            StaTestHelper.CreateControlTree(context.Dialog);
            FindToolStripButton(context.Dialog, "Add association...").PerformClick();

            Assert.Same(context.Persistence, childDialog.Persistence);
            Assert.Equal(refreshCountBeforeClose, CountGridRefreshCalls(context.HandlerMappingService));
        });
    }

    [Fact]
    public void ImportClick_PassesFilteredExistingKeysToChildDialog()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var importDialog = new FakeImportAssociationsDialog();
            var saveCalls = 0;
            var context = CreateContext(
                createImportDialog: () => importDialog,
                seed: (database, app) =>
                {
                    database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                    {
                        [".txt"] = new HandlerMappingEntry(app.Id, null)
                    };
                },
                saveDatabase: () => saveCalls++);
            context.InteractiveReader.Setup(reader => reader.GetInteractiveUserAssociations()).Returns(
            [
                new InteractiveAssociationEntry(".txt", new DirectHandlerEntry { ClassName = "txtfile" }, "Text"),
                new InteractiveAssociationEntry(".pdf", new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" }, "Pdf")
            ]);

            StaTestHelper.CreateControlTree(context.Dialog);
            FindToolStripButton(context.Dialog, "Import associations from interactive user...").PerformClick();

            Assert.Equal([".txt", ".pdf"], importDialog.InitializedEntries.Select(entry => entry.Key).ToArray());
            Assert.Equal([".txt"], importDialog.InitializedExistingKeys.OrderBy(key => key).ToArray());
            Assert.Same(context.Persistence, importDialog.Persistence);
            importDialog.Persistence!.SaveDatabase();
            Assert.Equal(1, saveCalls);
        });
    }

    [Fact]
    public void EditClick_AppMapping_PassesEditContextToChildDialog()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var childDialog = new FakeHandlerMappingAddDialog();
            var context = CreateContext(
                createAddDialog: () => childDialog,
                seed: (database, app) =>
                {
                    app.PathPrefixes = [@"C:\apps", @"D:\shared"];
                    database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                    {
                        [".txt"] = new HandlerMappingEntry(
                            app.Id,
                            "--open \"%1\"",
                            [@"C:\assoc", @"D:\assoc"],
                            true)
                    };
                });

            StaTestHelper.CreateControlTree(context.Dialog);
            SelectSingleGridRow(context.Dialog, 0);
            FindToolStripButton(context.Dialog, "Edit selected association").PerformClick();

            Assert.Equal(".txt", childDialog.EditKey);
            Assert.Equal(context.App.Id, childDialog.CurrentAppId);
            Assert.Equal("--open \"%1\"", childDialog.CurrentTemplate);
            Assert.Equal(new[] { @"C:\apps", @"D:\shared" }, childDialog.CurrentAppPrefixes);
            Assert.Equal(new[] { @"C:\assoc", @"D:\assoc" }, childDialog.CurrentAssociationPrefixes);
            Assert.True(childDialog.CurrentReplacePrefixes);
            Assert.Same(context.Persistence, childDialog.Persistence);
        });
    }

    [Fact]
    public void EditClick_DirectMapping_PassesDirectEditContextToChildDialog()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var childDialog = new FakeHandlerMappingAddDialog();
            var directEntry = new DirectHandlerEntry { Command = @"""C:\Tools\handler.exe"" ""%1""" };
            var context = CreateContext(
                createAddDialog: () => childDialog,
                seed: (database, _) =>
                {
                    database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
                    {
                        [".txt"] = directEntry
                    };
                });

            StaTestHelper.CreateControlTree(context.Dialog);
            SelectSingleGridRow(context.Dialog, 0);
            FindToolStripButton(context.Dialog, "Edit selected association").PerformClick();

            Assert.Equal(".txt", childDialog.DirectEditKey);
            Assert.Equal(@"""C:\Tools\handler.exe"" ""%1""", childDialog.CurrentDirectValue);
            Assert.Equal(directEntry, childDialog.CurrentDirectEntry);
            Assert.Same(context.Persistence, childDialog.Persistence);
        });
    }

    private sealed record TestContext(
        AppDatabase Database,
        AppEntry App,
        FakeHandlerMappingDialogPersistence Persistence,
        Mock<IHandlerMappingService> HandlerMappingService,
        Mock<IAppHandlerRegistrationService> RegistrationService,
        Mock<IMessageBoxService> MessageBoxService,
        Mock<IInteractiveUserAssociationReader> InteractiveReader,
        HandlerMappingsDialog Dialog);

    private static TestContext CreateContext(
        Func<IHandlerMappingAddDialog>? createAddDialog = null,
        Func<IImportAssociationsDialog>? createImportDialog = null,
        Action<AppDatabase, AppEntry>? seed = null,
        Action? saveDatabase = null)
    {
        var database = new AppDatabase();
        var app = new AppEntry
        {
            Id = AppId,
            Name = "Test App",
            AllowPassingArguments = false
        };
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
        var persistence = new FakeHandlerMappingDialogPersistence(database, saveDatabase);
        var syncService = new HandlerMappingSyncService(
            handlerMappingService.Object,
            registrationService.Object,
            autoSetService.Object,
            databaseProvider);

        var interactiveReader = new Mock<IInteractiveUserAssociationReader>();
        interactiveReader.Setup(reader => reader.GetAssociationHandler(It.IsAny<string>())).Returns((DirectHandlerEntry?)null);
        interactiveReader.Setup(reader => reader.GetInteractiveUserAssociations()).Returns([]);

        var messageBoxService = new Mock<IMessageBoxService>();
        messageBoxService.Setup(service => service.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()))
            .Returns(DialogResult.OK);
        messageBoxService.Setup(service => service.Show(It.IsAny<IWin32Window?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()))
            .Returns(DialogResult.OK);

        var exeReader = new Mock<IExeAssociationRegistryReader>();
        exeReader.Setup(reader => reader.GetHandledAssociations(It.IsAny<string>(), It.IsAny<string?>())).Returns([]);
        exeReader.Setup(reader => reader.GetNonDefaultArguments(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns((string?)null);
        exeReader.Setup(reader => reader.IsRegisteredProgId(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var dialogHelper = new HandlerMappingDialogHelper(exeReader.Object, handlerMappingService.Object);
        var gridBuilder = new HandlerMappingGridBuilder(
            handlerMappingService.Object,
            Mock.Of<ISidNameCacheService>(service => service.GetDisplayName(It.IsAny<string>()) == string.Empty));
        var mutationHandler = new HandlerMappingMutationHandler(handlerMappingService.Object);
        var addDialogFactory = createAddDialog ?? (() => new FakeHandlerMappingAddDialog());
        var importDialogFactory = createImportDialog ?? (() => new FakeImportAssociationsDialog());

        var childDialogCoordinator = new HandlerMappingsChildDialogCoordinator(
            addDialogFactory,
            importDialogFactory,
            dialogHelper,
            gridBuilder);

        var dialog = new HandlerMappingsDialog(
            mutationHandler,
            syncService,
            handlerMappingService.Object,
            interactiveReader.Object,
            new LoggingService(),
            messageBoxService.Object,
            Mock.Of<IShellHelper>(),
            gridBuilder,
            childDialogCoordinator);
        dialog.Initialize(persistence, "Interactive");

        return new TestContext(
            database,
            app,
            persistence,
            handlerMappingService,
            registrationService,
            messageBoxService,
            interactiveReader,
            dialog);
    }

    private static int CountGridRefreshCalls(Mock<IHandlerMappingService> handlerMappingService)
        => handlerMappingService.Invocations.Count(invocation =>
            invocation.Method.Name == nameof(IHandlerMappingService.GetAllHandlerMappings));

    private static ToolStripButton FindToolStripButton(Control root, string toolTipText)
    {
        return FindControls<ToolStrip>(root)
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => string.Equals(button.ToolTipText, toolTipText, StringComparison.Ordinal));
    }

    private static ToolStripButton FindToolStripButtonByText(Control root, string text)
    {
        return FindControls<ToolStrip>(root)
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => string.Equals(button.Text, text, StringComparison.Ordinal));
    }

    private static ToolStrip FindToolStrip(Control root) => FindControls<ToolStrip>(root).Single();

    private static DataGridView FindGrid(Control root) => FindControls<DataGridView>(root).Single();

    private static void SelectSingleGridRow(Control root, int rowIndex)
    {
        var grid = FindGrid(root);
        grid.ClearSelection();
        grid.CurrentCell = grid.Rows[rowIndex].Cells[0];
        grid.Rows[rowIndex].Selected = true;
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

    private sealed class FakeHandlerMappingAddDialog : IHandlerMappingAddDialog
    {
        public bool HasUnresolvedSubmitFailure { get; set; }
        public IntPtr Handle => IntPtr.Zero;
        public bool IsDirectMode { get; set; }
        public IReadOnlyList<string> ResolvedKeys { get; set; } = [];
        public AppEntry? SelectedApp { get; set; }
        public string? DirectHandlerValue { get; set; }
        public string? ArgumentsTemplate { get; set; }
        public IReadOnlyList<string>? AppPrefixes { get; set; }
        public IReadOnlyList<string>? PathPrefixes { get; set; }
        public bool ReplacePrefixes { get; set; }
        public IHandlerMappingDialogPersistence? Persistence { get; private set; }
        public string? EditKey { get; private set; }
        public string? CurrentAppId { get; private set; }
        public string? CurrentTemplate { get; private set; }
        public IReadOnlyList<string>? CurrentAppPrefixes { get; private set; }
        public IReadOnlyList<string>? CurrentAssociationPrefixes { get; private set; }
        public bool CurrentReplacePrefixes { get; private set; }
        public string? DirectEditKey { get; private set; }
        public string? CurrentDirectValue { get; private set; }
        public DirectHandlerEntry? CurrentDirectEntry { get; private set; }
        public DialogResult DialogResultToReturn { get; set; } = DialogResult.Cancel;
        public Action? OnShowDialog { get; set; }

        public void Initialize(IReadOnlyList<AppEntry> apps, IHandlerMappingDialogPersistence persistence)
        {
            Persistence = persistence;
        }

        public void InitializeForEditApp(
            string key,
            IReadOnlyList<AppEntry> apps,
            AppEntry? currentApp,
            string currentAppId,
            string? currentTemplate,
            IHandlerMappingDialogPersistence persistence,
            IReadOnlyList<string>? currentAppPrefixes = null,
            IReadOnlyList<string>? currentAssocPrefixes = null,
            bool currentReplacePrefixes = false)
        {
            Persistence = persistence;
            EditKey = key;
            CurrentAppId = currentAppId;
            CurrentTemplate = currentTemplate;
            CurrentAppPrefixes = currentAppPrefixes;
            CurrentAssociationPrefixes = currentAssocPrefixes;
            CurrentReplacePrefixes = currentReplacePrefixes;
        }

        public void InitializeForEditDirect(
            string key,
            string currentValue,
            DirectHandlerEntry currentEntry,
            IHandlerMappingDialogPersistence persistence)
        {
            Persistence = persistence;
            DirectEditKey = key;
            CurrentDirectValue = currentValue;
            CurrentDirectEntry = currentEntry;
        }

        public DialogResult ShowDialog(IWin32Window owner)
        {
            OnShowDialog?.Invoke();
            return DialogResultToReturn;
        }

        public void Dispose() { }
    }

    private sealed class FakeImportAssociationsDialog : IImportAssociationsDialog
    {
        public bool HasUnresolvedSubmitFailure { get; set; }
        public IntPtr Handle => IntPtr.Zero;
        public IReadOnlyList<InteractiveAssociationEntry> SelectedEntries { get; set; } = [];
        public IReadOnlyList<InteractiveAssociationEntry> InitializedEntries { get; private set; } = [];
        public IReadOnlySet<string> InitializedExistingKeys { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IHandlerMappingDialogPersistence? Persistence { get; private set; }
        public DialogResult DialogResultToReturn { get; set; } = DialogResult.Cancel;
        public Action? OnShowDialog { get; set; }

        public void Initialize(
            IReadOnlyList<InteractiveAssociationEntry> entries,
            IReadOnlySet<string> existingKeys,
            IHandlerMappingDialogPersistence persistence)
        {
            InitializedEntries = entries;
            InitializedExistingKeys = existingKeys;
            Persistence = persistence;
        }

        public DialogResult ShowDialog(IWin32Window owner)
        {
            OnShowDialog?.Invoke();
            return DialogResultToReturn;
        }

        public void Dispose() { }
    }
}
