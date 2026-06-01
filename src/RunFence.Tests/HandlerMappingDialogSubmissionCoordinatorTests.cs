using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingDialogSubmissionCoordinatorTests
{
    private const string AppId = "app01";

    [Fact]
    public void SubmitAdd_WhenDirectValidationFails_ReturnsValidationMessage()
    {
        var context = CreateContext();

        var result = context.Coordinator.SubmitAdd(new HandlerMappingAddDialogSubmitRequest(
            IsDirectMode: true,
            ResolvedKeys: ["bad key"],
            SelectedApp: null,
            DirectHandlerValue: "Acrobat.Document.DC",
            ArgumentsTemplate: null,
            AppPrefixes: null,
            PathPrefixes: null,
            ReplacePrefixes: false),
            new FakeHandlerMappingDialogPersistence(context.Database, () => throw new InvalidOperationException("should not save")));

        Assert.Null(result.DialogResult);
        Assert.False(result.HasUnresolvedFailure);
        Assert.Contains("bad key", result.ValidationMessage, StringComparison.Ordinal);
        Assert.Null(context.Database.Settings.DirectHandlerMappings);
    }

    [Fact]
    public void SubmitAdd_WhenSaveFails_RestoresDatabaseAndReturnsUnresolvedFailure()
    {
        var context = CreateContext(saveDatabase: () => throw new InvalidOperationException("save failed"));

        var result = context.Coordinator.SubmitAdd(new HandlerMappingAddDialogSubmitRequest(
            IsDirectMode: true,
            ResolvedKeys: [".pdf"],
            SelectedApp: null,
            DirectHandlerValue: "Acrobat.Document.DC",
            ArgumentsTemplate: null,
            AppPrefixes: null,
            PathPrefixes: null,
            ReplacePrefixes: false),
            context.Persistence);

        Assert.Null(result.DialogResult);
        Assert.True(result.HasUnresolvedFailure);
        Assert.Contains("save failed", result.UnresolvedFailureText, StringComparison.Ordinal);
        Assert.Null(result.UnexpectedErrorMessage);
        Assert.Null(context.Database.Settings.DirectHandlerMappings);
    }

    [Fact]
    public void SubmitEditApp_WhenNothingChanged_ReturnsCloseWithoutSaving()
    {
        var context = CreateContext(seed: (database, _) =>
        {
            database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new HandlerMappingEntry(AppId, "\"old %1\"")
            };
        });
        context.App.AllowPassingArguments = true;

        var saveCalls = 0;
        var savePersistence = new FakeHandlerMappingDialogPersistence(context.Database, () => saveCalls++);
        var result = context.Coordinator.SubmitEditApp(new EditAppHandlerMappingSubmitRequest(
            Key: ".txt",
            SelectedApp: context.App,
            ArgumentsTemplate: "\"old %1\"",
            AppPrefixes: null,
            PathPrefixes: null,
            ReplacePrefixes: false,
            CurrentAppId: AppId,
            CurrentTemplateInRow: "\"old %1\"",
            CurrentPathPrefixes: null,
            CurrentReplacePrefixes: false),
            savePersistence);

        Assert.Equal(System.Windows.Forms.DialogResult.OK, result.DialogResult);
        Assert.False(result.HasUnresolvedFailure);
        Assert.Null(result.ValidationMessage);
        Assert.Equal(0, saveCalls);
        Assert.Equal("\"old %1\"", context.Database.Settings.HandlerMappings![".txt"].ArgumentsTemplate);
    }

    [Fact]
    public void SubmitEditDirect_WhenSaveFails_RestoresDirectHandler()
    {
        var context = CreateContext(
            seed: (database, _) =>
            {
                database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
                };
            },
            saveDatabase: () => throw new InvalidOperationException("save failed"));
        var currentEntry = context.Database.Settings.DirectHandlerMappings![".txt"];

        var result = context.Coordinator.SubmitEditDirect(new EditDirectHandlerMappingSubmitRequest(
            Key: ".txt",
            CurrentEntry: currentEntry,
            CurrentValue: "txtfile",
            NewValue: "\"C:\\Tools\\viewer.exe\" \"%1\""),
            context.Persistence);

        Assert.Null(result.DialogResult);
        Assert.True(result.HasUnresolvedFailure);
        Assert.Equal("txtfile", context.Database.Settings.DirectHandlerMappings[".txt"].ClassName);
        Assert.Null(context.Database.Settings.DirectHandlerMappings[".txt"].Command);
    }

    [Fact]
    public void SubmitEditApp_WhenSaveFails_RestoresMappingAndAppState()
    {
        var context = CreateContext(
            seed: (database, _) =>
            {
                database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [".txt"] = new HandlerMappingEntry(AppId, "\"old %1\"")
                };
            },
            saveDatabase: () => throw new InvalidOperationException("save failed"));

        var result = context.Coordinator.SubmitEditApp(new EditAppHandlerMappingSubmitRequest(
            Key: ".txt",
            SelectedApp: context.App,
            ArgumentsTemplate: "\"new %1\"",
            AppPrefixes: null,
            PathPrefixes: null,
            ReplacePrefixes: false,
            CurrentAppId: AppId,
            CurrentTemplateInRow: "\"old %1\"",
            CurrentPathPrefixes: null,
            CurrentReplacePrefixes: false),
            context.Persistence);

        Assert.Null(result.DialogResult);
        Assert.True(result.HasUnresolvedFailure);
        Assert.Equal("\"old %1\"", context.Database.Settings.HandlerMappings![".txt"].ArgumentsTemplate);
        Assert.False(context.App.AllowPassingArguments);
    }

    [Fact]
    public void SubmitImport_WhenRegistrySyncFails_ReturnsWarningAndKeepsSavedMutation()
    {
        var context = CreateContext();
        context.RegistrationService
            .Setup(service => service.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
            .Throws(new InvalidOperationException("registry unavailable"));
        var selectedEntries =
            new[]
            {
                new InteractiveAssociationEntry(".pdf", new DirectHandlerEntry { ClassName = "Acrobat.Document.DC" }, "Pdf")
            };

        var result = context.Coordinator.SubmitImport(
            new ImportAssociationsDialogSubmitRequest(selectedEntries),
            context.Persistence);

        Assert.Equal(System.Windows.Forms.DialogResult.OK, result.DialogResult);
        Assert.False(result.HasUnresolvedFailure);
        Assert.Contains("registry unavailable", result.WarningMessage, StringComparison.Ordinal);
        Assert.NotNull(context.Database.Settings.DirectHandlerMappings);
        Assert.Equal("Acrobat.Document.DC", context.Database.Settings.DirectHandlerMappings![".pdf"].ClassName);
        context.Log.Verify(log => log.Warn(
            It.Is<string>(text => text.Contains("registry unavailable", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void ExecuteRetryable_WhenCallbackThrows_ReturnsUnexpectedFailure()
    {
        var exception = new InvalidOperationException("unexpected failure");
        Exception? reported = null;

        var result = HandlerMappingDialogSubmissionCoordinator.ExecuteRetryable(
            () => throw exception,
            ex => reported = ex);

        Assert.Null(result.DialogResult);
        Assert.True(result.HasUnresolvedFailure);
        Assert.Same(exception, reported);
        Assert.Contains("unexpected failure", result.UnexpectedErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteRetryable_WhenReporterThrows_StillReturnsUnexpectedFailure()
    {
        var result = HandlerMappingDialogSubmissionCoordinator.ExecuteRetryable(
            () => throw new InvalidOperationException("unexpected failure"),
            _ => throw new InvalidOperationException("report failed"));

        Assert.Null(result.DialogResult);
        Assert.True(result.HasUnresolvedFailure);
        Assert.Contains("unexpected failure", result.UnexpectedErrorMessage, StringComparison.Ordinal);
    }

    private sealed record TestContext(
        AppDatabase Database,
        AppEntry App,
        Mock<IAppHandlerRegistrationService> RegistrationService,
        Mock<ILoggingService> Log,
        HandlerMappingDialogSubmissionCoordinator Coordinator,
        FakeHandlerMappingDialogPersistence Persistence);

    private static TestContext CreateContext(
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
        var syncService = new HandlerMappingSyncService(
            handlerMappingService.Object,
            registrationService.Object,
            autoSetService.Object,
            databaseProvider);
        var submitTransaction = new HandlerMappingSubmitTransaction(
            handlerMappingService.Object,
            syncService);

        var persistence = new FakeHandlerMappingDialogPersistence(database, saveDatabase);

        var exeReader = new Mock<IExeAssociationRegistryReader>();
        exeReader.Setup(reader => reader.IsRegisteredProgId(".pdf", "Acrobat.Document.DC")).Returns(true);
        var dialogHelper = new HandlerMappingDialogHelper(exeReader.Object, handlerMappingService.Object);
        var mutationHandler = new HandlerMappingMutationHandler(handlerMappingService.Object);
        var log = new Mock<ILoggingService>();
        var coordinator = new HandlerMappingDialogSubmissionCoordinator(
            dialogHelper,
            mutationHandler,
            submitTransaction,
            log.Object);

        return new TestContext(
            database,
            app,
            registrationService,
            log,
            coordinator,
            persistence);
    }
}
