using RunFence.Acl.UI;
using RunFence.Firewall.UI;
using Xunit;

namespace RunFence.Tests;

public class DialogApplyPresentersTests
{
    [Fact]
    public void AclPresenter_ReturnsFailure_ForFailedApply()
    {
        var presenter = new AclDialogApplyPresenter();
        var result = presenter.Present(applySucceeded: false, changedCount: 0);

        Assert.Equal(DialogApplyPresentationStatus.RenderedFailure, result.Status);
        Assert.True(result.RetainPendingInput);
    }

    [Fact]
    public void FirewallPresenter_ReturnsWarning_WhenRolledBack()
    {
        var presenter = new FirewallDialogApplyPresenter();
        var result = presenter.Present(rolledBack: true, changedSettingsCount: 2);

        Assert.Equal(DialogApplyPresentationStatus.RenderedWarning, result.Status);
        Assert.True(result.RetainPendingInput);
    }
}
