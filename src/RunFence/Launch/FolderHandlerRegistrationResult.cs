namespace RunFence.Launch;

public sealed record class FolderHandlerRegistrationResult(IReadOnlyList<string> Warnings)
{
    public FolderHandlerRegistrationResult() : this([])
    {
    }
}

