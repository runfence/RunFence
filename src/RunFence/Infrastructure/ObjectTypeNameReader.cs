using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

internal sealed class ObjectTypeNameReader : IObjectTypeNameReader
{
    private const int InitialBufferSize = 1024;
    private const int MaxBufferSize = 64 * 1024;

    public bool TryGetObjectTypeName(IntPtr handle, out string typeName)
    {
        typeName = string.Empty;
        var bufferSize = InitialBufferSize;

        while (bufferSize <= MaxBufferSize)
        {
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                var status = SystemHandleNative.NtQueryObject(
                    handle,
                    SystemHandleNative.ObjectTypeInformation,
                    buffer,
                    bufferSize,
                    out var returnLength);

                if (status == SystemHandleNative.StatusSuccess)
                {
                    var unicodeString = Marshal.PtrToStructure<SystemHandleNative.UnicodeString>(buffer);
                    if (unicodeString.Buffer == IntPtr.Zero || unicodeString.Length == 0)
                        return false;

                    typeName = Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2) ?? string.Empty;
                    return typeName.Length > 0;
                }

                if (!IsBufferTooSmall(status))
                    return false;

                bufferSize = Math.Max(bufferSize * 2, returnLength);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return false;
    }

    private static bool IsBufferTooSmall(int status) =>
        status is SystemHandleNative.StatusInfoLengthMismatch
            or SystemHandleNative.StatusBufferOverflow
            or SystemHandleNative.StatusBufferTooSmall;
}
