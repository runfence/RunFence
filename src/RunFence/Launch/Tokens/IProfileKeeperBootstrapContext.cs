namespace RunFence.Launch.Tokens;

public interface IProfileKeeperBootstrapContext
{
    T Run<T>(Func<T> action);
}
