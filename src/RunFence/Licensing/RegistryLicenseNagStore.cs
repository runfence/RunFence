using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Licensing;

public class RegistryLicenseNagStore : ILicenseNagStore
{
    private readonly string _registryKeyPath;
    private readonly IRegistryKey? _currentUserOverride;

    public RegistryLicenseNagStore()
        : this(PathConstants.LicenseRegistryKey)
    {
    }

    public RegistryLicenseNagStore(string registryKeyPath, IRegistryKey? currentUserOverride = null)
    {
        _registryKeyPath = registryKeyPath;
        _currentUserOverride = currentUserOverride;
    }

    public DateTime? ReadLastNagDate()
    {
        try
        {
            using var currentUser = OpenCurrentUser();
            using var key = currentUser.OpenSubKey(_registryKeyPath);
            if (key?.GetValue(PathConstants.LastNagShownValueName) is string value
                && DateTime.TryParse(value, out var date))
            {
                return date;
            }
        }
        catch
        {
        }

        return null;
    }

    public void WriteLastNagDate(DateTime shownAt)
    {
        try
        {
            using var currentUser = OpenCurrentUser();
            using var key = currentUser.CreateSubKey(_registryKeyPath);
            key.SetValue(PathConstants.LastNagShownValueName, shownAt.ToString("o"));
        }
        catch
        {
        }
    }

    private IRegistryKey OpenCurrentUser()
        => _currentUserOverride ?? new WindowsRegistryKey(Registry.CurrentUser);
}
