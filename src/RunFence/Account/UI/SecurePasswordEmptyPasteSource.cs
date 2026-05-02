namespace RunFence.Account.UI;

internal sealed class SecurePasswordEmptyPasteSource : ISecurePasswordPasteSource
{
    public static readonly SecurePasswordEmptyPasteSource Instance = new();

    private SecurePasswordEmptyPasteSource()
    {
    }

    public char ReadChar(int charIndex) => '\0';
}
