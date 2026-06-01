using RunFence.Acl;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class GrantMutationOrderResolverTests
{
    private static readonly SavedRightsState ReadOnly =
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static readonly SavedRightsState ReadExecute =
        new(Execute: true, Write: false, Read: true, Special: false, Own: false);

    private static readonly SavedRightsState ReadWrite =
        new(Execute: false, Write: true, Read: true, Special: false, Own: false);

    private readonly GrantMutationOrderResolver _resolver = new();

    [Fact]
    public void ForRightsChange_AdditiveOnly_ReturnsSaveThenApply()
    {
        var priorEntry = CreateEntry(isDeny: false, ReadOnly);
        var newEntry = CreateEntry(isDeny: false, ReadExecute);

        var result = _resolver.ForRightsChange(priorEntry, newEntry);

        Assert.Equal(GrantMutationOrder.SaveThenApply, result);
    }

    [Fact]
    public void ForRightsChange_RemovalOnly_ReturnsApplyThenSave()
    {
        var priorEntry = CreateEntry(isDeny: false, ReadExecute);
        var newEntry = CreateEntry(isDeny: false, ReadOnly);

        var result = _resolver.ForRightsChange(priorEntry, newEntry);

        Assert.Equal(GrantMutationOrder.ApplyThenSave, result);
    }

    [Fact]
    public void ForRightsChange_MixedRightsUpdate_ReturnsRemoveSaveAdd()
    {
        var priorEntry = CreateEntry(isDeny: false, ReadExecute);
        var newEntry = CreateEntry(isDeny: false, ReadWrite);

        var result = _resolver.ForRightsChange(priorEntry, newEntry);

        Assert.Equal(GrantMutationOrder.RemoveSaveAdd, result);
    }

    [Fact]
    public void ForRightsChange_AllowToDenySwitch_ReturnsRemoveSaveAdd()
    {
        var priorEntry = CreateEntry(isDeny: false, ReadOnly);
        var newEntry = CreateEntry(isDeny: true, ReadOnly);

        var result = _resolver.ForRightsChange(priorEntry, newEntry);

        Assert.Equal(GrantMutationOrder.RemoveSaveAdd, result);
    }

    [Fact]
    public void ForRightsChange_DenyToAllowSwitch_ReturnsRemoveSaveAdd()
    {
        var priorEntry = CreateEntry(isDeny: true, ReadOnly);
        var newEntry = CreateEntry(isDeny: false, ReadOnly);

        var result = _resolver.ForRightsChange(priorEntry, newEntry);

        Assert.Equal(GrantMutationOrder.RemoveSaveAdd, result);
    }

    [Theory]
    [InlineData(true, false, GrantMutationOrder.SaveThenApply)]
    [InlineData(false, true, GrantMutationOrder.ApplyThenSave)]
    [InlineData(true, true, GrantMutationOrder.RemoveSaveAdd)]
    public void ForAclDelta_TraversesExpectedOrdering(
        bool hasAclAdditions,
        bool hasAclRemovals,
        GrantMutationOrder expected)
    {
        var result = _resolver.ForAclDelta(hasAclAdditions, hasAclRemovals);

        Assert.Equal(expected, result);
    }

    private static GrantedPathEntry CreateEntry(bool isDeny, SavedRightsState rights)
        => new()
        {
            Path = @"C:\Test\Path",
            IsDeny = isDeny,
            SavedRights = rights
        };
}
