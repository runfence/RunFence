using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclManagerTraverseRowBuilderTests
{
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string TraversePath = @"C:\Existing\ContainerTraverseRow";

    [Fact]
    public void AddTrackedTraverseRow_SpecificContainerSid_AllApplicationPackagesEffective_DoesNotMarkYellow()
    {
        var pathInfo = new TestFileSystemPathInfo().AddDirectory(TraversePath);
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                ContainerSid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(false);
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var resolver = new TraverseEntryResolver(
            aclPermission.Object,
            new Mock<ITraverseAcl>().Object,
            pathInfo);
        var rowBuilder = new AclManagerTraverseRowBuilder(
            new Mock<IAclPathIconProvider>().Object,
            resolver);
        using var grid = CreateTraverseGrid();
        var pending = new AclManagerPendingChanges();
        rowBuilder.Initialize(grid, ContainerSid, pending, []);
        var entry = new GrantedPathEntry
        {
            Path = TraversePath,
            IsTraverseOnly = true,
            AllAppliedPaths = [TraversePath]
        };

        rowBuilder.AddTrackedTraverseRow(entry);

        var row = Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.NotEqual(Color.LightYellow, row.DefaultCellStyle.BackColor);
        Assert.Empty(rowBuilder.FixableEntries);
    }

    private static DataGridView CreateTraverseGrid()
    {
        var grid = new DataGridView { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewImageColumn { Name = AclManagerGrantsHelper.ColIcon });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TraversePath" });
        return grid;
    }
}
