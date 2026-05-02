using System.Security.Cryptography;

namespace RunFence.Security;

public class TpmHandleProvider(ITpmNativeApi api)
{
    public IntPtr OpenProvider()
    {
        int status = api.NCryptOpenStorageProvider(out var hProvider, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0);
        if (status != 0)
            throw new CryptographicException($"NCryptOpenStorageProvider failed: 0x{status:X8}");
        return hProvider;
    }

    public T WithProviderAndKey<T>(string keyName, Func<IntPtr, IntPtr, T> action)
    {
        var hProvider = IntPtr.Zero;
        var hKey = IntPtr.Zero;
        try
        {
            hProvider = OpenProvider();
            int status = api.NCryptOpenKey(hProvider, out hKey, keyName, 0, 0);
            if (status != 0)
                throw new CryptographicException($"NCryptOpenKey failed: 0x{status:X8}");
            return action(hProvider, hKey);
        }
        finally
        {
            FreeIfNonZero(hKey);
            FreeIfNonZero(hProvider);
        }
    }

    public void FreeIfNonZero(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            api.NCryptFreeObject(handle);
    }
}
