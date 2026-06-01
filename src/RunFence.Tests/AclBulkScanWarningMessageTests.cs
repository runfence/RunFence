using Xunit;
using RunFence.Acl.UI;

namespace RunFence.Tests;

public class AclBulkScanWarningMessageTests
{
    [Fact]
    public void BuildSkippedConflictWarningMessage_ReturnsNull_WhenNoSkippedPaths()
    {
        var summary = new AclBulkScanImportSummary(
            ImportedCount: 1,
            UpdatedCount: 0,
            SkippedOppositeModeConflictPaths: []);

        var message = AclBulkScanWarningMessage.BuildSkippedConflictWarningMessage(summary);

        Assert.Null(message);
    }

    [Fact]
    public void BuildSkippedConflictWarningMessage_IncludesExactTextAndPaths()
    {
        var summary = new AclBulkScanImportSummary(
            ImportedCount: 1,
            UpdatedCount: 0,
            SkippedOppositeModeConflictPaths:
            [
                @"C:\Project\One",
                @"D:\Project\Two"
            ]);

        var message = AclBulkScanWarningMessage.BuildSkippedConflictWarningMessage(summary);

        var expected = "Some scanned ACL entries were skipped because the account already has the opposite grant mode for that path.\n\n" +
                       "C:\\Project\\One\nD:\\Project\\Two";

        Assert.Equal(expected, message);
    }
}
