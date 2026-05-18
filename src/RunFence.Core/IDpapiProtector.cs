namespace RunFence.Core;

public interface IDpapiProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy);
    SecureSecret UnprotectToSecret(byte[] protectedData, ReadOnlySpan<byte> entropy);
    ProtectedString UnprotectToProtectedString(byte[] protectedData, ReadOnlySpan<byte> entropy);
}
