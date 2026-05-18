using System.Security.Cryptography;
using Moq;
using RunFence.Core;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class ConfigMismatchPinVerifierTests
{
    private static readonly byte[] FileSalt = Enumerable.Repeat((byte)0x5A, 32).ToArray();

    private readonly Mock<IPinService> _pinService = new();

    private ConfigMismatchPinVerifier CreateVerifier()
        => new(_pinService.Object);

    [Fact]
    public void VerifyTemporary_WrongPin_ReturnsWrongPin()
    {
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Returns(() => TestSecretFactory.Create(32));

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        using var result = CreateVerifier().VerifyTemporary(
            pin,
            FileSalt,
            _ => throw new CryptographicException("wrong pin"));

        Assert.Equal(ConfigMismatchPinVerificationResult.StatusKind.WrongPin, result.Status);
        Assert.Null(result.FatalException);
    }

    [Fact]
    public void VerifyTemporary_FatalException_ReturnsAbortToRecovery()
    {
        var fatal = new InvalidOperationException("boom");
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Returns(() => TestSecretFactory.Create(32));

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        using var result = CreateVerifier().VerifyTemporary(
            pin,
            FileSalt,
            _ => throw fatal);

        Assert.Equal(ConfigMismatchPinVerificationResult.StatusKind.AbortToRecovery, result.Status);
        Assert.Same(fatal, result.FatalException);
    }

    [Fact]
    public void VerifyTemporary_KeyDerivationCryptographicFailure_ReturnsAbortToRecovery()
    {
        var fatal = new CryptographicException("derive failed");
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Throws(fatal);

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        using var result = CreateVerifier().VerifyTemporary(pin, FileSalt, _ => { });

        Assert.Equal(ConfigMismatchPinVerificationResult.StatusKind.AbortToRecovery, result.Status);
        Assert.Same(fatal, result.FatalException);
    }

    [Fact]
    public void VerifyAndReturnKey_TakeVerifiedKeyTransfersOwnershipExactlyOnce()
    {
        var verifiedBytes = Enumerable.Repeat((byte)0x33, 32).ToArray();
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Returns(() => TestSecretFactory.FromBytes(verifiedBytes));

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        using var result = CreateVerifier().VerifyAndReturnKey(pin, FileSalt, _ => { });

        Assert.Equal(ConfigMismatchPinVerificationResult.StatusKind.VerifiedWithReturnedKey, result.Status);

        using var key = result.TakeVerifiedKey("already taken");
        Assert.Equal(verifiedBytes, key.TransformSnapshot(data => data.ToArray()));
        Assert.Throws<InvalidOperationException>(() => result.TakeVerifiedKey("already taken"));
    }

    [Fact]
    public void VerifyAndReturnKey_ReturnedKeyRemainsUsableAfterResultDispose()
    {
        var verifiedBytes = Enumerable.Repeat((byte)0x44, 32).ToArray();
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Returns(() => TestSecretFactory.FromBytes(verifiedBytes));

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        SecureSecret returnedKey;
        using (var result = CreateVerifier().VerifyAndReturnKey(pin, FileSalt, _ => { }))
        {
            returnedKey = result.TakeVerifiedKey("already taken");
        }

        using (returnedKey)
        {
            Assert.Equal(verifiedBytes, returnedKey.TransformSnapshot(data => data.ToArray()));
        }
    }

    [Fact]
    public void VerifyTemporary_Success_DisposesTemporaryKeyBeforeReturning()
    {
        var verifiedBytes = Enumerable.Repeat((byte)0x55, 32).ToArray();
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), FileSalt))
            .Returns(() => TestSecretFactory.FromBytes(verifiedBytes));
        ISecureSecretSnapshotSource? observedKey = null;

        using var pin = ProtectedString.FromChars("1234".AsSpan());
        using var result = CreateVerifier().VerifyTemporary(
            pin,
            FileSalt,
            key =>
            {
                observedKey = key;
                Assert.Equal(verifiedBytes, key.TransformSnapshot(data => data.ToArray()));
            });

        Assert.Equal(ConfigMismatchPinVerificationResult.StatusKind.VerifiedTemporaryOnly, result.Status);
        Assert.NotNull(observedKey);
        Assert.Throws<ObjectDisposedException>(() => observedKey!.TransformSnapshot(data => data.ToArray()));
    }
}
