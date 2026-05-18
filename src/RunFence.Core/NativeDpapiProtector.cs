using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RunFence.Core;

public sealed class NativeDpapiProtector : IDpapiProtector
{
    private const int ErrorNotLocked = 158;

    private readonly IProtectedMemoryApi _protectedMemoryApi;

    public NativeDpapiProtector()
        : this(NativeProtectedMemoryApi.Instance)
    {
    }

    internal NativeDpapiProtector(IProtectedMemoryApi protectedMemoryApi)
    {
        _protectedMemoryApi = protectedMemoryApi ?? throw new ArgumentNullException(nameof(protectedMemoryApi));
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
    {
        NativeSecretMemory? plaintextBuffer = null;
        NativeSecretMemory? entropyBuffer = null;
        Native.DATA_BLOB output = default;
        try
        {
            plaintextBuffer = CreateScratchBuffer(plaintext);
            entropyBuffer = CreateScratchBuffer(entropy);

            var plaintextBlob = CreateBlob(plaintextBuffer, plaintext.Length);
            var entropyBlob = CreateBlob(entropyBuffer, entropy.Length);

            if (!Native.CryptProtectData(
                    ref plaintextBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out output))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptProtectData failed.");
            }

            if (output.cbData == 0)
                return [];

            byte[] protectedData = new byte[output.cbData];
            Marshal.Copy(output.pbData, protectedData, 0, output.cbData);
            return protectedData;
        }
        finally
        {
            plaintextBuffer?.Dispose();
            entropyBuffer?.Dispose();
            ZeroAndFreeDpapiBlob(ref output);
        }
    }

    public SecureSecret UnprotectToSecret(byte[] protectedData, ReadOnlySpan<byte> entropy)
    {
        ArgumentNullException.ThrowIfNull(protectedData);

        Native.DATA_BLOB input = default;
        NativeSecretMemory? entropyBuffer = null;
        Native.DATA_BLOB output = default;
        GCHandle inputHandle = default;
        DpapiOutputBuffer? outputBuffer = null;
        try
        {
            inputHandle = GCHandle.Alloc(protectedData, GCHandleType.Pinned);
            input = new Native.DATA_BLOB(protectedData.Length, inputHandle.AddrOfPinnedObject());
            entropyBuffer = CreateScratchBuffer(entropy);
            var entropyBlob = CreateBlob(entropyBuffer, entropy.Length);

            if (!Native.CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out output))
            {
                throw new CryptographicException(
                    "CryptUnprotectData failed.",
                    new Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectData failed."));
            }

            outputBuffer = new DpapiOutputBuffer(output.pbData, output.cbData, _protectedMemoryApi);
            outputBuffer.LockOrThrow();
            return new SecureSecret(
                outputBuffer.Length,
                destination => CopyNativeToSpan(outputBuffer.DangerousGetPointer(), destination),
                _protectedMemoryApi,
                lockTimeout: null);
        }
        finally
        {
            outputBuffer?.Dispose();
            entropyBuffer?.Dispose();
            if (inputHandle.IsAllocated)
                inputHandle.Free();
        }
    }

    public ProtectedString UnprotectToProtectedString(byte[] protectedData, ReadOnlySpan<byte> entropy)
    {
        ArgumentNullException.ThrowIfNull(protectedData);

        Native.DATA_BLOB input = default;
        NativeSecretMemory? entropyBuffer = null;
        Native.DATA_BLOB output = default;
        GCHandle inputHandle = default;
        DpapiOutputBuffer? outputBuffer = null;
        try
        {
            inputHandle = GCHandle.Alloc(protectedData, GCHandleType.Pinned);
            input = new Native.DATA_BLOB(protectedData.Length, inputHandle.AddrOfPinnedObject());
            entropyBuffer = CreateScratchBuffer(entropy);
            var entropyBlob = CreateBlob(entropyBuffer, entropy.Length);

            if (!Native.CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out output))
            {
                throw new CryptographicException(
                    "CryptUnprotectData failed.",
                    new Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectData failed."));
            }

            outputBuffer = new DpapiOutputBuffer(output.pbData, output.cbData, _protectedMemoryApi);
            outputBuffer.LockOrThrow();

            var result = new ProtectedString();
            unsafe
            {
                result.SetFromUtf16Bytes(new ReadOnlySpan<byte>(outputBuffer.DangerousGetPointer().ToPointer(), outputBuffer.Length));
            }

            result.MakeReadOnly();
            return result;
        }
        finally
        {
            outputBuffer?.Dispose();
            entropyBuffer?.Dispose();
            if (inputHandle.IsAllocated)
                inputHandle.Free();
        }
    }

    private NativeSecretMemory CreateScratchBuffer(ReadOnlySpan<byte> data)
    {
        var buffer = new NativeSecretMemory(Math.Max(1, data.Length), _protectedMemoryApi);
        try
        {
            if (!data.IsEmpty)
                CopySpanToNative(data, buffer.Address);

            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static Native.DATA_BLOB CreateBlob(NativeSecretMemory buffer, int length) =>
        new(length, buffer.Address);

    private static unsafe void CopySpanToNative(ReadOnlySpan<byte> source, IntPtr destination)
    {
        if (source.IsEmpty)
            return;

        fixed (byte* sourcePtr = source)
        {
            Buffer.MemoryCopy(sourcePtr, destination.ToPointer(), source.Length, source.Length);
        }
    }

    private static unsafe void CopyNativeToSpan(IntPtr source, Span<byte> destination)
    {
        if (destination.IsEmpty)
            return;

        fixed (byte* destinationPtr = destination)
        {
            Buffer.MemoryCopy(source.ToPointer(), destinationPtr, destination.Length, destination.Length);
        }
    }

    private static void ZeroAndFreeDpapiBlob(ref Native.DATA_BLOB blob)
    {
        if (blob.pbData == IntPtr.Zero)
            return;

        Exception? failure = null;
        try
        {
            for (int i = 0; i < blob.cbData; i++)
                Marshal.WriteByte(blob.pbData, i, 0);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        IntPtr result = Native.LocalFree(blob.pbData);
        if (result != IntPtr.Zero && failure is null)
            failure = new Win32Exception(Marshal.GetLastWin32Error(), "LocalFree failed for DPAPI output.");

        blob = default;
        if (failure is not null)
            throw new InvalidOperationException("DPAPI blob cleanup failed.", failure);
    }

    private sealed class DpapiOutputBuffer : IDisposable
    {
        private readonly IProtectedMemoryApi _api;
        private IntPtr _pointer;
        private readonly int _length;
        private bool _locked;
        private bool _disposed;

        public DpapiOutputBuffer(IntPtr pointer, int length, IProtectedMemoryApi api)
        {
            _pointer = pointer;
            _length = length;
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public int Length => _length;

        public IntPtr DangerousGetPointer()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pointer;
        }

        public void LockOrThrow()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_locked || _length == 0)
                return;

            if (!_api.VirtualLock(_pointer, _length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualLock failed for DPAPI output.");

            _locked = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Exception? failure = null;
            try
            {
                if (_pointer != IntPtr.Zero)
                {
                    for (int i = 0; i < _length; i++)
                        Marshal.WriteByte(_pointer, i, 0);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                if (_locked)
                {
                    bool unlocked = _api.VirtualUnlock(_pointer, _length);
                    int unlockError = unlocked ? 0 : Marshal.GetLastWin32Error();
                    // ERROR_NOT_LOCKED means there is no remaining page lock to release; the DPAPI
                    // output was already zeroed above, so other unlock failures are fatal.
                    if (!unlocked && failure is null && unlockError != ErrorNotLocked)
                        failure = new Win32Exception(unlockError, "VirtualUnlock failed for DPAPI output.");
                }
            }
            catch (Exception ex) when (failure is null)
            {
                failure = ex;
            }

            IntPtr result = _pointer == IntPtr.Zero ? IntPtr.Zero : Native.LocalFree(_pointer);
            if (_pointer != IntPtr.Zero && result != IntPtr.Zero && failure is null)
                failure = new Win32Exception(Marshal.GetLastWin32Error(), "LocalFree failed for DPAPI output.");

            _pointer = IntPtr.Zero;
            if (failure is not null)
                Environment.FailFast("NativeDpapiProtector DPAPI output cleanup failed.", failure);
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;

            public DATA_BLOB(int byteCount, IntPtr pointer)
            {
                cbData = byteCount;
                pbData = pointer;
            }
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            IntPtr szDataDescr,
            ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            IntPtr ppszDataDescr,
            ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);
    }
}
