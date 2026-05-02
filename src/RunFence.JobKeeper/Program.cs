using RunFence.Launching.Resolution;

namespace RunFence.JobKeeper;

public static class Program
{
    public static void Main(string[] args)
    {
        var options = ParseOptions(args);
        if (options == null)
            return;

        var launching = JobKeeperLaunchingComposition.CreateProduction();
        var executablePathResolver = new JobKeeperExecutablePathResolver(
            launching.ExecutablePathResolver);
        var nativeProcessApi = new JobKeeperNativeProcessApi();
        var childProcessLauncher = new JobKeeperChildProcessLauncher(
            executablePathResolver,
            new JobKeeperEnvironmentSnapshotReader(nativeProcessApi),
            new JobKeeperEnvironmentBlockFactory(),
            nativeProcessApi);
        var requestHandler = new JobKeeperRequestHandler(childProcessLauncher);
        new JobKeeperRunner(new JobKeeperPipeClientLoop(options, requestHandler)).Run();
    }

    private static JobKeeperStartupOptions? ParseOptions(string[] args)
    {
        if (args.Length != 2
            || !string.Equals(args[0], "--pipe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(args[1]))
        {
            return null;
        }

        return new JobKeeperStartupOptions(args[1]);
    }
}
