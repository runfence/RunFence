using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public sealed class WinRtUriProtocolLauncher(IWinRtStaDispatcher staDispatcher) : IAppxUriProtocolLauncher
{
    private const string LauncherClassName = "Windows.System.Launcher";
    private const string LauncherOptionsClassName = "Windows.System.LauncherOptions";
    private const string UriClassName = "Windows.Foundation.Uri";
    private static readonly TimeSpan DispatchObservationTimeout = TimeSpan.FromSeconds(5);
    private static readonly Guid UriRuntimeClassFactoryIid = new("44A9796F-723E-4FDF-A218-033E75B0C084");
    private static readonly Guid LauncherOptions2Iid = new("3BA08EB4-6E40-4DCE-A1A3-2F53950AFB49");
    private static readonly Guid LauncherStaticsIid = new("277151C3-9E3E-42F6-91A4-5DFDEB232451");
    // WinRT interface vtables extend IInspectable (3 IUnknown + 3 IInspectable members).
    private const int CreateUriSlot = 6;
    private const int TargetPackageFamilyNameSlot = 7;
    private const int LaunchUriAsyncSlot = 9;
    private const int AsyncBooleanGetResultsSlot = 8;

    public AppxLaunchResult Launch(AppxUriLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Uri))
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.CreateUriFailed,
                "CreateUri",
                "URI must not be empty.");
        }

        var roInitialized = false;
        IntPtr uriFactory = IntPtr.Zero;
        IntPtr uri = IntPtr.Zero;
        IntPtr launcherOptions = IntPtr.Zero;
        IntPtr launcherOptions2 = IntPtr.Zero;
        IntPtr launcherStatics = IntPtr.Zero;
        IntPtr launchOperation = IntPtr.Zero;
        try
        {
            try
            {
                var roInitializeHr = WinRtNative.RoInitialize(WinRtNative.RoInitSingleThreaded);
                if (roInitializeHr < 0 && roInitializeHr != WinRtNative.RpcEChangedMode)
                    Marshal.ThrowExceptionForHR(roInitializeHr);
                roInitialized = roInitializeHr >= 0;
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.RoInitializeFailed, "RoInitialize", ex);
            }

            try
            {
                uriFactory = GetActivationFactory(UriClassName, UriRuntimeClassFactoryIid);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.CreateUriFailed, "GetUriFactory", ex);
            }

            try
            {
                uri = CreateUri(uriFactory, options.Uri);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.CreateUriFailed, "CreateUri", ex);
            }

            try
            {
                launcherOptions = ActivateInstance(LauncherOptionsClassName);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.CreateLauncherOptionsFailed, "CreateLauncherOptions", ex);
            }

            try
            {
                launcherOptions2 = QueryInterface(launcherOptions, LauncherOptions2Iid);
                SetTargetPackageFamilyName(launcherOptions2, options.PackageFamilyName);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.SetTargetPackageFamilyFailed, "SetTargetPackageFamilyName", ex);
            }

            try
            {
                launcherStatics = GetActivationFactory(LauncherClassName, LauncherStaticsIid);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.LaunchUriAsyncFailed, "GetLauncherStatics", ex);
            }

            try
            {
                launchOperation = LaunchUriAsync(launcherStatics, uri, launcherOptions);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(AppxLaunchExitCode.LaunchUriAsyncFailed, "LaunchUriAsync", ex);
            }

            return ObserveDispatch(launchOperation);
        }
        finally
        {
            ReleaseInterfacePointer(launchOperation);
            ReleaseInterfacePointer(launcherStatics);
            ReleaseInterfacePointer(launcherOptions2);
            ReleaseInterfacePointer(launcherOptions);
            ReleaseInterfacePointer(uri);
            ReleaseInterfacePointer(uriFactory);
            if (roInitialized)
                WinRtNative.RoUninitialize();
        }
    }

    private IntPtr GetActivationFactory(string className, Guid iid)
    {
        using var classNameHandle = new HStringHandle(className);
        var hr = WinRtNative.RoGetActivationFactory(classNameHandle.Handle, ref iid, out var factoryPtr);
        Marshal.ThrowExceptionForHR(hr);
        return factoryPtr;
    }

    private IntPtr ActivateInstance(string className)
    {
        using var classNameHandle = new HStringHandle(className);
        var hr = WinRtNative.RoActivateInstance(classNameHandle.Handle, out var instancePtr);
        Marshal.ThrowExceptionForHR(hr);
        return instancePtr;
    }

    private IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        var hr = Marshal.QueryInterface(instance, in iid, out var interfacePtr);
        Marshal.ThrowExceptionForHR(hr);
        return interfacePtr;
    }

    private IntPtr CreateUri(IntPtr factory, string uri)
    {
        using var uriHandle = new HStringHandle(uri);
        var method = GetMethod<CreateUriDelegate>(factory, CreateUriSlot);
        var hr = method(factory, uriHandle.Handle, out var uriPtr);
        Marshal.ThrowExceptionForHR(hr);
        return uriPtr;
    }

    private void SetTargetPackageFamilyName(IntPtr launcherOptions2, string packageFamilyName)
    {
        using var packageFamilyNameHandle = new HStringHandle(packageFamilyName);
        var method = GetMethod<SetTargetPackageFamilyNameDelegate>(launcherOptions2, TargetPackageFamilyNameSlot);
        var hr = method(launcherOptions2, packageFamilyNameHandle.Handle);
        Marshal.ThrowExceptionForHR(hr);
    }

    private IntPtr LaunchUriAsync(IntPtr launcherStatics, IntPtr uri, IntPtr launcherOptions)
    {
        var method = GetMethod<LaunchUriAsyncDelegate>(launcherStatics, LaunchUriAsyncSlot);
        var hr = method(launcherStatics, uri, launcherOptions, out var operation);
        Marshal.ThrowExceptionForHR(hr);
        return operation;
    }

    private AppxLaunchResult ObserveDispatch(IntPtr operation)
    {
        WinRtAsyncInfo asyncInfo;
        try
        {
            asyncInfo = new WinRtAsyncInfo(operation);
        }
        catch (Exception ex)
        {
            return AppxLaunchResult.Failed(AppxLaunchExitCode.AsyncObservationFailed, "QueryAsyncInfo", ex);
        }

        using (asyncInfo)
        {
            var deadline = DateTime.UtcNow + DispatchObservationTimeout;
            while (DateTime.UtcNow < deadline)
            {
                AsyncStatus status;
                try
                {
                    status = asyncInfo.GetStatus();
                }
                catch (Exception ex)
                {
                    return AppxLaunchResult.Failed(AppxLaunchExitCode.AsyncObservationFailed, "ReadAsyncStatus", ex);
                }

                switch (status)
                {
                    case AsyncStatus.Completed:
                        try
                        {
                            return GetResults(operation)
                                ? AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted")
                                : AppxLaunchResult.Failed(AppxLaunchExitCode.LaunchUriAsyncFailed, "GetResults", "LaunchUriAsync returned false.");
                        }
                        catch (Exception ex)
                        {
                            return AppxLaunchResult.Failed(AppxLaunchExitCode.AsyncObservationFailed, "GetResults", ex);
                        }
                    case AsyncStatus.Error:
                        return CreateAsyncErrorResult(asyncInfo);
                    case AsyncStatus.Canceled:
                        return AppxLaunchResult.Failed(AppxLaunchExitCode.AsyncObservationFailed, "ObserveDispatch", "LaunchUriAsync was canceled.");
                    default:
                        staDispatcher.WaitForDispatch(deadline);
                        break;
                }
            }

            return AppxLaunchResult.Succeeded("LaunchUriAsyncDispatched");
        }
    }

    private bool GetResults(IntPtr operation)
    {
        var method = GetMethod<GetBooleanResultsDelegate>(operation, AsyncBooleanGetResultsSlot);
        var hr = method(operation, out byte launchAccepted);
        Marshal.ThrowExceptionForHR(hr);
        return launchAccepted != 0;
    }

    private AppxLaunchResult CreateAsyncErrorResult(WinRtAsyncInfo asyncInfo)
    {
        try
        {
            var errorCode = asyncInfo.GetErrorCode();
            return new AppxLaunchResult(
                false,
                AppxLaunchExitCode.AsyncObservationFailed,
                "ObserveDispatch",
                errorCode,
                $"LaunchUriAsync completed with error status (0x{unchecked((uint)errorCode):X8}).");
        }
        catch (Exception ex)
        {
            return AppxLaunchResult.Failed(AppxLaunchExitCode.AsyncObservationFailed, "ObserveDispatch", ex);
        }
    }

    private T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var methodPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
    }

    private void ReleaseInterfacePointer(IntPtr value)
    {
        if (value != IntPtr.Zero)
            Marshal.Release(value);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int CreateUriDelegate(IntPtr factory, IntPtr uri, out IntPtr createdUri);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetTargetPackageFamilyNameDelegate(IntPtr launcherOptions2, IntPtr packageFamilyName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int LaunchUriAsyncDelegate(IntPtr launcherStatics, IntPtr uri, IntPtr launcherOptions, out IntPtr launchOperation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetBooleanResultsDelegate(IntPtr asyncOperation, out byte launchAccepted);

}
