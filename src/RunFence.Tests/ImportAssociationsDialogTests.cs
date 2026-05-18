using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class ImportAssociationsDialogTests
{
    [Fact]
    public void Initialize_FiltersEntriesWithExistingKeys()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            dialog.Initialize(
            [
                new InteractiveAssociationEntry(".pdf", new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" }, "Pdf"),
                new InteractiveAssociationEntry(".txt", new DirectHandlerEntry { ClassName = "txtfile" }, "Text")
            ],
            new HashSet<string>([".txt"], StringComparer.OrdinalIgnoreCase),
            new FakeHandlerMappingDialogPersistence(new AppDatabase()));

            var grid = FindControls<DataGridView>(dialog).Single();

            Assert.Equal(1, grid.Rows.Count);
            Assert.Equal(".pdf", grid.Rows[0].Cells[1].Value);
        });
    }

    [Fact]
    public void SelectedEntries_UncheckedRowIsIgnored()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var dialog = CreateDialog();
            dialog.Initialize(
            [
                new InteractiveAssociationEntry("bad key", new DirectHandlerEntry { Command = "\"C:\\App.exe\" \"%1\"" }, "Bad"),
                new InteractiveAssociationEntry(".pdf", new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" }, "Pdf")
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new FakeHandlerMappingDialogPersistence(new AppDatabase()));

            var grid = FindControls<DataGridView>(dialog).Single();
            grid.Rows[0].Cells[0].Value = false;

            Assert.Equal([".pdf"], dialog.SelectedEntries.Select(entry => entry.Key).ToList());
        });
    }

    private static ImportAssociationsDialog CreateDialog()
    {
        var database = new AppDatabase();
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        var handlerMappingService = new Mock<IHandlerMappingService>();
        var coordinator = new HandlerMappingDialogSubmissionCoordinator(
            new HandlerMappingDialogHelper(
                Mock.Of<IExeAssociationRegistryReader>(),
                handlerMappingService.Object),
            new HandlerMappingMutationHandler(
                handlerMappingService.Object),
            new HandlerMappingSubmitTransaction(
                handlerMappingService.Object,
                new HandlerMappingSyncService(
                    handlerMappingService.Object,
                    Mock.Of<IAppHandlerRegistrationService>(),
                    Mock.Of<IAssociationAutoSetService>(),
                    databaseProvider)),
            Mock.Of<RunFence.Core.ILoggingService>());

        return new ImportAssociationsDialog(coordinator, Mock.Of<IMessageBoxService>());
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
}
