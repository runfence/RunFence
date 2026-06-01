using Moq;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;

namespace RunFence.Tests;

/// <summary>
/// Shared helper for tests that need isolated in-memory registry hive roots (HKU and HKLM).
/// Also provides a pre-configured <see cref="IUserHiveManager"/> mock that reports every
/// SID as loaded (no actual hive load attempted).
/// </summary>
public sealed class RegistryTestHelper : IDisposable
{
    public InMemoryRegistryKey HkuRoot { get; }
    public InMemoryRegistryKey HklmRoot { get; }
    public Mock<IUserHiveManager> HiveManager { get; }

    public RegistryTestHelper(string hkuPrefix, string hklmPrefix)
    {
        HkuRoot = InMemoryRegistryKey.CreateRoot(hkuPrefix);
        HklmRoot = InMemoryRegistryKey.CreateRoot(hklmPrefix);

        HiveManager = new Mock<IUserHiveManager>();
        HiveManager.Setup(h => h.EnsureHiveLoaded(It.IsAny<string>())).Returns((IDisposable?)null);
        HiveManager.Setup(h => h.IsHiveLoaded(It.IsAny<string>())).Returns(true);
    }

    public void Dispose()
    {
        HkuRoot.Dispose();
        HklmRoot.Dispose();
    }
}
