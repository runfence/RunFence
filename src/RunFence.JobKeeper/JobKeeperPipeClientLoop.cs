using System.IO.Pipes;
using RunFence.Core;

namespace RunFence.JobKeeper;

internal sealed class JobKeeperPipeClientLoop(
    JobKeeperStartupOptions options,
    IJobKeeperRequestHandler requestHandler,
    IJobKeeperLifetimeController lifetimeController)
{
    private const int ConnectTimeoutMilliseconds = 1_000;
    private const int ReadPollTimeoutMilliseconds = 1_000;

    public bool RunOnce()
    {
        if (lifetimeController.ShouldExit())
            return false;

        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(ConnectTimeoutMilliseconds);
            }
            catch
            {
                pipe.Dispose();
                Thread.Sleep(500);
                return !lifetimeController.ShouldExit();
            }

            return RunLoop(pipe);
        }
        catch
        {
        }
        finally
        {
            pipe?.Dispose();
        }

        return !lifetimeController.ShouldExit();
    }

    private bool RunLoop(NamedPipeClientStream pipe)
    {
        Task<JobKeeperLaunchRequest?>? pendingRead = null;
        while (true)
        {
            pendingRead ??= Task.Run(() => JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(pipe));
            if (Task.WaitAny([pendingRead], ReadPollTimeoutMilliseconds) != 0)
            {
                if (!lifetimeController.ShouldExit())
                    continue;

                pipe.Dispose();
                ObservePendingReadCompletion(pendingRead);
                return false;
            }

            JobKeeperLaunchRequest? request;
            try
            {
                request = pendingRead.GetAwaiter().GetResult();
            }
            catch (IOException)
            {
                return !lifetimeController.ShouldExit();
            }
            finally
            {
                pendingRead = null;
            }

            if (request == null)
                return !lifetimeController.ShouldExit();

            lifetimeController.RecordRequestArrival();
            JobKeeperProtocol.WriteMessage(pipe, requestHandler.Handle(request));
        }
    }

    private static void ObservePendingReadCompletion(Task<JobKeeperLaunchRequest?> pendingRead)
    {
        try
        {
            pendingRead.Wait(1_000);
        }
        catch
        {
        }
    }
}
