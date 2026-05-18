using RunFence.Account;
using Xunit;

namespace RunFence.Tests;

public class AccountRestrictionEntryFormatterTests
{
    [Fact]
    public void Format_UsesExplicitErrorWhenPresent()
    {
        var entry = new AccountRestrictionEntry(
            AccountRestrictionKind.NetworkLogin,
            AccountRestrictionStatus.RolledBack,
            true,
            "denied by policy");

        var result = AccountRestrictionEntryFormatter.Format(entry);

        Assert.Equal("NetworkLogin: denied by policy", result);
    }

    [Fact]
    public void Format_FallsBackToStatusWhenErrorMissing()
    {
        var entry = new AccountRestrictionEntry(
            AccountRestrictionKind.HideLogon,
            AccountRestrictionStatus.Failed,
            false,
            null);

        var result = AccountRestrictionEntryFormatter.Format(entry);

        Assert.Equal("HideLogon: Failed", result);
    }
}
