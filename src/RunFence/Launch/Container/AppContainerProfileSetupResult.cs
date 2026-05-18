namespace RunFence.Launch.Container;

public sealed record AppContainerProfileSetupResult(
    AppContainerProfileSetupStatus Status,
    bool ProfileCreatedOrAlreadyExists,
    bool ShellFolderRedirectsWritten,
    bool EnvironmentRewritten,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage)
{
    public static AppContainerProfileSetupResult Success(
        bool profileCreatedOrAlreadyExists = false,
        bool shellFolderRedirectsWritten = false,
        bool environmentRewritten = false,
        IReadOnlyList<string>? warnings = null)
        => new(
            AppContainerProfileSetupStatus.Succeeded,
            profileCreatedOrAlreadyExists,
            shellFolderRedirectsWritten,
            environmentRewritten,
            warnings ?? Array.Empty<string>(),
            null);

    public static AppContainerProfileSetupResult Failure(
        AppContainerProfileSetupStatus status,
        string errorMessage,
        bool profileCreatedOrAlreadyExists = false,
        bool shellFolderRedirectsWritten = false,
        bool environmentRewritten = false,
        IReadOnlyList<string>? warnings = null)
        => new(
            status,
            profileCreatedOrAlreadyExists,
            shellFolderRedirectsWritten,
            environmentRewritten,
            warnings ?? Array.Empty<string>(),
            errorMessage);
}
