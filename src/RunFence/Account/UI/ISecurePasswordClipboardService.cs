namespace RunFence.Account.UI;

internal interface ISecurePasswordClipboardService
{
    ISecurePasswordPasteSession? OpenUnicodeText(out bool unicodeTextAvailable);

    bool ShouldSuppressPasswordClipboardWrite();
}
