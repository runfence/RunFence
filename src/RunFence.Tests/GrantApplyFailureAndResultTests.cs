using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public class GrantApplyFailureAndResultTests
{
    private sealed record FormatterExpectation(string Description, bool IsSaveFailureStep);

    private static readonly IReadOnlyDictionary<GrantApplyFailureStep, FormatterExpectation> ExpectedSteps =
        new Dictionary<GrantApplyFailureStep, FormatterExpectation>
        {
            { GrantApplyFailureStep.GrantIntentSave, new("Failed to save the grant intent", true) },
            { GrantApplyFailureStep.TraverseIntentSave, new("Failed to save the traverse grant intent", true) },
            { GrantApplyFailureStep.GrantAclApply, new("Failed to apply target ACL", false) },
            { GrantApplyFailureStep.TraverseAclApply, new("Failed to apply traverse ACL", false) },
            { GrantApplyFailureStep.TargetEffectiveAccessValidation, new("Failed to validate target effective access", false) },
            { GrantApplyFailureStep.TraverseEffectiveAccessValidation, new("Failed to validate traverse effective access", false) },
            { GrantApplyFailureStep.GrantAclRemove, new("Failed to remove target ACL", false) },
            { GrantApplyFailureStep.TraverseAclRemove, new("Failed to remove traverse ACL", false) },
            { GrantApplyFailureStep.DenyConflictGrantAclRemove, new("Failed to remove deny-conflict target ACL", false) },
            { GrantApplyFailureStep.DenyConflictGrantAclApply, new("Failed to apply deny-conflict target ACL", false) },
            { GrantApplyFailureStep.DenyConflictPostRemoveSave, new("Failed to save deny-conflict state after target ACL removal", true) },
            { GrantApplyFailureStep.DenyConflictPostUpdateSave, new("Failed to save deny-conflict state after target ACL update", true) },
            { GrantApplyFailureStep.DenyConflictRollback, new("Failed to rollback deny-conflict change", false) },
            { GrantApplyFailureStep.RemoveAllGrantAclRemove, new("Failed to remove all target ACLs", false) },
            { GrantApplyFailureStep.RemoveAllTraverseAclRemove, new("Failed to remove all traverse ACLs", false) },
            { GrantApplyFailureStep.PostGrantMutationSave, new("Failed to save state after grant mutation", true) },
            { GrantApplyFailureStep.PostGrantRemoveSave, new("Failed to save state after target ACL removal", true) },
            { GrantApplyFailureStep.PostTraverseRemoveSave, new("Failed to save state after traverse ACL removal", true) },
            { GrantApplyFailureStep.PostRemoveAllSave, new("Failed to save state after complete grant removal", true) },
            { GrantApplyFailureStep.UntrackGrantSave, new("Failed to save grant untrack state", true) },
            { GrantApplyFailureStep.UntrackTraverseSave, new("Failed to save traverse untrack state", true) },
            { GrantApplyFailureStep.UntrackAllSave, new("Failed to save untrack-all state", true) },
            { GrantApplyFailureStep.RevertIntentSave, new("Failed to save revert intent", true) },
            { GrantApplyFailureStep.GrantAclRollback, new("Failed to rollback target ACL", false) },
            { GrantApplyFailureStep.TraverseAclRollback, new("Failed to rollback traverse ACL", false) },
            { GrantApplyFailureStep.FixGrantAclApply, new("Failed to re-apply fixed target ACL", false) },
            { GrantApplyFailureStep.FixTraverseAclApply, new("Failed to re-apply fixed traverse ACL", false) }
        };

    [Fact]
    public void GrantApplyResult_DefaultWarningsAreEmpty()
    {
        var result = default(GrantApplyResult);

        Assert.Empty(result.Warnings);
        Assert.False(result.GrantApplied);
        Assert.False(result.TraverseApplied);
        Assert.False(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
    }

    [Fact]
    public void GrantApplyResult_PreservesConstructedWarnings()
    {
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\apps\MyApp.exe",
            @"C:\configs\main.rfc",
            new InvalidOperationException("save warning"));
        var result = new GrantApplyResult(
            GrantApplied: true,
            TraverseApplied: true,
            DatabaseModified: true,
            DurableSaveCompleted: false,
            Warnings: [warning]);

        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal([warning], result.Warnings);
    }

    [Fact]
    public void GrantApplyWarning_Format_UsesFormatter()
    {
        var cause = new InvalidOperationException("save warning");
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\apps\MyApp.exe",
            @"C:\configs\main.rfc",
            cause);

        Assert.Equal(
            GrantApplyFailureFormatter.Format(
                GrantApplyFailureStep.PostGrantMutationSave,
                @"C:\apps\MyApp.exe",
                @"C:\configs\main.rfc",
                cause),
            GrantApplyFailureFormatter.Format(warning));
    }

    [Theory]
    [MemberData(nameof(GetExpectedDescriptions))]
    public void GrantApplyFailureFormatter_DescribeStep_ReturnsOwnedDescription(
        GrantApplyFailureStep step,
        string expectedDescription)
    {
        Assert.Equal(expectedDescription, GrantApplyFailureFormatter.DescribeStep(step));
    }

    [Fact]
    public void GrantApplyFailureFormatter_IsSaveFailureStep_MatchesOwnedClassification()
    {
        foreach (var step in Enum.GetValues<GrantApplyFailureStep>())
            Assert.Equal(ExpectedSteps[step].IsSaveFailureStep, GrantApplyFailureFormatter.IsSaveFailureStep(step));
    }

    [Theory]
    [MemberData(nameof(GetExpectedDescriptions))]
    public void GrantApplyFailureFormatter_Format_CanFormatEveryStep(
        GrantApplyFailureStep step,
        string expectedDescription)
    {
        var cause = new IOException("i/o problem");
        var withConfigPath = GrantApplyFailureFormatter.Format(step, @"C:\target", @"C:\configs\batch.rfc", cause);
        var withoutConfigPath = GrantApplyFailureFormatter.Format(step, @"C:\target", null, cause);

        Assert.Contains(expectedDescription, withConfigPath);
        Assert.Contains("C:\\target", withConfigPath);
        Assert.Contains("'C:\\configs\\batch.rfc'", withConfigPath);
        Assert.Contains("i/o problem", withConfigPath);
        Assert.Contains(expectedDescription, withoutConfigPath);
        Assert.Contains("C:\\target", withoutConfigPath);
        Assert.Contains("main config", withoutConfigPath);
        Assert.Contains("i/o problem", withoutConfigPath);
    }

    [Fact]
    public void GrantApplyFailureFormatter_UnknownStep_UsesIntentionalFallback()
    {
        var undefinedStep = (GrantApplyFailureStep)999;

        Assert.Equal("Failure in 999", GrantApplyFailureFormatter.DescribeStep(undefinedStep));
        Assert.Contains(
            "Failure in 999",
            GrantApplyFailureFormatter.Format(
                undefinedStep,
                @"C:\target",
                null,
                new InvalidOperationException("unknown")));
    }

    [Fact]
    public void GrantApplyFailure_ToString_UsesFormatter()
    {
        var cause = new InvalidOperationException("access denied");
        var failure = new GrantApplyFailure(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\apps\MyApp.exe",
            @"C:\configs\main.rfc",
            cause);

        var expected = GrantApplyFailureFormatter.Format(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\apps\MyApp.exe",
            @"C:\configs\main.rfc",
            cause);

        Assert.Equal(expected, failure.ToString());
    }

    [Fact]
    public void GrantOperationException_Message_UsesFormatterAndPreservesCause()
    {
        var cause = new UnauthorizedAccessException("cannot update acl");
        var exception = new GrantOperationException(
            GrantApplyFailureStep.TraverseAclApply,
            @"C:\target",
            @"C:\configs\main.rfc",
            cause);

        Assert.Same(cause, exception.Cause);
        Assert.Same(cause, exception.InnerException);
        Assert.Equal(
            GrantApplyFailureFormatter.Format(
                GrantApplyFailureStep.TraverseAclApply,
                @"C:\target",
                @"C:\configs\main.rfc",
                cause),
            exception.Message);
    }

    [Fact]
    public void GrantOperationException_ToString_IncludesPrimaryAndCleanupAndStack()
    {
        var primary = new IOException("primary");
        var cleanup1 = new IOException("cleanup-1");
        var cleanup2 = new UnauthorizedAccessException("cleanup-2");

        var ex = new GrantOperationException(
            GrantApplyFailureStep.GrantIntentSave,
            @"C:\target",
            @"C:\configs\main.rfc",
            primary);
        ex.AppendCleanupFailure(new GrantApplyFailure(
            GrantApplyFailureStep.RevertIntentSave,
            @"C:\target\cleanup",
            null,
            cleanup1));
        ex.AppendCleanupFailure(
            GrantApplyFailureStep.DenyConflictRollback,
            @"C:\target",
            @"C:\configs\main.rfc",
            cleanup2);

        var message = ex.ToString();

        Assert.Contains("Grant operation failure:", message);
        Assert.Contains(GrantApplyFailureFormatter.Format(
            GrantApplyFailureStep.GrantIntentSave,
            @"C:\target",
            @"C:\configs\main.rfc",
            primary), message);
        Assert.Contains("Cleanup failures:", message);
        Assert.Contains(GrantApplyFailureFormatter.Format(
            GrantApplyFailureStep.RevertIntentSave,
            @"C:\target\cleanup",
            null,
            cleanup1), message);
        Assert.Contains(GrantApplyFailureFormatter.Format(
            GrantApplyFailureStep.DenyConflictRollback,
            @"C:\target",
            @"C:\configs\main.rfc",
            cleanup2), message);
        Assert.Contains(ex.GetType().Name, message);
    }

    public static TheoryData<GrantApplyFailureStep, string> GetExpectedDescriptions()
    {
        var data = new TheoryData<GrantApplyFailureStep, string>();
        foreach (var pair in ExpectedSteps)
            data.Add(pair.Key, pair.Value.Description);

        return data;
    }
}
