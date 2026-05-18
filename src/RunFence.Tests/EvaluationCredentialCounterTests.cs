using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class EvaluationCredentialCounterTests
{
    [Fact]
    public void CountCredentialsExcludingCurrent_RemovesCurrentAccountEntries()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var counter = new EvaluationCredentialCounter();

        var result = counter.CountCredentialsExcludingCurrent(new[]
        {
            new CredentialEntry { Sid = currentSid },
            new CredentialEntry { Sid = "S-1-5-21-100" },
            new CredentialEntry { Sid = currentSid },
            new CredentialEntry { Sid = "S-1-5-21-200" }
        });

        Assert.Equal(2, result);
    }

    [Fact]
    public void CountCredentialsExcludingCurrent_WithOnlyCurrentEntries_ReturnsZero()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var counter = new EvaluationCredentialCounter();

        var result = counter.CountCredentialsExcludingCurrent(new[]
        {
            new CredentialEntry { Sid = currentSid },
            new CredentialEntry { Sid = currentSid }
        });

        Assert.Equal(0, result);
    }
}
