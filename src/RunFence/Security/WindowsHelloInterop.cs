using System.Runtime.InteropServices;

namespace RunFence.Security;

public static class WindowsHelloInterop
{
    private const string ClassId = "Windows.Security.Credentials.UI.UserConsentVerifier";

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    /// <remarks>Known limitation: polling loop used because WinRT put_Completed requires COM marshaling setup. Consider callback approach in future.</remarks>
    public static async Task<HelloVerificationResult> RequestAsync(IntPtr hwnd, string message)
    {
        IntPtr classId = IntPtr.Zero;
        IntPtr messageHString = IntPtr.Zero;
        IntPtr factoryPtr = IntPtr.Zero;
        IntPtr opPtr = IntPtr.Zero;

        try
        {
            ThrowIfFailed(WindowsCreateString(ClassId, ClassId.Length, out classId));

            var interopIid = typeof(IUserConsentVerifierInterop).GUID;
            ThrowIfFailed(RoGetActivationFactory(classId, ref interopIid, out factoryPtr));

            var interop = (IUserConsentVerifierInterop)Marshal.GetObjectForIUnknown(factoryPtr);

            var msg = message ?? string.Empty;
            ThrowIfFailed(WindowsCreateString(msg, msg.Length, out messageHString));

            var asyncIid = typeof(IAsyncOperation_VerificationResult).GUID;
            interop.RequestVerificationForWindowAsync(hwnd, messageHString, ref asyncIid, out opPtr);

            var op = (IAsyncOperation_VerificationResult)Marshal.GetObjectForIUnknown(opPtr);

            while (true)
            {
                op.get_Status(out var status);

                if (status == AsyncStatus.Completed)
                    break;

                switch (status)
                {
                    case AsyncStatus.Error:
                        op.get_ErrorCode(out var hr);
                        Marshal.ThrowExceptionForHR(hr);
                        break;
                    case AsyncStatus.Canceled:
                        return HelloVerificationResult.Canceled;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            op.GetResults(out var result);
            return result switch
            {
                VerificationResult.Verified => HelloVerificationResult.Verified,
                VerificationResult.Canceled => HelloVerificationResult.Canceled,
                VerificationResult.DeviceNotPresent or VerificationResult.NotConfiguredForUser or
                    VerificationResult.DisabledByPolicy or VerificationResult.DeviceBusy or
                    VerificationResult.RetriesExhausted => HelloVerificationResult.NotAvailable,
                _ => HelloVerificationResult.Failed
            };
        }
        finally
        {
            if (opPtr != IntPtr.Zero)
                Marshal.Release(opPtr);
            if (factoryPtr != IntPtr.Zero)
                Marshal.Release(factoryPtr);
            if (messageHString != IntPtr.Zero)
                WindowsDeleteString(messageHString);
            if (classId != IntPtr.Zero)
                WindowsDeleteString(classId);
        }
    }

    public static bool IsSystemAvailable()
    {
        IntPtr classId = IntPtr.Zero;
        IntPtr factoryPtr = IntPtr.Zero;

        try
        {
            if (WindowsCreateString(ClassId, ClassId.Length, out classId) < 0)
                return false;

            var iid = typeof(IUserConsentVerifierInterop).GUID;
            var hr = RoGetActivationFactory(classId, ref iid, out factoryPtr);
            return hr >= 0 && factoryPtr != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero)
                Marshal.Release(factoryPtr);
            if (classId != IntPtr.Zero)
                WindowsDeleteString(classId);
        }
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private enum VerificationResult
    {
        Verified = 0,
        DeviceNotPresent = 1,
        NotConfiguredForUser = 2,
        DisabledByPolicy = 3,
        DeviceBusy = 4,
        RetriesExhausted = 5,
        Canceled = 6,
    }

    private enum AsyncStatus
    {
        Started = 0,
        Completed = 1,
        Canceled = 2,
        Error = 3,
    }

    [ComImport]
    [Guid("39E050C3-4E74-441A-8DC0-B81104DF949C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUserConsentVerifierInterop
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);

        void RequestVerificationForWindowAsync(
            IntPtr appWindow,
            IntPtr message,
            ref Guid riid,
            out IntPtr asyncOperation);
    }

    [ComImport]
    [Guid("00000036-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWinHelloAsyncInfo
    {
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);

        void get_Id(out uint id);
        void get_Status(out AsyncStatus status);
        void get_ErrorCode(out int errorCode);
        void Cancel();
        void Close();
    }

    // FIXED IID: this is IAsyncOperation<UserConsentVerificationResult>
    [ComImport]
    [Guid("FD596FFD-2318-558F-9DBE-D21DF43764A5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAsyncOperation_VerificationResult : IWinHelloAsyncInfo
    {
        void put_Completed(IntPtr handler);
        void get_Completed(out IntPtr handler);
        void GetResults(out VerificationResult result);
    }
}
