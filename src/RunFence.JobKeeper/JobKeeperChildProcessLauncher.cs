using System.Text;
using RunFence.Core;

namespace RunFence.JobKeeper;

public sealed class JobKeeperChildProcessLauncher(
    IJobKeeperExecutablePathResolver executablePathResolver,
    IJobKeeperEnvironmentSnapshotReader environmentSnapshotReader,
    IJobKeeperEnvironmentBlockFactory environmentBlockFactory,
    IJobKeeperNativeProcessApi nativeProcessApi) : IJobKeeperChildProcessLauncher
{
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public JobKeeperLaunchResponse Launch(JobKeeperLaunchRequest request)
    {
        var environment = environmentSnapshotReader.ReadAll();
        if (request.EnvOverrides != null)
        {
            foreach (var (key, value) in request.EnvOverrides)
                environment[key] = value;
        }

        var exePath = executablePathResolver.Resolve(request.ExePath, environment);

        var cmdLine = new StringBuilder();
        cmdLine.Append('"').Append(exePath).Append('"');
        if (!string.IsNullOrEmpty(request.Arguments))
            cmdLine.Append(' ').Append(request.Arguments);

        var envBlock = environmentBlockFactory.Build(environment);

        try
        {
            if (!nativeProcessApi.CreateProcess(
                    cmdLine,
                    CreateUnicodeEnvironment,
                    envBlock,
                    request.WorkingDirectory,
                    request.HideWindow,
                    out var processInfo))
            {
                return new JobKeeperLaunchResponse(0, nativeProcessApi.GetLastWin32Error());
            }

            nativeProcessApi.CloseHandle(processInfo.ThreadHandle);
            nativeProcessApi.CloseHandle(processInfo.ProcessHandle);
            return new JobKeeperLaunchResponse((int)processInfo.ProcessId, 0);
        }
        finally
        {
            environmentBlockFactory.Free(envBlock);
        }
    }
}
