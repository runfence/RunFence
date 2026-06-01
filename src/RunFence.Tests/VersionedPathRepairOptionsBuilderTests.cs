using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public sealed class VersionedPathRepairOptionsBuilderTests
{
    [Fact]
    public void ForAutomaticRepair_NormalizesBoundaryPath()
    {
        var builder = new VersionedPathRepairOptionsBuilder(Mock.Of<IProfilePathResolver>());

        var options = builder.ForAutomaticRepair(new VersionedPathAutoRepairTrust(@"C:\Program Files\"));

        Assert.Equal([@"C:\Program Files"], options.UnversionedBoundaryPaths);
    }

    [Fact]
    public void ForEditSuggestion_EmptyProfileRoot_IsOmitted()
    {
        var resolver = new Mock<IProfilePathResolver>();
        resolver.Setup(current => current.TryGetProfilePath("S-1-5-21-1")).Returns(string.Empty);
        var builder = new VersionedPathRepairOptionsBuilder(resolver.Object);

        var options = builder.ForEditSuggestion(new AppEntry { AccountSid = "S-1-5-21-1" });

        Assert.Empty(options.UnversionedBoundaryPaths);
    }

    [Fact]
    public void ForEditSuggestion_NormalizesResolvedProfileRoot()
    {
        var resolver = new Mock<IProfilePathResolver>();
        resolver.Setup(current => current.TryGetProfilePath("S-1-5-21-1")).Returns(@"C:\Users\Alice\");
        var builder = new VersionedPathRepairOptionsBuilder(resolver.Object);

        var options = builder.ForEditSuggestion(new AppEntry { AccountSid = "S-1-5-21-1" });

        Assert.Equal([@"C:\Users\Alice"], options.UnversionedBoundaryPaths);
    }

    [Fact]
    public void ForEditSuggestion_NormalizesEquivalentProfileRootToSingleBoundary()
    {
        var resolver = new Mock<IProfilePathResolver>();
        resolver.Setup(current => current.TryGetProfilePath("S-1-5-21-1")).Returns(@"C:\Users\Alice\.\");
        var builder = new VersionedPathRepairOptionsBuilder(resolver.Object);

        var options = builder.ForEditSuggestion(new AppEntry { AccountSid = "S-1-5-21-1" });

        Assert.Single(options.UnversionedBoundaryPaths);
        Assert.Equal(@"C:\Users\Alice", options.UnversionedBoundaryPaths[0]);
    }
}
