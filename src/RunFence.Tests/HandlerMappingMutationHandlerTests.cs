using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingMutationHandlerTests
{
    private const string AppId = "app01";

    private record struct MakeResult(
        HandlerMappingMutationHandler Handler,
        Mock<IHandlerMappingService> Service,
        AppDatabase Database);

    private static MakeResult Make()
    {
        var service = new Mock<IHandlerMappingService>();
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = AppId, Name = "TestApp", AllowPassingArguments = true });

        service.Setup(s => s.GetAllHandlerMappings(database))
            .Returns(() =>
            {
                var mappings = database.Settings.HandlerMappings ?? [];
                return mappings.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<HandlerMappingEntry>)[kv.Value],
                    StringComparer.OrdinalIgnoreCase);
            });

        service.Setup(s => s.GetEffectiveDirectHandlerMappings(database))
            .Returns(() => database.Settings.DirectHandlerMappings
                ?? new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));

        service.Setup(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database))
            .Callback((string key, HandlerMappingEntry entry, AppDatabase _) =>
            {
                database.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
                database.Settings.HandlerMappings[key] = entry;
            });

        service.Setup(s => s.SetDirectHandlerMapping(It.IsAny<string>(), It.IsAny<DirectHandlerEntry>(), database))
            .Callback((string key, DirectHandlerEntry entry, AppDatabase _) =>
            {
                database.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
                database.Settings.DirectHandlerMappings[key] = entry;
            });

        service.Setup(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database))
            .Callback((string key, string _, AppDatabase _) =>
            {
                if (database.Settings.HandlerMappings == null)
                    return;

                database.Settings.HandlerMappings.Remove(key);
                if (database.Settings.HandlerMappings.Count == 0)
                    database.Settings.HandlerMappings = null;
            });

        service.Setup(s => s.RemoveDirectHandlerMapping(It.IsAny<string>(), database))
            .Callback((string key, AppDatabase _) =>
            {
                if (database.Settings.DirectHandlerMappings == null)
                    return;

                database.Settings.DirectHandlerMappings.Remove(key);
                if (database.Settings.DirectHandlerMappings.Count == 0)
                    database.Settings.DirectHandlerMappings = null;
            });

        return new MakeResult(new HandlerMappingMutationHandler(service.Object), service, database);
    }

    private static int CountChangedFirings(HandlerMappingMutationHandler handler, Action action)
    {
        var count = 0;
        handler.Changed += () => count++;
        action();
        return count;
    }

    private static void SeedAppMapping(AppDatabase database, string key, string? template = null,
        IReadOnlyList<string>? prefixes = null, bool replacePrefixes = false)
    {
        database.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
        database.Settings.HandlerMappings[key] = new HandlerMappingEntry(
            AppId,
            template,
            prefixes?.ToList(),
            replacePrefixes);
    }

    private static void SeedDirectMapping(AppDatabase database, string key, DirectHandlerEntry entry)
    {
        database.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
        database.Settings.DirectHandlerMappings[key] = entry;
    }

    [Fact]
    public void SetAssociationsForApp_NewAssociation_WritesExpectedEntry()
    {
        var (handler, service, database) = Make();
        var prefixes = new List<string> { @"C:\Work\" };

        IReadOnlyList<string>? removedKeys = null;
        var fired = CountChangedFirings(handler, () =>
            removedKeys = handler.SetAssociationsForApp(
                database,
                AppId,
                [new HandlerAssociationItem(".txt", "--flag \"%1\"", prefixes, ReplacePrefixes: true)]));

        service.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(entry =>
                entry.AppId == AppId &&
                entry.ArgumentsTemplate == "--flag \"%1\"" &&
                entry.PathPrefixes != null &&
                entry.PathPrefixes.SequenceEqual(prefixes) &&
                entry.ReplacePrefixes),
            database),
            Times.Once);
        Assert.Empty(removedKeys!);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_NoChange_DoesNotMutate()
    {
        var (handler, service, database) = Make();
        SeedAppMapping(database, ".txt", "tmpl", [@"C:\Work\"]);

        IReadOnlyList<string>? removedKeys = null;
        var fired = CountChangedFirings(handler, () =>
            removedKeys = handler.SetAssociationsForApp(
                database,
                AppId,
                [new HandlerAssociationItem(".txt", "tmpl", [@"C:\Work\"])]));

        service.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database), Times.Never);
        service.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database), Times.Never);
        Assert.Empty(removedKeys!);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void AddAppMapping_WhenRequired_AutoEnablesAllowPassingArguments()
    {
        var (handler, _, database) = Make();
        var app = database.Apps[0];
        app.AllowPassingArguments = false;

        var fired = CountChangedFirings(handler, () =>
            handler.AddAppMapping(
                database,
                [".txt"],
                app,
                null,
                requiresAllowPassingArgumentsEnable: true));

        Assert.True(app.AllowPassingArguments);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ChangeAppMapping_WhenNoChanges_DoesNotMutate()
    {
        var (handler, service, database) = Make();
        SeedAppMapping(database, ".txt");

        var fired = CountChangedFirings(handler, () =>
            handler.ChangeAppMapping(database, ".txt", AppId, database.Apps[0], null, null, newPrefixes: []));

        service.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database), Times.Never);
        service.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database), Times.Never);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ChangeAppMapping_WithOnlyAllowPassingArgumentsEnableReturnsChanged()
    {
        var (handler, service, database) = Make();
        SeedAppMapping(database, ".txt");
        database.Apps[0].AllowPassingArguments = false;

        bool changed = false;
        var fired = CountChangedFirings(handler, () =>
            changed = handler.ChangeAppMapping(
                database,
                ".txt",
                AppId,
                database.Apps[0],
                null,
                null,
                requiresAllowPassingArgumentsEnable: true));

        service.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database), Times.Never);
        service.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database), Times.Never);
        Assert.True(changed);
        Assert.True(database.Apps[0].AllowPassingArguments);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ChangeAppMapping_WhenAlreadyAllowsPassingArgumentsIsNoOp()
    {
        var (handler, service, database) = Make();
        SeedAppMapping(database, ".txt");
        database.Apps[0].AllowPassingArguments = true;

        bool changed = true;
        var fired = CountChangedFirings(handler, () =>
            changed = handler.ChangeAppMapping(
                database,
                ".txt",
                AppId,
                database.Apps[0],
                null,
                null,
                requiresAllowPassingArgumentsEnable: true));

        service.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), database), Times.Never);
        service.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), database), Times.Never);
        Assert.False(changed);
        Assert.True(database.Apps[0].AllowPassingArguments);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void UpdateAppPrefixes_EmptyListClearsPrefixes()
    {
        var (handler, _, database) = Make();
        database.Apps[0].PathPrefixes = [@"C:\Old\"];

        bool changed = false;
        var fired = CountChangedFirings(handler, () => changed = handler.UpdateAppPrefixes(database, AppId, []));

        Assert.True(changed);
        Assert.Null(database.Apps[0].PathPrefixes);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void UpdateAppPrefixes_UnchangedPrefixes_IsNoOp()
    {
        var (handler, _, database) = Make();
        database.Apps[0].PathPrefixes = [@"C:\Same\"];

        bool changed = true;
        var fired = CountChangedFirings(handler, () => changed = handler.UpdateAppPrefixes(database, AppId, [@"C:\Same\"]));

        Assert.False(changed);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void AddDirectHandler_TypeChangeReturnsRestoreKey()
    {
        var (handler, _, database) = Make();
        SeedDirectMapping(database, ".txt", new DirectHandlerEntry { ClassName = "txtfile" });

        HandlerMappingMutationOutcome? outcome = null;
        var fired = CountChangedFirings(handler, () =>
            outcome = handler.AddDirectHandler(
                database,
                [".txt"],
                [new DirectHandlerEntry { Command = @"C:\notepad.exe ""%1""" }]));

        Assert.Equal([".txt"], outcome!.KeysToRestore);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void AddDirectHandler_SameTypeReturnsNoRestoreKey()
    {
        var (handler, _, database) = Make();
        SeedDirectMapping(database, ".txt", new DirectHandlerEntry { ClassName = "txtfile" });

        HandlerMappingMutationOutcome? outcome = null;
        var fired = CountChangedFirings(handler, () =>
            outcome = handler.AddDirectHandler(
                database,
                [".txt"],
                [new DirectHandlerEntry { ClassName = "wordpadfile" }]));

        Assert.Empty(outcome!.KeysToRestore);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void EditDirectHandler_TypeChangeReturnsRestoreKey()
    {
        var (handler, _, database) = Make();

        HandlerMappingMutationOutcome? outcome = null;
        var fired = CountChangedFirings(handler, () =>
            outcome = handler.EditDirectHandler(
                database,
                ".txt",
                new DirectHandlerEntry { ClassName = "txtfile" },
                new DirectHandlerEntry { Command = @"C:\notepad.exe ""%1""" }));

        Assert.Equal([".txt"], outcome!.KeysToRestore);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void RemoveMapping_ReturnsRemovedEntryAndRestoreState()
    {
        var (handler, _, database) = Make();
        SeedAppMapping(database, ".txt", "tmpl");

        var removed = handler.RemoveMapping(database, new AppMappingRowTag(".txt", AppId));

        Assert.NotNull(removed);
        Assert.Equal(".txt", removed!.Key);
        Assert.Equal(AppId, removed.Entry.AppId);
        Assert.Equal("tmpl", removed.Entry.ArgumentsTemplate);
        Assert.True(removed.RequiresRestore);
    }

    [Fact]
    public void RestoreRemovedAppMapping_RecreatesOriginalEntry()
    {
        var (handler, _, database) = Make();
        SeedAppMapping(database, ".txt", "tmpl");

        var removed = handler.RemoveMapping(database, new AppMappingRowTag(".txt", AppId));
        Assert.NotNull(removed);

        handler.RestoreRemovedAppMapping(database, removed!);

        Assert.NotNull(database.Settings.HandlerMappings);
        Assert.Equal("tmpl", database.Settings.HandlerMappings![".txt"].ArgumentsTemplate);
    }

    [Fact]
    public void RemoveDirectHandler_ReturnsRemovedEntryAndRestoreState()
    {
        var (handler, _, database) = Make();
        SeedDirectMapping(database, ".txt", new DirectHandlerEntry { Command = @"C:\notepad.exe ""%1""" });

        var removed = handler.RemoveDirectHandler(database, new DirectHandlerRowTag(".txt"));

        Assert.NotNull(removed);
        Assert.Equal(".txt", removed!.Key);
        Assert.Equal(@"C:\notepad.exe ""%1""", removed.Entry.Command);
        Assert.True(removed.RequiresRestore);
    }

    [Fact]
    public void RestoreRemovedDirectHandler_RecreatesOriginalEntry()
    {
        var (handler, _, database) = Make();
        SeedDirectMapping(database, ".txt", new DirectHandlerEntry { ClassName = "txtfile" });

        var removed = handler.RemoveDirectHandler(database, new DirectHandlerRowTag(".txt"));
        Assert.NotNull(removed);

        handler.RestoreRemovedDirectHandler(database, removed!);

        Assert.NotNull(database.Settings.DirectHandlerMappings);
        Assert.Equal("txtfile", database.Settings.DirectHandlerMappings![".txt"].ClassName);
    }
}
