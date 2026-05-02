namespace RunFence.Account.UI;

internal sealed class RenderedSecurePasswordText(IntPtr textPointer, int byteCount) : IDisposable
{
    public IntPtr TextPointer { get; } = textPointer;
    public int ByteCount { get; } = byteCount;

    public void Dispose()
    {
        if (TextPointer == IntPtr.Zero)
            return;

        for (int i = 0; i < ByteCount; i++)
            System.Runtime.InteropServices.Marshal.WriteByte(TextPointer, i, 0);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(TextPointer);
    }
}
