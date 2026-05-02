namespace RunFence.Core;

public static class Constants
{
    // Crypto
    public static readonly int Argon2MemoryMb = DebugHelper.IsDebugBuild ? 256 : 1024;
    public static readonly int Argon2Iterations = DebugHelper.IsDebugBuild ? 1 : 3;
    public const int Argon2Parallelism = 2;
    public const int Argon2OutputBytes = 32;
    public const int Argon2SaltSize = 32;
    public static readonly byte[] PinCanaryPlaintext = "RunAsMgr-PIN-OK!"u8.ToArray();
}
