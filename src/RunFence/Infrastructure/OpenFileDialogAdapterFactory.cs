namespace RunFence.Infrastructure;

public sealed class OpenFileDialogAdapterFactory : IOpenFileDialogAdapterFactory
{
    public IOpenFileDialogAdapter Create()
        => new OpenFileDialogAdapter();
}
