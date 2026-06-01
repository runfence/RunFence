namespace RunFence.Infrastructure;

public sealed class SaveFileDialogAdapterFactory : ISaveFileDialogAdapterFactory
{
    public ISaveFileDialogAdapter Create()
        => new SaveFileDialogAdapter();
}
