using Microsoft.Win32;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Tests;

/// <summary>
/// Shared helper for tests that need isolated registry hive roots (HKU and HKLM) under HKCU.
/// Creates unique sub-keys per test instance and removes them on <see cref="Dispose"/>.
/// Also provides a pre-configured <see cref="IUserHiveManager"/> mock that reports every
/// SID as loaded (no actual hive load attempted).
/// </summary>
public sealed class RegistryTestHelper : IDisposable
{
    public RegistryKey HkuRoot { get; }
    public RegistryKey HklmRoot { get; }
    public Mock<IUserHiveManager> HiveManager { get; }

    private readonly string _hkuSubKey;
    private readonly string _hklmSubKey;

    public RegistryTestHelper(string hkuPrefix, string hklmPrefix)
    {
        _hkuSubKey = $@"Software\RunFenceTests\{hkuPrefix}_{Guid.NewGuid():N}";
        _hklmSubKey = $@"Software\RunFenceTests\{hklmPrefix}_{Guid.NewGuid():N}";
        HkuRoot = Registry.CurrentUser.CreateSubKey(_hkuSubKey)!;
        HklmRoot = Registry.CurrentUser.CreateSubKey(_hklmSubKey)!;

        HiveManager = new Mock<IUserHiveManager>();
        HiveManager.Setup(h => h.EnsureHiveLoaded(It.IsAny<string>())).Returns((IDisposable?)null);
        HiveManager.Setup(h => h.IsHiveLoaded(It.IsAny<string>())).Returns(true);
    }

    public void Dispose()
    {
        HkuRoot.Dispose();
        HklmRoot.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_hkuSubKey, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(_hklmSubKey, throwOnMissingSubKey: false); } catch { }
    }
}
