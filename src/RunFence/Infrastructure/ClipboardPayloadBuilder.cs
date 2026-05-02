using System.Runtime.InteropServices;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class ClipboardPayloadBuilder(ILoggingService log) : IClipboardPayloadBuilder
{
    private static readonly IntPtr s_pGlobalAlloc64;
    private static readonly IntPtr s_pGlobalLock64;
    private static readonly IntPtr s_pGlobalUnlock64;
    private static readonly IntPtr s_pGlobalFree64;
    private static readonly IntPtr s_pOpenClipboard64;
    private static readonly IntPtr s_pEmptyClipboard64;
    private static readonly IntPtr s_pSetClipboard64;
    private static readonly IntPtr s_pCloseClipboard64;

    private static readonly byte[] s_shellcodeX64 =
    [
        0x41,0x57, 0x41,0x56, 0x41,0x55, 0x41,0x54, 0x57, 0x56, 0x53,
        0x48,0x83,0xEC,0x20,
        0x49,0x89,0xCF, 0x45,0x8B,0x77,0x48, 0x4D,0x8D,0x6F,0x4C,
        0x49,0x8B,0x4F,0x40, 0x41,0xFF,0x57,0x20, 0x85,0xC0, 0x74,0x7E,
        0x41,0xFF,0x57,0x28,
        0x45,0x85,0xF6, 0x74,0x5B,
        0xB9,0x02,0x00,0x00,0x00, 0x41,0x8B,0x55,0x04, 0x41,0xFF,0x17,
        0x48,0x85,0xC0, 0x74,0x59,
        0x4C,0x8B,0xE0, 0x49,0x8B,0xCC, 0x41,0xFF,0x57,0x08,
        0x48,0x85,0xC0, 0x74,0x43,
        0x48,0x8B,0xF8, 0xFC, 0x49,0x8D,0x75,0x08, 0x41,0x8B,0x4D,0x04, 0xF3,0xA4,
        0x49,0x8B,0xCC, 0x41,0xFF,0x57,0x10,
        0x41,0x8B,0x4D,0x00, 0x49,0x8B,0xD4, 0x41,0xFF,0x57,0x30,
        0x48,0x85,0xC0, 0x75,0x07,
        0x49,0x8B,0xCC, 0x41,0xFF,0x57,0x18,
        0x41,0x8B,0x45,0x04, 0x83,0xC0,0x08, 0x4C,0x03,0xE8, 0x41,0xFF,0xCE, 0xEB,0xA0,
        0x41,0xFF,0x57,0x38, 0x33,0xC0, 0xEB,0x17,
        0x49,0x8B,0xCC, 0x41,0xFF,0x57,0x18,
        0x41,0xFF,0x57,0x38, 0xB8,0x01,0x00,0x00,0x00, 0xEB,0x05,
        0xB8,0x02,0x00,0x00,0x00,
        0x48,0x83,0xC4,0x20, 0x5B, 0x5E, 0x5F, 0x41,0x5C, 0x41,0x5D, 0x41,0x5E, 0x41,0x5F, 0xC3,
    ];

    private static readonly byte[] s_shellcodeX86 =
    [
        0x55, 0x89,0xE5, 0x53, 0x56, 0x57,
        0x8B,0x5D,0x08, 0x8B,0x7B,0x24, 0x8D,0x73,0x28,
        0xFF,0x73,0x20, 0xFF,0x53,0x10, 0x85,0xC0, 0x74,0x68,
        0xFF,0x53,0x14,
        0x85,0xFF, 0x74,0x49,
        0xFF,0x76,0x04, 0x6A,0x02, 0xFF,0x13, 0x85,0xC0, 0x74,0x4C,
        0x50, 0x50, 0xFF,0x53,0x04, 0x85,0xC0, 0x74,0x3C,
        0x8B,0x4E,0x04, 0x8D,0x56,0x08, 0x56, 0x57, 0x89,0xC7, 0x89,0xD6, 0xFC, 0xF3,0xA4, 0x5F, 0x5E,
        0xFF,0x34,0x24, 0xFF,0x53,0x08,
        0xFF,0x34,0x24, 0xFF,0x36, 0xFF,0x53,0x18, 0x85,0xC0, 0x75,0x06,
        0xFF,0x34,0x24, 0xFF,0x53,0x0C,
        0x58, 0x8B,0x46,0x04, 0x83,0xC0,0x08, 0x01,0xC6, 0x4F, 0xEB,0xB3,
        0xFF,0x53,0x1C, 0x33,0xC0, 0xEB,0x16,
        0xFF,0x34,0x24, 0xFF,0x53,0x0C, 0x58,
        0xFF,0x53,0x1C, 0xB8,0x01,0x00,0x00,0x00, 0xEB,0x05,
        0xB8,0x02,0x00,0x00,0x00,
        0x5F, 0x5E, 0x5B, 0x5D, 0xC2,0x04,0x00,
    ];

    static ClipboardPayloadBuilder()
    {
        var k = NativeLibrary.Load("kernel32.dll");
        var u = NativeLibrary.Load("user32.dll");
        s_pGlobalAlloc64 = NativeLibrary.GetExport(k, "GlobalAlloc");
        s_pGlobalLock64 = NativeLibrary.GetExport(k, "GlobalLock");
        s_pGlobalUnlock64 = NativeLibrary.GetExport(k, "GlobalUnlock");
        s_pGlobalFree64 = NativeLibrary.GetExport(k, "GlobalFree");
        s_pOpenClipboard64 = NativeLibrary.GetExport(u, "OpenClipboard");
        s_pEmptyClipboard64 = NativeLibrary.GetExport(u, "EmptyClipboard");
        s_pSetClipboard64 = NativeLibrary.GetExport(u, "SetClipboardData");
        s_pCloseClipboard64 = NativeLibrary.GetExport(u, "CloseClipboard");
    }

    public bool TryBuild(IntPtr processHandle, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats, out ClipboardInjectionPayload payload)
    {
        payload = null!;
        if (!ProcessNative.IsWow64Process(processHandle, out bool isWow64))
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: IsWow64Process failed with error {err}.");
            return false;
        }

        log.Debug($"ClipboardPasteInterceptService: target isWow64={isWow64}, hWnd=0x{hWnd.ToInt64():X}.");

        if (!isWow64)
        {
            payload = new ClipboardInjectionPayload(s_shellcodeX64, BuildDataBlock64(formats, hWnd));
            log.Debug($"ClipboardPasteInterceptService: Using x64 shellcode, data block {payload.DataBlock.Length} bytes.");
            return true;
        }

        ReadOnlySpan<(string, string)> requests =
        [
            ("kernel32.dll", "GlobalAlloc"),
            ("kernel32.dll", "GlobalLock"),
            ("kernel32.dll", "GlobalUnlock"),
            ("kernel32.dll", "GlobalFree"),
            ("user32.dll", "OpenClipboard"),
            ("user32.dll", "EmptyClipboard"),
            ("user32.dll", "SetClipboardData"),
            ("user32.dll", "CloseClipboard"),
        ];

        uint[] addrs = new uint[8];
        if (!Wow64FunctionResolver.TryResolve(processHandle, requests, addrs))
        {
            log.Warn("ClipboardPasteInterceptService: Wow64 function resolution failed.");
            return false;
        }

        log.Debug($"ClipboardPasteInterceptService: Resolved x86 functions: GlobalAlloc=0x{addrs[0]:X8}, OpenClipboard=0x{addrs[4]:X8}.");
        payload = new ClipboardInjectionPayload(s_shellcodeX86, BuildDataBlock32(formats, addrs, hWnd));
        log.Debug($"ClipboardPasteInterceptService: Using x86 shellcode, data block {payload.DataBlock.Length} bytes.");
        return true;
    }

    internal static byte[] BuildDataBlock64(IReadOnlyList<ClipboardFormatData> formats, IntPtr hWnd)
    {
        int size = 8 * 8 + 8 + 4;
        foreach (var format in formats)
            size += 4 + 4 + format.Data.Length;

        var block = new byte[size];
        int offset = 0;

        void WritePtr(IntPtr p)
        {
            long v = p.ToInt64();
            for (int i = 0; i < 8; i++)
                block[offset++] = (byte)(v >> (i * 8));
        }

        void WriteU32(uint v)
        {
            for (int i = 0; i < 4; i++)
                block[offset++] = (byte)(v >> (i * 8));
        }

        WritePtr(s_pGlobalAlloc64);
        WritePtr(s_pGlobalLock64);
        WritePtr(s_pGlobalUnlock64);
        WritePtr(s_pGlobalFree64);
        WritePtr(s_pOpenClipboard64);
        WritePtr(s_pEmptyClipboard64);
        WritePtr(s_pSetClipboard64);
        WritePtr(s_pCloseClipboard64);
        WritePtr(hWnd);
        WriteU32((uint)formats.Count);

        foreach (var format in formats)
        {
            WriteU32(format.Format);
            WriteU32((uint)format.Data.Length);
            format.Data.CopyTo(block, offset);
            offset += format.Data.Length;
        }

        return block;
    }

    internal static byte[] BuildDataBlock32(IReadOnlyList<ClipboardFormatData> formats, uint[] addrs, IntPtr hWnd)
    {
        int size = 8 * 4 + 4 + 4;
        foreach (var format in formats)
            size += 4 + 4 + format.Data.Length;

        var block = new byte[size];
        int offset = 0;

        void WriteU32(uint v)
        {
            for (int i = 0; i < 4; i++)
                block[offset++] = (byte)(v >> (i * 8));
        }

        foreach (var addr in addrs)
            WriteU32(addr);
        WriteU32((uint)hWnd.ToInt64());
        WriteU32((uint)formats.Count);

        foreach (var format in formats)
        {
            WriteU32(format.Format);
            WriteU32((uint)format.Data.Length);
            format.Data.CopyTo(block, offset);
            offset += format.Data.Length;
        }

        return block;
    }
}
