using System.Management;
using Microsoft.Win32;

namespace RunFence.Licensing;

public class MachineIdentityReader : IMachineIdentityReader
{
    public string? ReadSmbiosUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var uuid = obj["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uuid))
                    return uuid;
            }
        }
        catch
        {
        }

        return null;
    }

    public string? ReadWindowsMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
