using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class AclManagerTraverseHelperTests
{
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";

    [Fact]
    public void PopulateTraverseGrid_Container_UsesAllApplicationPackagesTraverseEntries()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\Shared", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(sharedEntry);

        var helper = CreateHelper(database, loadedConfigPaths: [], out var grid);
        helper.Initialize(grid, ContainerSid, isContainer: true, new AclManagerPendingChanges(), [], new AclManagerSectionHeaderFactory(), sortHelper: null);

        helper.PopulateTraverseGrid();

        var displayedEntries = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag)
            .OfType<GrantedPathEntry>()
            .ToList();
        Assert.Contains(sharedEntry, displayedEntries);
    }

    [Fact]
    public void PopulateTraverseGrid_Container_ExcludesOtherContainerTrackedSharedTraverseEntries()
    {
        var ownEntry = new GrantedPathEntry
        {
            Path = @"C:\Own",
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        };
        var otherEntry = new GrantedPathEntry
        {
            Path = @"C:\Other",
            IsTraverseOnly = true,
            SourceSids = ["S-1-15-2-99-1-2-3-4-5-7"]
        };
        var manualEntry = new GrantedPathEntry
        {
            Path = @"C:\Manual",
            IsTraverseOnly = true,
            SourceSids = null
        };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.AddRange(
            [ownEntry, otherEntry, manualEntry]);

        var helper = CreateHelper(database, loadedConfigPaths: [], out var grid);
        helper.Initialize(grid, ContainerSid, isContainer: true, new AclManagerPendingChanges(), [], new AclManagerSectionHeaderFactory(), sortHelper: null);

        helper.PopulateTraverseGrid();

        var displayedEntries = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag)
            .OfType<GrantedPathEntry>()
            .ToList();
        Assert.Contains(ownEntry, displayedEntries);
        Assert.Contains(manualEntry, displayedEntries);
        Assert.DoesNotContain(otherEntry, displayedEntries);
    }

    [Fact]
    public void PopulateTraverseGrid_ContainerTraverseEntry_UsesAllApplicationPackagesStoreOwnership()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\Shared", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(sharedEntry);

        var mainStore = new TestGrantIntentStore();
        var provider = new TestGrantIntentStoreProvider(mainStore);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        additionalStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, sharedEntry);
        provider.AddLoadedStore(additionalStore);

        var helper = CreateHelper(
            database,
            loadedConfigPaths: [additionalStore.ConfigPath!],
            out var grid,
            provider);
        helper.Initialize(grid, ContainerSid, isContainer: true, new AclManagerPendingChanges(), [], new AclManagerSectionHeaderFactory(), sortHelper: null);

        helper.PopulateTraverseGrid();

        var sectionHeaders = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<ConfigSectionHeader>()
            .ToList();
        Assert.Contains(sectionHeaders, header => string.Equals(
            header.ConfigPath,
            additionalStore.ConfigPath,
            StringComparison.OrdinalIgnoreCase));
    }

    private static AclManagerTraverseHelper CreateHelper(
        AppDatabase database,
        IReadOnlyList<string> loadedConfigPaths,
        out DataGridView grid,
        TestGrantIntentStoreProvider? storeProvider = null)
    {
        grid = new DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewImageColumn { Name = AclManagerGrantsHelper.ColIcon });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TraversePath" });

        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(s => s.ResolveAccountGroupSids(It.IsAny<string>())).Returns([]);
        var provider = storeProvider ?? new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        var repository = new GrantIntentRepository(provider);
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        var pathInfo = new TestFileSystemPathInfo();
        var reparsePointHelper = new Mock<IReparsePointPromptHelper>();
        var traverseOperations = new AclManagerTraverseOperations(
            databaseProvider,
            reparsePointHelper.Object,
            aclPermission.Object,
            pathInfo,
            Mock.Of<IOpenFileDialogAdapterFactory>());
        var resolver = new TraverseEntryResolver(
            aclPermission.Object,
            new Mock<ITraverseAcl>().Object,
            pathInfo);
        var rowBuilder = new AclManagerTraverseRowBuilder(
            new Mock<IAclPathIconProvider>().Object,
            resolver);

        var appConfig = new AppConfigTestContext();
        foreach (var path in loadedConfigPaths)
            appConfig.AddLoadedConfig(path);
        return new AclManagerTraverseHelper(
            appConfig.Service,
            repository,
            provider,
            databaseProvider,
            new TraverseGrantOwnerResolver(),
            traverseOperations,
            rowBuilder);
    }
}
