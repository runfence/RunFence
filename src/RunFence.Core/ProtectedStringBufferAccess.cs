namespace RunFence.Core;

internal readonly ref struct ProtectedStringBufferAccess
{
    internal ProtectedStringBufferAccess(IntPtr address, int capacity)
    {
        Address = address;
        Capacity = capacity;
    }

    public IntPtr Address { get; }

    public int Capacity { get; }
}
