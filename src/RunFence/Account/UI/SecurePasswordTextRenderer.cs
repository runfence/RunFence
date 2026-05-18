using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Account.UI;

internal sealed class SecurePasswordTextRenderer
{
    public const char BulletChar = '\u25CF';

    public RenderedSecurePasswordText Render(ProtectedString password, bool revealed)
    {
        int charCount = password.Length;
        int byteCount = (charCount + 1) * sizeof(char);
        IntPtr textPointer = Marshal.AllocHGlobal(byteCount);

        try
        {
            if (revealed)
            {
                password.UseUtf16BytesSnapshot(utf16Bytes =>
                {
                    for (int i = 0; i < utf16Bytes.Length; i++)
                        Marshal.WriteByte(textPointer, i, utf16Bytes[i]);

                    Marshal.WriteInt16(textPointer, utf16Bytes.Length, 0);
                });
            }
            else
            {
                for (int i = 0; i < charCount; i++)
                    Marshal.WriteInt16(textPointer, i * sizeof(char), (short)BulletChar);

                Marshal.WriteInt16(textPointer, charCount * sizeof(char), 0);
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
