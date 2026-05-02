namespace RunFence.Infrastructure;

public interface IProcessIdentityReader :
    IWindowProcessIdReader,
    IConsoleHostProcessResolver,
    IProcessOwnerSidReader,
    IProcessImageNameReader
{
}
