namespace RunFence.Account.UI;

internal readonly record struct SecurePasswordEditResult(
    bool Changed,
    int SelectionStart,
    int SelectionLength);
