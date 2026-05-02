namespace RunFence.Account.UI;

internal interface ISecurePasswordPasteSource
{
    /// <summary>Returns '\0' at the end of the paste source.</summary>
    char ReadChar(int charIndex);
}
