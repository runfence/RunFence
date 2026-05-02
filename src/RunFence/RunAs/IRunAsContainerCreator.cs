using RunFence.Core.Models;

namespace RunFence.RunAs;

public interface IRunAsContainerCreator
{
    AppContainerEntry? CreateNewContainer();
}
