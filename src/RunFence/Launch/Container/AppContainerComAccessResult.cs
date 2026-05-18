namespace RunFence.Launch.Container;

public sealed record AppContainerComAccessResult(bool Succeeded, string? ErrorMessage)
{
    public static AppContainerComAccessResult Success() => new(true, null);

    public static AppContainerComAccessResult Failure(string errorMessage) => new(false, errorMessage);
}
