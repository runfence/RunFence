using System.IO.Pipes;
using RunFence.Core;

namespace RunFence.JobKeeper;

internal sealed class JobKeeperPipeClientLoop(
    JobKeeperStartupOptions options,
    IJobKeeperRequestHandler requestHandler)
{
    public void RunOnce()
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.None);
            try
            {
                pipe.Connect(1_000);
            }
            catch
            {
                pipe.Dispose();
                Thread.Sleep(500);
                return;
            }

            RunLoop(pipe);
        }
        catch
        {
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private void RunLoop(NamedPipeClientStream pipe)
    {
        while (true)
        {
            JobKeeperLaunchRequest? request;
            try
            {
                request = JobKeeperProtocol.ReadMessage<JobKeeperLaunchRequest>(pipe);
            }
            catch (IOException)
            {
                return;
            }

            if (request == null)
                return;

            JobKeeperProtocol.WriteMessage(pipe, requestHandler.Handle(request));
        }
    }
}
