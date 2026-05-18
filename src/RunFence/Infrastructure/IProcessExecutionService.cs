namespace RunFence.Infrastructure;

public interface IProcessExecutionService
{
    ProcessExecutionResult Run(ProcessExecutionRequest request);

    Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request);
}
