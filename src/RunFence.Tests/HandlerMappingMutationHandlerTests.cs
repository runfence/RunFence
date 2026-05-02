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
        Mock<IHandlerMappingService> Svc,
        AppDatabase Db,
        Mock<IAssociationAutoSetService> AutoSet,
        Mock<IHandlerMappingNotifier> Notifier);

    private static MakeResult Make()
    {
        var svc = new Mock<IHandlerMappingService>();
        var autoSet = new Mock<IAssociationAutoSetService>();
        var notifier = new Mock<IHandlerMappingNotifier>();
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Id = AppId, Name = "TestApp", AllowPassingArguments = true });

        svc.Setup(s => s.GetAllHandlerMappings(db))
           .Returns(() =>
           {
               var mappings = db.Settings.HandlerMappings ?? [];
               return mappings.ToDictionary(
                   kv => kv.Key,
                   kv => (IReadOnlyList<HandlerMappingEntry>)[kv.Value],
                   StringComparer.OrdinalIgnoreCase);
           });

        svc.Setup(s => s.GetEffectiveDirectHandlerMappings(db))
           .Returns(() => db.Settings.DirectHandlerMappings
               ?? new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));

        svc.Setup(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), db))
           .Callback((string key, HandlerMappingEntry entry, AppDatabase _) =>
           {
               db.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
               db.Settings.HandlerMappings[key] = entry;
           });

        svc.Setup(s => s.SetDirectHandlerMapping(It.IsAny<string>(), It.IsAny<DirectHandlerEntry>(), db))
           .Callback((string key, DirectHandlerEntry entry, AppDatabase _) =>
           {
               db.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
               db.Settings.DirectHandlerMappings[key] = entry;
           });

        svc.Setup(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), db))
           .Callback((string key, string _, AppDatabase _) =>
           {
               if (db.Settings.HandlerMappings == null) return;
               db.Settings.HandlerMappings.Remove(key);
               if (db.Settings.HandlerMappings.Count == 0)
                   db.Settings.HandlerMappings = null;
           });

        var dbProvider = new LambdaDatabaseProvider(() => db);
        var handler = new HandlerMappingMutationHandler(svc.Object, autoSet.Object, dbProvider, notifier.Object);
        return new MakeResult(handler, svc, db, autoSet, notifier);
    }

    private static void SeedMapping(AppDatabase db, string key, string? template = null,
        List<string>? prefixes = null, bool replacePrefixes = false)
    {
        db.Settings.HandlerMappings ??= new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
        db.Settings.HandlerMappings[key] = new HandlerMappingEntry(AppId, template, prefixes, replacePrefixes);
    }

    private static void SeedDirectMapping(AppDatabase db, string key, DirectHandlerEntry entry)
    {
        db.Settings.DirectHandlerMappings ??= new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);
        db.Settings.DirectHandlerMappings[key] = entry;
    }

    private static int CountChangedFirings(HandlerMappingMutationHandler handler, Action act)
    {
        var count = 0;
        handler.Changed += () => count++;
        act();
        return count;
    }

    // ─── SetAssociationsForApp ────────────────────────────────────────────────

    [Fact]
    public void SetAssociationsForApp_NewAssociation_CallsSetHandlerMappingWithCorrectEntry()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        var prefixes = new List<string> { @"C:\Work\" };
        var items = new List<HandlerAssociationItem>
        {
            new(".txt", "--flag \"%1\"", prefixes, ReplacePrefixes: true)
        };

        // Act
        IReadOnlyList<string>? removed = null;
        var fired = CountChangedFirings(handler, () => removed = handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e =>
                e.AppId == AppId &&
                e.ArgumentsTemplate == "--flag \"%1\"" &&
                e.PathPrefixes != null &&
                e.PathPrefixes.SequenceEqual(prefixes) &&
                e.ReplacePrefixes == true),
            db), Times.Once);
        Assert.Empty(removed!);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_NewAssociation_WithNullPrefixes_SetsNullPathPrefixes()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        var items = new List<HandlerAssociationItem>
        {
            new(".pdf", null)
        };

        // Act
        var fired = CountChangedFirings(handler, () => handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".pdf",
            It.Is<HandlerMappingEntry>(e => e.PathPrefixes == null && !e.ReplacePrefixes),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_KeyAbsent_RemovesMapping_AndReturnsKey()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt");

        // Act
        IReadOnlyList<string>? removed = null;
        var fired = CountChangedFirings(handler, () => removed = handler.SetAssociationsForApp(AppId, []));

        // Assert
        svc.Verify(s => s.RemoveHandlerMapping(".txt", AppId, db), Times.Once);
        svc.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), db), Times.Never);
        Assert.Contains(".txt", removed!);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_TemplateChanged_CallsSetHandlerMapping()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", "old-template");
        var items = new List<HandlerAssociationItem> { new(".txt", "new-template") };

        // Act
        var fired = CountChangedFirings(handler, () => handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e => e.ArgumentsTemplate == "new-template"),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_PrefixesChanged_CallsSetHandlerMappingWithNewPrefixes()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", prefixes: [@"C:\Old\"]);
        var newPrefixes = new List<string> { @"C:\New\" };
        var items = new List<HandlerAssociationItem> { new(".txt", null, newPrefixes) };

        // Act
        var fired = CountChangedFirings(handler, () => handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e =>
                e.PathPrefixes != null &&
                e.PathPrefixes.SequenceEqual(newPrefixes)),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_ReplacePrefixesChanged_CallsSetHandlerMapping()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt");
        var items = new List<HandlerAssociationItem> { new(".txt", null, ReplacePrefixes: true) };

        // Act
        var fired = CountChangedFirings(handler, () => handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e => e.ReplacePrefixes == true),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetAssociationsForApp_NoChange_DoesNotCallServiceAndDoesNotFireChanged()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", "tmpl", [@"C:\Work\"]);
        var items = new List<HandlerAssociationItem>
        {
            new(".txt", "tmpl", [@"C:\Work\"])
        };

        // Act
        IReadOnlyList<string>? removed = null;
        var fired = CountChangedFirings(handler, () => removed = handler.SetAssociationsForApp(AppId, items));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), db), Times.Never);
        svc.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), db), Times.Never);
        Assert.Empty(removed!);
        Assert.Equal(0, fired);
    }

    // ─── AddAppMapping ────────────────────────────────────────────────────────

    [Fact]
    public void AddAppMapping_WithPrefixes_SetsCorrectEntryFields()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        var prefixes = new List<string> { @"C:\Docs\" };

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.AddAppMapping([".pdf"], db.Apps[0], "template", prefixes, replacePrefixes: true));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".pdf",
            It.Is<HandlerMappingEntry>(e =>
                e.PathPrefixes != null &&
                e.PathPrefixes.SequenceEqual(prefixes, StringComparer.OrdinalIgnoreCase) &&
                e.ReplacePrefixes == true),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void AddAppMapping_WithNullPrefixes_SetsNullPathPrefixes()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.AddAppMapping([".txt"], db.Apps[0], null));

        // Assert
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e => e.PathPrefixes == null && !e.ReplacePrefixes),
            db), Times.Once);
        Assert.Equal(1, fired);
    }

    // ─── ChangeAppMapping ─────────────────────────────────────────────────────

    [Fact]
    public void ChangeAppMapping_PrefixChanged_ReturnsTrueAndCallsSetHandlerMapping()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", prefixes: [@"C:\Old\"]);
        var newPrefixes = new List<string> { @"C:\New\" };

        // Act
        var changed = handler.ChangeAppMapping(".txt", AppId, db.Apps[0], null, null,
            currentPrefixes: [@"C:\Old\"], newPrefixes: newPrefixes);

        // Assert
        Assert.True(changed);
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e =>
                e.PathPrefixes != null &&
                e.PathPrefixes.SequenceEqual(newPrefixes)),
            db), Times.Once);
    }

    [Fact]
    public void ChangeAppMapping_ReplacePrefixesChanged_ReturnsTrue()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt");

        // Act
        var changed = handler.ChangeAppMapping(".txt", AppId, db.Apps[0], null, null,
            newReplacePrefixes: true);

        // Assert
        Assert.True(changed);
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e => e.ReplacePrefixes == true),
            db), Times.Once);
    }

    [Fact]
    public void ChangeAppMapping_NothingChanged_ReturnsFalse()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", prefixes: [@"C:\Work\"]);

        // Act
        var changed = handler.ChangeAppMapping(".txt", AppId, db.Apps[0], null, null,
            currentPrefixes: [@"C:\Work\"], newPrefixes: [@"C:\Work\"]);

        // Assert
        Assert.False(changed);
        svc.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), db), Times.Never);
        svc.Verify(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), db), Times.Never);
    }

    [Fact]
    public void ChangeAppMapping_NullAndEmptyPrefixesTreatedAsEqual_NoOp()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt");

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.ChangeAppMapping(".txt", AppId, db.Apps[0], null, null, newPrefixes: []));

        // Assert — null and empty list must be treated as equal, so no mutation
        svc.Verify(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), db), Times.Never);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void ChangeAppMapping_NonEmptyToNull_DetectsChangeAndSetsNullPrefixes()
    {
        // Arrange
        var (handler, svc, db, _, _) = Make();
        SeedMapping(db, ".txt", prefixes: [@"C:\Work\"]);

        // Act
        var changed = handler.ChangeAppMapping(".txt", AppId, db.Apps[0], null, null,
            currentPrefixes: [@"C:\Work\"]);

        // Assert
        Assert.True(changed);
        svc.Verify(s => s.SetHandlerMapping(
            ".txt",
            It.Is<HandlerMappingEntry>(e => e.PathPrefixes == null),
            db), Times.Once);
    }

    // ─── UpdateAppPrefixes ────────────────────────────────────────────────────

    [Fact]
    public void UpdateAppPrefixes_WhenAppFound_UpdatesPathPrefixesAndFiresChanged()
    {
        // Arrange
        var (handler, _, db, _, _) = Make();
        var prefixes = new List<string> { @"C:\Data\" };

        // Act
        var fired = CountChangedFirings(handler, () => handler.UpdateAppPrefixes(AppId, prefixes));

        // Assert
        Assert.Equal(prefixes, db.Apps[0].PathPrefixes);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void UpdateAppPrefixes_WhenEmptyListPassed_SetsPathPrefixesToNull()
    {
        // Arrange
        var (handler, _, db, _, _) = Make();
        db.Apps[0].PathPrefixes = [@"C:\Old\"];

        // Act
        var fired = CountChangedFirings(handler, () => handler.UpdateAppPrefixes(AppId, []));

        // Assert
        Assert.Null(db.Apps[0].PathPrefixes);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void UpdateAppPrefixes_WhenAppNotFound_DoesNotFireChanged()
    {
        // Arrange
        var (handler, _, _, _, _) = Make();

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.UpdateAppPrefixes("nonexistent-id", [@"C:\Anything\"]));

        // Assert
        Assert.Equal(0, fired);
    }

    // ─── AddDirectHandler ─────────────────────────────────────────────────────

    [Fact]
    public void AddDirectHandler_WhenTypeChangesClassNameToCommand_RestoresKey()
    {
        // Arrange: existing ClassName handler, adding a Command handler for the same key
        var (handler, _, db, autoSet, _) = Make();
        SeedDirectMapping(db, ".txt", new DirectHandlerEntry { ClassName = "txtfile" });

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.AddDirectHandler([".txt"], [new DirectHandlerEntry { Command = @"C:\notepad.exe ""%1""" }]));

        // Assert: type changed ClassName→Command → RestoreKeyForAllUsers must be called
        autoSet.Verify(a => a.RestoreKeyForAllUsers(".txt"), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void AddDirectHandler_WhenTypeSame_DoesNotRestore()
    {
        // Arrange: existing ClassName handler, adding another ClassName handler
        var (handler, _, db, autoSet, _) = Make();
        SeedDirectMapping(db, ".txt", new DirectHandlerEntry { ClassName = "txtfile" });

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.AddDirectHandler([".txt"], [new DirectHandlerEntry { ClassName = "wordpadfile" }]));

        // Assert: same type (ClassName→ClassName) → RestoreKeyForAllUsers must NOT be called
        autoSet.Verify(a => a.RestoreKeyForAllUsers(It.IsAny<string>()), Times.Never);
        Assert.Equal(1, fired);
    }

    // ─── EditDirectHandler ────────────────────────────────────────────────────

    [Fact]
    public void EditDirectHandler_WhenTypeChanges_RestoresKey()
    {
        // Arrange: editing a ClassName handler to a Command handler
        var (handler, _, _, autoSet, _) = Make();
        var currentEntry = new DirectHandlerEntry { ClassName = "txtfile" };
        var newEntry = new DirectHandlerEntry { Command = @"C:\notepad.exe ""%1""" };

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.EditDirectHandler(".txt", currentEntry, newEntry));

        // Assert: type changed ClassName→Command → RestoreKeyForAllUsers must be called
        autoSet.Verify(a => a.RestoreKeyForAllUsers(".txt"), Times.Once);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void EditDirectHandler_WhenTypeSame_DoesNotRestore()
    {
        // Arrange: editing a Command handler to another Command handler
        var (handler, _, _, autoSet, _) = Make();
        var currentEntry = new DirectHandlerEntry { Command = @"C:\old.exe ""%1""" };
        var newEntry = new DirectHandlerEntry { Command = @"C:\new.exe ""%1""" };

        // Act
        var fired = CountChangedFirings(handler, () =>
            handler.EditDirectHandler(".txt", currentEntry, newEntry));

        // Assert: same type (Command→Command) → RestoreKeyForAllUsers must NOT be called
        autoSet.Verify(a => a.RestoreKeyForAllUsers(It.IsAny<string>()), Times.Never);
        Assert.Equal(1, fired);
    }

    // ─── F-30: Immediate-save behavior (AllowPassingArguments) ───────────────

    [Fact]
    public void AddAppMapping_WhenAllowPassingArgumentsFalse_EnablesImmediately()
    {
        // Arrange: app has AllowPassingArguments=false
        var (handler, _, db, _, notifier) = Make();
        var app = db.Apps[0];
        app.AllowPassingArguments = false;

        // Act
        handler.AddAppMapping([".txt"], app, null);

        // Assert: AllowPassingArguments set to true immediately (not deferred)
        Assert.True(app.AllowPassingArguments);
        notifier.Verify(n => n.ShowAllowPassingArgumentsEnabled(app.Name), Times.Once);
    }

    [Fact]
    public void RemoveMapping_WhenLastMappingForApp_DisablesAllowPassingArguments()
    {
        // Arrange: one mapping for app, AllowPassingArguments=true
        var (handler, _, db, _, notifier) = Make();
        var app = db.Apps[0];
        app.AllowPassingArguments = true;
        SeedMapping(db, ".txt");

        // Act: remove the only mapping
        handler.RemoveMapping(new AppMappingRowTag(".txt", AppId));

        // Assert: AllowPassingArguments reverted to false
        Assert.False(app.AllowPassingArguments);
        notifier.Verify(n => n.ShowAllowPassingArgumentsDisabled(app.Name), Times.Once);
    }

    [Fact]
    public void RemoveMapping_WhenOtherMappingsRemain_KeepsAllowPassingArguments()
    {
        // Arrange: two mappings for the same app
        var (handler, _, db, _, notifier) = Make();
        var app = db.Apps[0];
        app.AllowPassingArguments = true;
        SeedMapping(db, ".txt");
        SeedMapping(db, ".pdf");

        // Act: remove only .txt — .pdf still maps to the same app
        handler.RemoveMapping(new AppMappingRowTag(".txt", AppId));

        // Assert: AllowPassingArguments stays true (other mappings remain)
        Assert.True(app.AllowPassingArguments);
        notifier.Verify(n => n.ShowAllowPassingArgumentsDisabled(It.IsAny<string>()), Times.Never);
    }

    // ─── F-82: RemoveMapping calls RestoreKeyForAllUsers when no remaining app mappings ──

    [Fact]
    public void RemoveMapping_WhenNoRemainingAppMappingsForKey_CallsRestoreKeyForAllUsers()
    {
        // Arrange: one mapping for the key; after removal, no app-based mapping remains for it
        var (handler, _, db, autoSet, _) = Make();
        SeedMapping(db, ".txt");

        // Act
        handler.RemoveMapping(new AppMappingRowTag(".txt", AppId));

        // Assert: RestoreKeyForAllUsers called because no app-based mapping remains for ".txt"
        autoSet.Verify(a => a.RestoreKeyForAllUsers(".txt"), Times.Once);
    }
}
