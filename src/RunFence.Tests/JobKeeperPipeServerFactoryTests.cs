using System.Security.Principal;
using Moq;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperPipeServerFactoryTests
{
    [Theory]
    [InlineData(JobKeeperIntegrityMode.LowIntegrity, 1)]
    [InlineData(JobKeeperIntegrityMode.Restricted, 0)]
    public void Create_LowIntegrityMode_ControlsMandatoryLabel(
        JobKeeperIntegrityMode mode,
        int expectedCalls)
    {
        var mandatoryLabel = new Mock<IKernelObjectMandatoryLabelService>();
        var factory = new JobKeeperPipeServerFactory(mandatoryLabel.Object);

        using var pipe = factory.Create(
            new JobKeeperInstanceIdentity
            {
                TargetSid = "S-1-5-21-100-200-300-1001",
                ExpectedMode = mode,
                InstanceId = Guid.NewGuid().ToString("N"),
                PipeName = $"RunFenceTest_JobKeeperPipe_{Guid.NewGuid():N}",
                JobName = $"RunFenceTest_JobKeeperJob_{Guid.NewGuid():N}",
            },
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));

        mandatoryLabel.Verify(s => s.ApplyLowIntegrityLabel(It.IsAny<IntPtr>()), Times.Exactly(expectedCalls));
    }
}
