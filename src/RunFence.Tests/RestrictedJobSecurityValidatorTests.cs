using System.Security.Principal;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class RestrictedJobSecurityValidatorTests
{
    [Fact]
    public void TryIsPolicyMutableByUntrustedPrincipal_HarmlessReadAceForUsers_ReturnsFalse()
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var snapshot = new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(
                    usersSid,
                    unchecked((int)0x80000000),
                    true),
            ]);

        Assert.False(RestrictedJobSecurityValidator.TryIsPolicyMutableByUntrustedPrincipal(snapshot));
    }

    [Fact]
    public void TryIsPolicyMutableByUntrustedPrincipal_GenericWriteAceForUsers_ReturnsTrue()
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var snapshot = new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(usersSid, 0x40000000, true),
            ]);

        Assert.True(RestrictedJobSecurityValidator.TryIsPolicyMutableByUntrustedPrincipal(snapshot));
    }

    [Fact]
    public void TryIsPolicyMutableByUntrustedPrincipal_IndividualAdministratorAce_ReturnsTrue()
    {
        var adminUserSid = new SecurityIdentifier("S-1-5-21-100-200-300-500");
        var snapshot = new JobObjectSecuritySnapshot(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            true,
            [
                new JobObjectAccessEntry(adminUserSid, 0x00040000, true),
            ]);

        Assert.True(RestrictedJobSecurityValidator.TryIsPolicyMutableByUntrustedPrincipal(snapshot));
    }

    [Fact]
    public void IsTrustedOwner_BuiltinAdministratorsAndLocalSystem_ReturnTrue()
    {
        Assert.True(RestrictedJobSecurityValidator.IsTrustedOwner(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
        Assert.True(RestrictedJobSecurityValidator.IsTrustedOwner(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.False(RestrictedJobSecurityValidator.IsTrustedOwner(
            new SecurityIdentifier("S-1-5-21-100-200-300-500")));
    }
}
