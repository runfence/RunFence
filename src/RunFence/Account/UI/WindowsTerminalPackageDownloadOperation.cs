namespace RunFence.Account.UI;

public readonly record struct WindowsTerminalPackageDownloadOperation(
    string DownloadUrl,
    string DestinationPath);
