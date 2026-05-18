namespace RunFence.Infrastructure;

public sealed class FolderBrowserDialogAdapterFactory : IFolderBrowserDialogAdapterFactory
{
    public IFolderBrowserDialogAdapter Create()
        => new FolderBrowserDialogAdapter();
}
