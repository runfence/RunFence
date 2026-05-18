using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public class AccountListPresenterTests
{
    [Fact]
    public void NextGeneration_AdvancesAndMarksPreviousAsStale()
    {
        var presenter = new AccountListPresenter();
        var g1 = presenter.NextGeneration();
        var g2 = presenter.NextGeneration();

        Assert.True(g2 > g1);
        Assert.False(presenter.IsCurrent(g1));
        Assert.True(presenter.IsCurrent(g2));
    }
}
