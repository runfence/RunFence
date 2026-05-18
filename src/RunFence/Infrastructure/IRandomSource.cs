namespace RunFence.Infrastructure;

public interface IRandomSource
{
    int NextInt32(int exclusiveUpperBound);
}
