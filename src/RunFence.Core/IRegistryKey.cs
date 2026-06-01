using Microsoft.Win32;

namespace RunFence.Core;

public interface IRegistryKey : IDisposable
{
    string Name { get; }

    int SubKeyCount { get; }

    int ValueCount { get; }

    IRegistryKey? OpenSubKey(string name, bool writable = false);

    IRegistryKey CreateSubKey(string subkey);

    void DeleteSubKey(string subkey, bool throwOnMissingSubKey = true);

    void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = true);

    object? GetValue(string? name);

    RegistryValueKind GetValueKind(string? name);

    string[] GetValueNames();

    string[] GetSubKeyNames();

    void SetValue(string? name, object value, RegistryValueKind valueKind = RegistryValueKind.String);

    void DeleteValue(string? name, bool throwOnMissingValue = true);

    void Flush();
}
