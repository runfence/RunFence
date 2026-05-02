using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class ClipboardFormatReader(ILoggingService log) : IClipboardFormatReader
{
    public IntPtr GetClipboardOwnerWindow() => ClipboardNative.GetClipboardOwner();

    public static bool IsGlobalMemoryFormat(uint format) => format switch
    {
        2 => false,
        9 => false,
        14 => false,
        0x80 => false,
        _ => true,
    };

    public IReadOnlyList<ClipboardFormatData> ReadGlobalMemoryFormats()
    {
        var result = new List<ClipboardFormatData>();
        if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: OpenClipboard failed with error {err}.");
            return result;
        }

        try
        {
            uint format = 0;
            int skipped = 0;
            while ((format = ClipboardNative.EnumClipboardFormats(format)) != 0)
            {
                if (!IsGlobalMemoryFormat(format))
                {
                    skipped++;
                    continue;
                }

                IntPtr hMem = ClipboardNative.GetClipboardData(format);
                if (hMem == IntPtr.Zero)
                {
                    log.Debug($"ClipboardPasteInterceptService: GetClipboardData(format={format}) returned null, skipping.");
                    continue;
                }

                UIntPtr size = ClipboardNative.GlobalSize(hMem);
                if (size == UIntPtr.Zero)
                {
                    log.Debug($"ClipboardPasteInterceptService: GlobalSize(format={format}) is 0, skipping.");
                    continue;
                }

                IntPtr pMem = ClipboardNative.GlobalLock(hMem);
                if (pMem == IntPtr.Zero)
                {
                    log.Debug($"ClipboardPasteInterceptService: GlobalLock(format={format}) failed, skipping.");
                    continue;
                }

                try
                {
                    int byteCount = (int)(uint)size;
                    var data = new byte[byteCount];
                    Marshal.Copy(pMem, data, 0, byteCount);
                    result.Add(new ClipboardFormatData(format, data));
                    log.Debug($"ClipboardPasteInterceptService: Read format={format}, size={byteCount} bytes.");
                }
                finally
                {
                    ClipboardNative.GlobalUnlock(hMem);
                }
            }

            if (skipped > 0)
                log.Debug($"ClipboardPasteInterceptService: Skipped {skipped} non-HGLOBAL clipboard format(s).");
        }
        finally
        {
            ClipboardNative.CloseClipboard();
        }

        return result;
    }
}
