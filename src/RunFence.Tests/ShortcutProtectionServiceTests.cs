using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ShortcutProtectionServiceTests
{
    [Fact]
    public void ProtectInternalShortcut_RemovesEveryoneDenyAce()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var log = new Mock<ILoggingService>();
            var aclAccessor = new Mock<IAclAccessor>();
            var service = new ShortcutProtectionService(log.Object, aclAccessor.Object);

            // Apply regular protection -- adds Everyone Deny ACE
            service.ProtectShortcut(tempFile);

            // Confirm Everyone Deny exists after regular protection
            var fileInfo1 = new FileInfo(tempFile);
            var security1 = fileInfo1.GetAccessControl();
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            bool hasEveryoneDenyBefore = false;
            foreach (FileSystemAccessRule rule in security1.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Deny &&
                    rule.IdentityReference is SecurityIdentifier sid1 &&
                    sid1.Equals(everyoneSid))
                {
                    hasEveryoneDenyBefore = true;
                    break;
                }
            }
            Assert.True(hasEveryoneDenyBefore, "Everyone Deny should exist after ProtectShortcut");

            // Act: apply internal protection
            const string accountSid = "S-1-5-21-111-222-333-1001";
            service.ProtectInternalShortcut(tempFile, accountSid);

            // Assert: Everyone Deny should be removed
            var fileInfo2 = new FileInfo(tempFile);
            var security2 = fileInfo2.GetAccessControl();
            bool hasEveryoneDenyAfter = false;
            foreach (FileSystemAccessRule rule in security2.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Deny &&
                    rule.IdentityReference is SecurityIdentifier sid2 &&
                    sid2.Equals(everyoneSid))
                {
                    hasEveryoneDenyAfter = true;
                    break;
                }
            }
            Assert.False(hasEveryoneDenyAfter, "Everyone Deny ACE should be removed by ProtectInternalShortcut");

            // Assert: account has Allow access
            bool hasAccountAllow = false;
            var accountIdentity = new SecurityIdentifier(accountSid);
            foreach (FileSystemAccessRule rule in security2.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference is SecurityIdentifier sid3 &&
                    sid3.Equals(accountIdentity))
                {
                    hasAccountAllow = true;
                    break;
                }
            }
            Assert.True(hasAccountAllow, "Account should have Allow access after ProtectInternalShortcut");
        }
        finally
        {
            try
            {
                File.SetAttributes(tempFile, FileAttributes.Normal);
                File.Delete(tempFile);
            }
            catch { }
        }
    }
}
