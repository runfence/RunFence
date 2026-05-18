namespace RunFence.Core;

public readonly ref struct ProtectedUtf16ZSnapshot
{
    private readonly IntPtr _address;

    internal ProtectedUtf16ZSnapshot(int charCount, IntPtr address)
    {
        if (charCount < 0)
            throw new ArgumentOutOfRangeException(nameof(charCount));

        CharCount = charCount;
        _address = address;
    }

    public int CharCount { get; }

    public IntPtr DangerousGetIntPtr() => _address;
}
