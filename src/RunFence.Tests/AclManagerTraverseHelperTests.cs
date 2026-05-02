using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class AclManagerTraverseHelperTests
{
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";

    [Fact]
    public void PopulateTraverseGrid_Container_ShowsSpecificAndSharedTraverseEntries()
    {
        var specificEntry = new GrantedPathEntry { Path = @"C:\Specific", IsTraverseOnly = true };
        var sharedEntry = new GrantedPathEntry { Path = @"C:\Shared", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.GetOrCreateAccount(ContainerSid).Grants.Add(specificEntry);
        database.SharedContainerTraverseGrants.Add(sharedEntry);

        var helper = CreateHelper(database, out var grid, out _);
        helper.Initialize(grid, ContainerSid, isContainer: true, new AclManagerPendingChanges(), sortHelper: null);

        helper.PopulateTraverseGrid();

        var displayedEntries = grid.Rows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag)
            .OfType<GrantedPathEntry>()
            .ToList();
        Assert.Contains(specificEntry, displayedEntries);
        Assert.Contains(sharedEntry, displayedEntries);
    }

    [Fact]
    public void PopulateTraverseGrid_ContainerSharedEntry_UsesAllApplicationPackagesConfigLookup()
    {
        var sharedEntry = new GrantedPathEntry { Path = @"C:\Shared", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.SharedContainerTraverseGrants.Add(sharedEntry);

        var helper = CreateHelper(database, out var grid, out var grantConfigTracker);
        helper.Initialize(grid, ContainerSid, isContainer: true, new AclManagerPendingChanges(), sortHelper: null);

        helper.PopulateTraverseGrid();

        grantConfigTracker.Verify(t => t.GetGrantConfigPath(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            sharedEntry), Times.AtLeastOnce);
    }

    private static AclManagerTraverseHelper CreateHelper(
        AppDatabase database,
        out DataGridView grid,
        out Mock<IGrantConfigTracker> grantConfigTracker)
    {
        grid = new DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewImageColumn { Name = AclManagerGrantsHelper.ColIcon });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TraversePath" });

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([]);
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(s => s.ResolveAccountGroupSids(It.IsAny<string>())).Returns([]);
        grantConfigTracker = new Mock<IGrantConfigTracker>();
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        var pathInfo = new TestFileSystemPathInfo();
        var reparsePointHelper = new Mock<IReparsePointPromptHelper>();
        var traverseOperations = new AclManagerTraverseOperations(
            databaseProvider,
            reparsePointHelper.Object,
            aclPermission.Object,
            pathInfo);
        var resolver = new TraverseEntryResolver(
            aclPermission.Object,
            new Mock<ITraverseAcl>().Object,
            pathInfo);
        var rowBuilder = new AclManagerTraverseRowBuilder(
            new Mock<IAclPathIconProvider>().Object,
            resolver);

        return new AclManagerTraverseHelper(
            appConfigService.Object,
            aclPermission.Object,
            grantConfigTracker.Object,
            databaseProvider,
            traverseOperations,
            rowBuilder);
    }
}
