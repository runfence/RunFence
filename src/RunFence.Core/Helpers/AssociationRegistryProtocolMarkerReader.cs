using Microsoft.Win32;
using System.Security;

namespace RunFence.Core.Helpers;

public sealed class AssociationRegistryProtocolMarkerReader : IAssociationRegistryProtocolMarkerReader
{
    public bool HasUrlProtocolMarker(IRegistryKey? key)
        => HasUrlProtocolMarkerCore(
            key,
            current => current.GetValueKind("URL Protocol"));

    public bool HasUrlProtocolMarker(RegistryKey? key)
        => HasUrlProtocolMarkerCore(
            key,
            current => current.GetValueKind("URL Protocol"));

    private static bool HasUrlProtocolMarkerCore<T>(T? key, Action<T> readValueKind)
        where T : class
    {
        if (key == null)
            return false;

        try
        {
            readValueKind(key);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
