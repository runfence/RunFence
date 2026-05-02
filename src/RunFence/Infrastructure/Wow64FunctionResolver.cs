using System.Text;

namespace RunFence.Infrastructure;

/// <summary>
/// Resolves 32-bit function addresses from WoW64 target processes by walking
/// PEB32 → LDR → InLoadOrderModuleList → PE export tables, all via ReadProcessMemory.
/// </summary>
internal static class Wow64FunctionResolver
{
    private const uint ProcessWow64Information = 26;

    /// <summary>
    /// Resolves multiple 32-bit function addresses from a WoW64 target process.
    /// <paramref name="addresses"/> is populated in the same order as <paramref name="requests"/>.
    /// Returns false if the PEB32 address cannot be read or any request cannot be resolved.
    /// </summary>
    public static bool TryResolve(IntPtr hProcess,
        ReadOnlySpan<(string module, string function)> requests,
        Span<uint> addresses)
    {
        // Step 1: read PEB32 address via NtQueryInformationProcess(ProcessWow64Information)
        uint status = ProcessNative.NtQueryInformationProcess(
            hProcess, ProcessWow64Information,
            out IntPtr peb32Ptr, (uint)IntPtr.Size, out _);
        if (status != 0 || peb32Ptr == IntPtr.Zero)
            return false;

        uint peb32 = (uint)peb32Ptr.ToInt64();

        // Step 2: PEB32.Ldr at offset 0x0C
        uint ldrPtr = ReadUInt32(hProcess, peb32 + 0x0C);
        if (ldrPtr == 0)
            return false;

        // Step 3: InLoadOrderModuleList head at ldrPtr + 0x0C (LIST_ENTRY32: Flink, Blink)
        uint listHead = ldrPtr + 0x0C;
        uint flink = ReadUInt32(hProcess, listHead);

        // Track which requests are still unresolved
        bool[] resolved = new bool[requests.Length];
        int remaining = requests.Length;

        // Step 4: walk the module list
        uint current = flink;
        int safetyLimit = 256;
        while (current != listHead && safetyLimit-- > 0 && remaining > 0)
        {
            // LDR_DATA_TABLE_ENTRY32 (InLoadOrderLinks at offset 0)
            // DllBase at +0x18
            uint dllBase = ReadUInt32(hProcess, current + 0x18);
            // BaseDllName: Length at +0x2C, Buffer at +0x30
            ushort nameLen = ReadUInt16(hProcess, current + 0x2C);
            uint nameBuffer = ReadUInt32(hProcess, current + 0x30);

            if (dllBase != 0 && nameLen > 0 && nameBuffer != 0)
            {
                string moduleName = ReadUnicodeString(hProcess, nameBuffer, nameLen);

                for (int i = 0; i < requests.Length; i++)
                {
                    if (resolved[i])
                        continue;
                    if (!string.Equals(moduleName, requests[i].module, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Step 5: parse 32-bit PE export table
                    uint funcAddr = ResolveExport(hProcess, dllBase, requests[i].function);
                    if (funcAddr != 0)
                    {
                        addresses[i] = funcAddr;
                        resolved[i] = true;
                        remaining--;
                    }
                }
            }

            // Advance: next Flink at offset 0 of LDR_DATA_TABLE_ENTRY32
            uint next = ReadUInt32(hProcess, current);
            if (next == current)
                break;
            current = next;
        }

        return remaining == 0;
    }

    private static uint ResolveExport(IntPtr hProcess, uint dllBase, string functionName)
    {
        // PE offset (e_lfanew) at dllBase + 0x3C
        uint peOffset = ReadUInt32(hProcess, dllBase + 0x3C);
        uint peBase = dllBase + peOffset;

        // Export directory RVA at peBase + 0x78 (IMAGE_DIRECTORY_ENTRY_EXPORT)
        uint exportDirRva = ReadUInt32(hProcess, peBase + 0x78);
        if (exportDirRva == 0)
            return 0;

        uint exportDir = dllBase + exportDirRva;

        // NumberOfNames at exportDir + 0x18
        uint nameCount = ReadUInt32(hProcess, exportDir + 0x18);
        // AddressOfFunctions RVA at exportDir + 0x1C
        uint funcTableRva = ReadUInt32(hProcess, exportDir + 0x1C);
        // AddressOfNames RVA at exportDir + 0x20
        uint nameTableRva = ReadUInt32(hProcess, exportDir + 0x20);
        // AddressOfNameOrdinals RVA at exportDir + 0x24
        uint ordinalTableRva = ReadUInt32(hProcess, exportDir + 0x24);

        uint funcTable = dllBase + funcTableRva;
        uint nameTable = dllBase + nameTableRva;
        uint ordinalTable = dllBase + ordinalTableRva;

        for (uint j = 0; j < nameCount; j++)
        {
            uint nameRva = ReadUInt32(hProcess, nameTable + j * 4);
            string name = ReadAnsiString(hProcess, dllBase + nameRva, 256);
            if (!string.Equals(name, functionName, StringComparison.Ordinal))
                continue;

            ushort ordinal = ReadUInt16(hProcess, ordinalTable + j * 2);
            uint funcRva = ReadUInt32(hProcess, funcTable + (uint)(ordinal * 4));
            return dllBase + funcRva;
        }

        return 0;
    }

    private static uint ReadUInt32(IntPtr hProcess, uint address)
    {
        byte[] buf = ReadBytes(hProcess, address, 4);
        return (uint)(buf[0] | (buf[1] << 8) | (buf[2] << 16) | (buf[3] << 24));
    }

    private static ushort ReadUInt16(IntPtr hProcess, uint address)
    {
        byte[] buf = ReadBytes(hProcess, address, 2);
        return (ushort)(buf[0] | (buf[1] << 8));
    }

    private static byte[] ReadBytes(IntPtr hProcess, uint address, int count)
    {
        var buf = new byte[count];
        ProcessNative.ReadProcessMemory(hProcess, (IntPtr)(long)address,
            buf, (IntPtr)count, out _);
        return buf;
    }

    private static string ReadAnsiString(IntPtr hProcess, uint address, int maxLen)
    {
        byte[] buf = ReadBytes(hProcess, address, maxLen);
        int len = Array.IndexOf(buf, (byte)0);
        if (len < 0) len = buf.Length;
        return Encoding.ASCII.GetString(buf, 0, len);
    }

    private static string ReadUnicodeString(IntPtr hProcess, uint address, int byteLen)
    {
        byte[] buf = ReadBytes(hProcess, address, byteLen);
        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }
}
