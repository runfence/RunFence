namespace RunFence.Account.UI;

public readonly record struct AuthenticodeVerificationResult(
    int TrustResult,
    byte[]? SignerCertificateData);
