namespace RunFence.Launching.Processes;

public interface IProcessOwnerInfoReader
{
    ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid);
}
