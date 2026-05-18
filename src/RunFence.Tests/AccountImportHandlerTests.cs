using Moq;
using RunFence.Core;
using RunFence.Account;
using RunFence.Core.Models;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class AccountImportHandlerTests : IDisposable
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";

    private readonly TempDirectory _tempDir = new("AccountImportHandler");
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly Mock<ICredentialDecryptionService> _credentialDecryption = new();
    private readonly Mock<ILoggingService> _log = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public async Task RunImportAsync_DatabaseModified_DoesNotSaveConfigAgain()
    {
        var settingsPath = Path.Combine(_tempDir.Path, "settings.json");
        File.WriteAllText(settingsPath, "{}");

        _credentialDecryption
            .Setup(d => d.CheckCredential(TestSid, It.IsAny<CredentialStore>()))
            .Returns(CredentialLookupStatus.Success);
        _settingsTransferService
            .Setup(s => s.Import(settingsPath, TestSid, It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(true, "", DatabaseModified: true));

        var handler = new AccountImportHandler(
            _settingsTransferService.Object,
            _credentialDecryption.Object,
            _log.Object);

        var sink = new Mock<IImportProgressSink>();
        sink.Setup(s => s.SelectFile()).Returns(settingsPath);

        var account = new ImportAccount(new CredentialEntry { Sid = TestSid }, "testuser");
        var result = await handler.RunImportAsync(
            [account],
            new CredentialStore(),
            sink.Object);

        Assert.Equal(settingsPath, result);
    }
}
