using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public sealed class WinRtPackageManagerContextFactory(IWinRtStaDispatcher staDispatcher) : IWinRtPackageManagerContextFactory
{
    private readonly IWinRtStaDispatcher _staDispatcher = staDispatcher;

    private const int ForceTargetApplicationShutdown = 64;
    private const string PackageManagerClassName = "Windows.Management.Deployment.PackageManager";
    private static readonly Guid PackageManager5Iid = new("711F3117-1AFD-4313-978C-9BB6E1B864A7");
    private const int RegisterPackageByFamilyNameAsyncSlot = 8;
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(10);

    public IWinRtPackageManagerContext Create()
        => PackageManagerContext.Create(_staDispatcher);

    private sealed class PackageManagerContext(IWinRtStaDispatcher staDispatcher) : IWinRtPackageManagerContext
    {
        private readonly IWinRtStaDispatcher _staDispatcher = staDispatcher;
        private bool _roInitialized;
        private IntPtr _packageManager5;
        private bool _disposed;

        public static IWinRtPackageManagerContext Create(IWinRtStaDispatcher staDispatcher)
        {
            var context = new PackageManagerContext(staDispatcher);
            try
            {
                var roInitializeHr = WinRtNative.RoInitialize(WinRtNative.RoInitSingleThreaded);
                if (roInitializeHr < 0 && roInitializeHr != WinRtNative.RpcEChangedMode)
                    Marshal.ThrowExceptionForHR(roInitializeHr);

                context._roInitialized = roInitializeHr >= 0;

                using var classNameHandle = new HStringHandle(PackageManagerClassName);
                var activationHr = WinRtNative.RoActivateInstance(classNameHandle.Handle, out var packageManagerInspectable);
                Marshal.ThrowExceptionForHR(activationHr);

                try
                {
                    context._packageManager5 = QueryInterface(packageManagerInspectable, PackageManager5Iid);
                }
                finally
                {
                    Marshal.Release(packageManagerInspectable);
                }

                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        public void RegisterPackageByFamilyName(string packageFamilyName)
        {
            using var packageFamilyNameHandle = new HStringHandle(packageFamilyName);
            var method = GetMethod<RegisterPackageByFamilyNameAsyncDelegate>(
                _packageManager5,
                RegisterPackageByFamilyNameAsyncSlot);
            var operation = IntPtr.Zero;
            var hr = method(
                _packageManager5,
                packageFamilyNameHandle.Handle,
                IntPtr.Zero,
                ForceTargetApplicationShutdown,
                IntPtr.Zero,
                IntPtr.Zero,
                out operation);
            try
            {
                Marshal.ThrowExceptionForHR(hr);
                using var asyncInfo = new WinRtAsyncInfo(operation);
                WaitForRegistrationCompletion(asyncInfo);
            }
            finally
            {
                if (operation != IntPtr.Zero)
                    Marshal.Release(operation);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_packageManager5 != IntPtr.Zero)
            {
                Marshal.Release(_packageManager5);
                _packageManager5 = IntPtr.Zero;
            }

            if (_roInitialized)
                WinRtNative.RoUninitialize();
        }

        private void WaitForRegistrationCompletion(WinRtAsyncInfo asyncInfo)
        {
            var deadline = DateTime.UtcNow + RegistrationTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var status = asyncInfo.GetStatus();
                switch (status)
                {
                    case AsyncStatus.Completed:
                        return;
                    case AsyncStatus.Error:
                        Marshal.ThrowExceptionForHR(asyncInfo.GetErrorCode());
                        break;
                    case AsyncStatus.Canceled:
                        throw new OperationCanceledException("RegisterPackageByFamilyNameAsync was canceled.");
                    default:
                        _staDispatcher.WaitForDispatch(deadline);
                        break;
                }
            }

            throw new TimeoutException("RegisterPackageByFamilyNameAsync timed out.");
        }

        private static IntPtr QueryInterface(IntPtr instance, Guid iid)
        {
            var hr = Marshal.QueryInterface(instance, in iid, out var interfacePtr);
            Marshal.ThrowExceptionForHR(hr);
            return interfacePtr;
        }

        private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
        {
            var vtable = Marshal.ReadIntPtr(instance);
            var methodPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int RegisterPackageByFamilyNameAsyncDelegate(
        IntPtr packageManager,
        IntPtr mainPackageFamilyName,
        IntPtr dependencyPackageFamilyNames,
        int deploymentOptions,
        IntPtr appDataVolume,
        IntPtr optionalPackageFamilyNames,
        out IntPtr operation);
}
