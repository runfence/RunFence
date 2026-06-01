using RunFence.Launching.Processes;

namespace RunFence.AppxLauncher;

public sealed class AppxTargetProcessQuery(
    IProcessImageNameSnapshotReader processImageNameReader,
    IProcessExecutablePathReader processPathReader,
    IProcessOwnerInfoReader processOwnerReader) : IAppxTargetProcessQuery
{
    public IReadOnlyList<AppxTargetProcessInfo> GetTargetProcesses(string executablePath)
    {
        var targetFileName = Path.GetFileName(executablePath);
        if (string.IsNullOrWhiteSpace(targetFileName))
            return [];

        var targetPath = TryNormalizePath(executablePath);
        if (targetPath == null)
            return [];

        var matches = new List<AppxTargetProcessInfo>();
        foreach (var process in processImageNameReader.GetProcessesByImageName(targetFileName))
        {
            var processImagePath = processPathReader.GetExecutablePath(process.ProcessId);
            if (processImagePath == null ||
                !string.Equals(TryNormalizePath(processImagePath), targetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            matches.Add(new AppxTargetProcessInfo(
                process.ProcessId,
                process.CreationTimeUtcTicks.HasValue ? new DateTime(process.CreationTimeUtcTicks.Value, DateTimeKind.Utc) : null,
                processImagePath));
        }

        return matches;
    }

    public ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid) =>
        processOwnerReader.GetProcessOwner(processId, expectedOwnerSid);

    private static string? TryNormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
