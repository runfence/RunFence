using RunFence.Launching.Processes;

namespace RunFence.AppxLauncher;

public interface IAppxTargetProcessQuery
{
    IReadOnlyList<AppxTargetProcessInfo> GetTargetProcesses(string executablePath);

    ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid);
}
