using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Account.UI;

internal sealed class SecurePasswordTextRenderer
{
    public const char BulletChar = '\u25CF';

    public RenderedSecurePasswordText Render(ProtectedString password, bool revealed)
    {
        int charCount = password.Length;
        int byteCount = (charCount + 1) * 2;
        IntPtr textPointer = Marshal.AllocHGlobal(byteCount);

        try
        {
            if (revealed)
            {
                IntPtr source = password.AllocUnicode();
                try
                {
                    for (int i = 0; i < byteCount; i++)
                        Marshal.WriteByte(textPointer, i, Marshal.ReadByte(source, i));
                }
                finally
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(source);
                }
            }
            else
            {
                for (int i = 0; i < charCount; i++)
                    Marshal.WriteInt16(textPointer, i * 2, (short)BulletChar);
                Marshal.WriteInt16(textPointer, charCount * 2, 0);
            }
        }
        catch
        {
            for (int i = 0; i < byteCount; i++)
                Marshal.WriteByte(textPointer, i, 0);
            Marshal.FreeHGlobal(textPointer);
            throw;
        }

        return new RenderedSecurePasswordText(textPointer, byteCount);
    }
}
