using Microsoft.Win32;

namespace RunFence.Core.Helpers;

public interface IAssociationRegistryProtocolMarkerReader
{
    bool HasUrlProtocolMarker(IRegistryKey? key);

    bool HasUrlProtocolMarker(RegistryKey? key);
}
