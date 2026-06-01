using Microsoft.Win32;

namespace RunFence.Core;

public sealed class WindowsRegistryKey(RegistryKey inner) : IRegistryKey
{
    public string Name => inner.Name;

    public int SubKeyCount => inner.SubKeyCount;

    public int ValueCount => inner.ValueCount;

    public IRegistryKey? OpenSubKey(string name, bool writable = false)
        => inner.OpenSubKey(name, writable) is { } key ? new WindowsRegistryKey(key) : null;

    public IRegistryKey CreateSubKey(string subkey)
        => new WindowsRegistryKey(inner.CreateSubKey(subkey)
                                  ?? throw new InvalidOperationException($"Failed to create registry key: {subkey}"));

    public void DeleteSubKey(string subkey, bool throwOnMissingSubKey = true)
        => inner.DeleteSubKey(subkey, throwOnMissingSubKey);

    public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = true)
        => inner.DeleteSubKeyTree(subkey, throwOnMissingSubKey);

    public object? GetValue(string? name)
        => inner.GetValue(name);

    public RegistryValueKind GetValueKind(string? name)
        => inner.GetValueKind(name);

    public string[] GetValueNames()
        => inner.GetValueNames();

    public string[] GetSubKeyNames()
        => inner.GetSubKeyNames();

    public void SetValue(string? name, object value, RegistryValueKind valueKind = RegistryValueKind.String)
        => inner.SetValue(name, value, valueKind);

    public void DeleteValue(string? name, bool throwOnMissingValue = true)
        => inner.DeleteValue(name ?? string.Empty, throwOnMissingValue);

    public void Flush()
        => inner.Flush();

    public void Dispose()
        => inner.Dispose();
}
