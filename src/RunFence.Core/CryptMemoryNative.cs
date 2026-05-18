using System.Runtime.InteropServices;

namespace RunFence.Core;

internal static class CryptMemoryNative
{
    public const uint CRYPTPROTECTMEMORY_SAME_PROCESS = 0;
    public const int CRYPTPROTECTMEMORY_BLOCK_SIZE = 16;

    public static int RoundUpToBlockSize(int value) =>
        (value + CRYPTPROTECTMEMORY_BLOCK_SIZE - 1) &
        ~(CRYPTPROTECTMEMORY_BLOCK_SIZE - 1);

    [DllImport("crypt32.dll", SetLastError = true)]
    public static extern bool CryptProtectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("crypt32.dll", SetLastError = true)]
    public static extern bool CryptUnprotectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualLock(IntPtr lpAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualUnlock(IntPtr lpAddress, UIntPtr dwSize);
}
