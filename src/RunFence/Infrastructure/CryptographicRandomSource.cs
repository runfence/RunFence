using System.Security.Cryptography;

namespace RunFence.Infrastructure;

public sealed class CryptographicRandomSource : IRandomSource
{
    public int NextInt32(int exclusiveUpperBound) => RandomNumberGenerator.GetInt32(exclusiveUpperBound);
}
