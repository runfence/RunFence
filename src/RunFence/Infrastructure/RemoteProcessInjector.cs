using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Infrastructure;

public sealed class RemoteProcessInjector(IClipboardPayloadBuilder payloadBuilder, ILoggingService log) : IRemoteProcessInjector
{
    public bool TryInjectClipboardData(int targetProcessId, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats)
    {
        IntPtr hProcess = ProcessNative.OpenProcess(
            ProcessNative.PROCESS_CREATE_THREAD | ProcessNative.PROCESS_VM_OPERATION |
            ProcessNative.PROCESS_VM_READ | ProcessNative.PROCESS_VM_WRITE |
            ProcessNative.PROCESS_QUERY_INFORMATION,
            false,
            targetProcessId);

        if (hProcess == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: OpenProcess({targetProcessId}) failed with error {err}.");
            return false;
        }

        try
        {
            LogProcessName(hProcess, targetProcessId);
            return InjectIntoOpenProcess(hProcess, targetProcessId, hWnd, formats);
        }
        finally
        {
            ProcessNative.CloseHandle(hProcess);
        }
    }

    private bool InjectIntoOpenProcess(IntPtr hProcess, int targetProcessId, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats)
    {
        if (!payloadBuilder.TryBuild(hProcess, hWnd, formats, out var payload))
            return false;

        IntPtr pData = ProcessNative.VirtualAllocEx(
            hProcess,
            IntPtr.Zero,
            (uint)payload.DataBlock.Length,
            ProcessNative.MEM_COMMIT | ProcessNative.MEM_RESERVE,
            ProcessNative.PAGE_READWRITE);
        if (pData == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: VirtualAllocEx(data, {payload.DataBlock.Length}) failed with error {err} for pid {targetProcessId}.");
            return false;
        }

        log.Debug($"ClipboardPasteInterceptService: Allocated data block at 0x{pData.ToInt64():X} in pid {targetProcessId}.");

        if (!ProcessNative.WriteProcessMemory(hProcess, pData, payload.DataBlock, (IntPtr)payload.DataBlock.Length, out _))
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: WriteProcessMemory(data) failed with error {err} for pid {targetProcessId}.");
            ProcessNative.VirtualFreeEx(hProcess, pData, 0, ProcessNative.MEM_RELEASE);
            return false;
        }

        IntPtr pCode = ProcessNative.VirtualAllocEx(
            hProcess,
            IntPtr.Zero,
            (uint)payload.Shellcode.Length,
            ProcessNative.MEM_COMMIT | ProcessNative.MEM_RESERVE,
            ProcessNative.PAGE_EXECUTE_READWRITE);
        if (pCode == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: VirtualAllocEx(code, {payload.Shellcode.Length}) failed with error {err} for pid {targetProcessId}.");
            ProcessNative.VirtualFreeEx(hProcess, pData, 0, ProcessNative.MEM_RELEASE);
            return false;
        }

        log.Debug($"ClipboardPasteInterceptService: Allocated shellcode at 0x{pCode.ToInt64():X} in pid {targetProcessId}.");

        if (!ProcessNative.WriteProcessMemory(hProcess, pCode, payload.Shellcode, (IntPtr)payload.Shellcode.Length, out _))
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: WriteProcessMemory(shellcode) failed with error {err} for pid {targetProcessId}.");
            ProcessNative.VirtualFreeEx(hProcess, pCode, 0, ProcessNative.MEM_RELEASE);
            ProcessNative.VirtualFreeEx(hProcess, pData, 0, ProcessNative.MEM_RELEASE);
            return false;
        }

        IntPtr hThread = ProcessNative.CreateRemoteThread(hProcess, IntPtr.Zero, 0, pCode, pData, 0, out uint threadId);
        if (hThread == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            log.Warn($"ClipboardPasteInterceptService: CreateRemoteThread failed with error {err} for pid {targetProcessId}.");
            ProcessNative.VirtualFreeEx(hProcess, pCode, 0, ProcessNative.MEM_RELEASE);
            ProcessNative.VirtualFreeEx(hProcess, pData, 0, ProcessNative.MEM_RELEASE);
            return false;
        }

        log.Debug($"ClipboardPasteInterceptService: Remote thread {threadId} started in pid {targetProcessId}.");

        uint waitResult = ProcessLaunchNative.WaitForSingleObject(hThread, 5000);
        if (waitResult != ProcessLaunchNative.WAIT_OBJECT_0)
        {
            log.Warn($"ClipboardPasteInterceptService: Remote thread timed out or failed (wait=0x{waitResult:X}) for pid {targetProcessId}. Regions left allocated.");
            ProcessNative.CloseHandle(hThread);
            return false;
        }

        log.Info($"ClipboardPasteInterceptService: Clipboard injection succeeded for pid {targetProcessId}. Sending synthetic paste.");
        ProcessNative.CloseHandle(hThread);
        ProcessNative.VirtualFreeEx(hProcess, pCode, 0, ProcessNative.MEM_RELEASE);
        ProcessNative.VirtualFreeEx(hProcess, pData, 0, ProcessNative.MEM_RELEASE);
        return true;
    }

    private void LogProcessName(IntPtr hProcess, int targetProcessId)
    {
        var exePath = new StringBuilder(260);
        uint exePathLen = 260;
        string processName = ProcessNative.QueryFullProcessImageName(hProcess, 0, exePath, ref exePathLen)
            ? Path.GetFileName(exePath.ToString())
            : "?";
        log.Debug($"ClipboardPasteInterceptService: OpenProcess({targetProcessId}) succeeded ({processName}).");
    }
}
