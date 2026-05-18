using RunFence.Acl.UI;
using Xunit;

namespace RunFence.Tests;

public class AclBulkScanWarningMessageTests
{
    [Fact]
    public void BuildSkippedConflictWarningMessage_NoSkippedPaths_ReturnsNull()
    {
        var summary = new AclBulkScanImportSummary(0, 0, []);

        var message = AclBulkScanWarningMessage.BuildSkippedConflictWarningMessage(summary);

        Assert.Null(message);
    }

    [Fact]
    public void BuildSkippedConflictWarningMessage_SkippedPaths_ReturnsWarningText()
    {
        var summary = new AclBulkScanImportSummary(0, 0, [@"C:\data", @"D:\apps"]);

        var message = AclBulkScanWarningMessage.BuildSkippedConflictWarningMessage(summary);

        Assert.Equal(
            "Some scanned ACL entries were skipped because the account already has the opposite grant mode for that path.\n\nC:\\data\nD:\\apps",
            message);
    }
}
