using Moq;
using RunFence.Account;

namespace RunFence.Tests;

/// <summary>
/// Shared assertion helpers for verifying restriction service mock calls
/// with the correct SID. Eliminates repeated Verify patterns across Account test files.
/// </summary>
public static class RestrictionMockVerifyExtensions
{
    extension(Mock<IAccountLsaRestrictionService> mock)
    {
        /// <summary>
        /// Verifies that <see cref="IAccountLsaRestrictionService.SetLocalOnlyBySid"/> was called
        /// exactly once with the expected SID and value.
        /// </summary>
        public void VerifySetLocalOnly(string expectedSid, bool expectedValue)
            => mock.Verify(r => r.SetLocalOnlyBySid(expectedSid, expectedValue), Times.Once);

        /// <summary>
        /// Verifies that <see cref="IAccountLsaRestrictionService.SetLocalOnlyBySid"/> was never called.
        /// </summary>
        public void VerifySetLocalOnlyNeverCalled()
            => mock.Verify(r => r.SetLocalOnlyBySid(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

        /// <summary>
        /// Verifies that <see cref="IAccountLsaRestrictionService.SetNoBgAutostartBySid"/> was called
        /// exactly once with the expected SID and value.
        /// </summary>
        public void VerifySetNoBgAutostart(string expectedSid, bool expectedValue)
            => mock.Verify(r => r.SetNoBgAutostartBySid(expectedSid, expectedValue), Times.Once);

        /// <summary>
        /// Verifies that <see cref="IAccountLsaRestrictionService.SetNoBgAutostartBySid"/> was never called.
        /// </summary>
        public void VerifySetNoBgAutostartNeverCalled()
            => mock.Verify(r => r.SetNoBgAutostartBySid(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    extension(Mock<IAccountLoginRestrictionService> mock)
    {
        /// <summary>
        /// Verifies that <see cref="IAccountLoginRestrictionService.SetLoginBlockedBySid"/> was called
        /// exactly once with the expected SID, username, and value.
        /// </summary>
        public void VerifySetLoginBlocked(string expectedSid, string expectedUsername, bool expectedValue)
            => mock.Verify(r => r.SetLoginBlockedBySid(expectedSid, expectedUsername, expectedValue), Times.Once);

        /// <summary>
        /// Verifies that <see cref="IAccountLoginRestrictionService.SetLoginBlockedBySid"/> was never called.
        /// </summary>
        public void VerifySetLoginBlockedNeverCalled()
            => mock.Verify(r => r.SetLoginBlockedBySid(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
