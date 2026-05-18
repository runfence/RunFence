using RunFence.Core.Models;

namespace RunFence.RunAs;

public interface IRunAsContainerCreationUI
{
    AppContainerEntry? ShowCreateContainerDialog();
}
