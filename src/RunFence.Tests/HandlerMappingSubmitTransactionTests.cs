using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingSubmitTransactionTests
{
    private const string AppId = "app01";

    private sealed record TestContext(
        AppDatabase Database,
        Mock<IHandlerMappingService> HandlerMappingService,
        Mock<IAppHandlerRegistrationService> RegistrationService,
        Mock<IAssociationAutoSetService> AutoSetService,
        FakeHandlerMappingDialogPersistence Persistence,
        HandlerMappingSubmitTransaction Transaction);

    private static TestContext CreateContext()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = AppId,
            Name = "Test App",
            AllowPassingArguments = false,
            PathPrefixes = [@"C:\Original\"]
        });

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

        var syncService = new HandlerMappingSyncService(
            handlerMappingService.Object,
            registrationService.Object,
            autoSetService.Object,
            new LambdaDatabaseProvider(() => database));

        var persistence = new FakeHandlerMappingDialogPersistence(database);

        return new TestContext(
            database,
            handlerMappingService,
            registrationService,
            autoSetService,
            persistence,
            new HandlerMappingSubmitTransaction(
                handlerMappingService.Object,
                syncService));
    }

    [Fact]
    public async Task SubmitAsync_WhenSaveFails_RestoresAffectedAppMappingsAndAppState()
    {
        var context = CreateContext();
        context.Database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new HandlerMappingEntry(AppId, "\"old %1\"")
        };

        context = context with
        {
            Persistence = new FakeHandlerMappingDialogPersistence(context.Database, () => throw new InvalidOperationException("disk full"))
        };

        var result = await context.Transaction.SubmitAsync(
                context.Persistence,
                [".txt"],
                [AppId],
                database =>
                {
                    var app = database.Apps.Single();
                    app.PathPrefixes = [@"C:\Changed\"];
                    app.AllowPassingArguments = true;
                    context.HandlerMappingService.Object.SetHandlerMapping(
                        ".txt",
                        new HandlerMappingEntry(AppId, "\"new %1\"", [@"C:\Work\"], true),
                        database);
                    return null;
                });

        Assert.False(result.ShouldClose);
        Assert.False(result.SavedDurably);
        Assert.Equal("disk full", result.SaveError);
        Assert.Null(result.RegistrySyncWarning);

        var restoredEntry = context.Database.Settings.HandlerMappings![".txt"];
        Assert.Equal("\"old %1\"", restoredEntry.ArgumentsTemplate);
        Assert.Null(restoredEntry.PathPrefixes);
        Assert.False(restoredEntry.ReplacePrefixes);

        var restoredApp = context.Database.Apps.Single();
        Assert.Equal([@"C:\Original\"], restoredApp.PathPrefixes);
        Assert.False(restoredApp.AllowPassingArguments);
    }

    [Fact]
    public async Task SubmitAsync_WhenSnapshotCaptureFails_ReturnsRetryableFailureWithoutMutating()
    {
        var context = CreateContext();
        context.HandlerMappingService
            .Setup(service => service.GetAllHandlerMappings(context.Database))
            .Throws(new InvalidOperationException("snapshot failed"));
        var mutateCalled = false;

        var result = await context.Transaction.SubmitAsync(
            context.Persistence,
            [".txt"],
            [AppId],
            _ =>
            {
                mutateCalled = true;
                return null;
            });

        Assert.False(result.ShouldClose);
        Assert.False(result.SavedDurably);
        Assert.Equal("snapshot failed", result.SaveError);
        Assert.False(mutateCalled);
        Assert.Equal(0, context.Persistence.SaveDatabaseCalls);
        Assert.Null(context.Database.Settings.HandlerMappings);
        Assert.Equal([@"C:\Original\"], context.Database.Apps.Single().PathPrefixes);
        Assert.False(context.Database.Apps.Single().AllowPassingArguments);
    }

    [Fact]
    public async Task SubmitAsync_WhenSaveFails_RestoresAffectedDirectHandler()
    {
        var context = CreateContext();
        context.Database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        context = context with
        {
            Persistence = new FakeHandlerMappingDialogPersistence(context.Database, () => throw new InvalidOperationException("save failed"))
        };

        var result = await context.Transaction.SubmitAsync(
                context.Persistence,
                [".txt"],
                [],
                database =>
                {
                    context.HandlerMappingService.Object.SetDirectHandlerMapping(
                        ".txt",
                        new DirectHandlerEntry { Command = "\"C:\\Tools\\viewer.exe\" \"%1\"" },
                        database);
                    return [".txt"];
                });

        Assert.False(result.ShouldClose);
        Assert.Equal("save failed", result.SaveError);
        Assert.Equal("txtfile", context.Database.Settings.DirectHandlerMappings![".txt"].ClassName);
        Assert.Null(context.Database.Settings.DirectHandlerMappings[".txt"].Command);
    }

    [Fact]
    public async Task SubmitAsync_WhenSyncFailsAfterSave_ReturnsWarningCompletionAndKeepsMutation()
    {
        var context = CreateContext();

        context.RegistrationService.Setup(service => service.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
            .Throws(new InvalidOperationException("registry unavailable"));

        var result = await context.Transaction.SubmitAsync(
                context.Persistence,
                [".txt"],
                [AppId],
                database =>
                {
                    var app = database.Apps.Single();
                    app.AllowPassingArguments = true;
                    context.HandlerMappingService.Object.SetHandlerMapping(
                        ".txt",
                        new HandlerMappingEntry(AppId, "\"%1\""),
                        database);
                    return null;
                });

        Assert.True(result.ShouldClose);
        Assert.True(result.SavedDurably);
        Assert.Equal("registry unavailable", result.RegistrySyncWarning);
        Assert.Null(result.SaveError);
        Assert.Equal("\"%1\"", context.Database.Settings.HandlerMappings![".txt"].ArgumentsTemplate);
        Assert.True(context.Database.Apps.Single().AllowPassingArguments);
    }
}
