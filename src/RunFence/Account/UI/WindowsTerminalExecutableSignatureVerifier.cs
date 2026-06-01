using System.Security.Cryptography.X509Certificates;

namespace RunFence.Account.UI;

public interface IWindowsTerminalExecutableSignatureVerifier
{
    void VerifyMicrosoftSignedExecutable(string executablePath);
}

public sealed class WindowsTerminalExecutableSignatureVerifier : IWindowsTerminalExecutableSignatureVerifier
{
    private const int TrustSuccess = 0;

    public void VerifyMicrosoftSignedExecutable(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        var verification = AuthenticodeNative.VerifyFile(executablePath);
        if (verification.TrustResult != TrustSuccess)
        {
            throw new InvalidOperationException(
                $"{fileName} Authenticode verification failed with 0x{verification.TrustResult:X8}.");
        }

        if (verification.SignerCertificateData == null)
            throw new InvalidOperationException($"{fileName} signer certificate could not be read.");

        using var signerCertificate = X509CertificateLoader.LoadCertificate(verification.SignerCertificateData);
        if (!IsMicrosoftPublisher(signerCertificate))
        {
            throw new InvalidOperationException(
                $"{fileName} is signed by '{signerCertificate.GetNameInfo(X509NameType.SimpleName, false)}', not Microsoft Corporation.");
        }
    }

    private static bool IsMicrosoftPublisher(X509Certificate2 certificate)
        => string.Equals(
               certificate.GetNameInfo(X509NameType.SimpleName, false),
               "Microsoft Corporation",
               StringComparison.OrdinalIgnoreCase);
}
