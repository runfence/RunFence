namespace RunFence.Launch;

public sealed record AssociationResolutionRequest(
    AssociationLaunchKind Kind,
    string RawArgument,
    ProcessLaunchTarget? FileTarget,
    string? Extension)
{
    public ProcessLaunchTarget GetFallbackTarget()
        => Kind == AssociationLaunchKind.File
            ? ProcessLaunchHelper.WrapForShellLaunch(FileTarget!)
            : ProcessLaunchHelper.BuildUrlLaunchTarget(RawArgument);

    public static AssociationResolutionRequest ForFile(ProcessLaunchTarget originalTarget, string? extension)
        => new(AssociationLaunchKind.File, originalTarget.ExePath, originalTarget, extension);

    public static AssociationResolutionRequest ForUrl(string url)
        => new(AssociationLaunchKind.Url, url, null, null);
}

public enum AssociationLaunchKind
{
    File,
    Url
}
