using System.Security.Cryptography;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class LicenseServiceTests : IDisposable
{
    private static readonly byte[] TestPublicKeyBytes = LicenseTestKey.PublicKeyBytes;

    private readonly string _licenseFilePath = Path.Combine(Path.GetTempPath(), $"license_test_{Guid.NewGuid():N}.dat");
    private readonly string _registryKeyPath = $@"Software\RunFenceTests\{Guid.NewGuid():N}";
    private readonly LicenseValidator _validator = new(TestPublicKeyBytes);

    private static readonly byte[] TestMachineHash = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

    public void Dispose()
    {
        if (File.Exists(_licenseFilePath))
            File.Delete(_licenseFilePath);
        if (File.Exists(_licenseFilePath + ".bak"))
            File.Delete(_licenseFilePath + ".bak");
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    private LicenseService CreateService(byte[]? overrideMachineHash = null)
    {
        var machineProvider = new TestMachineIdProvider(overrideMachineHash ?? TestMachineHash);
        var svc = new LicenseService(machineProvider, _validator, _licenseFilePath, _registryKeyPath);
        svc.Initialize();
        return svc;
    }

    private string BuildValidKey(string name = "Test User", uint expiryDays = 0)
    {
        using var key = LicenseTestKey.CreateSigningKey();
        return TestKeyBuilder.BuildKey(key, TestMachineHash, expiryDays: expiryDays, licenseeName: name);
    }

    [Fact]
    public void ActivateLicense_ValidKey_ReturnsSuccess_AndStoresInFile()
    {
        var svc = CreateService();
        var key = BuildValidKey();
        var result = svc.ActivateLicense(key);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.True(svc.IsLicensed);
        Assert.True(File.Exists(_licenseFilePath));
    }

    [Fact]
    public void ActivateLicense_InvalidKey_ReturnsMalformed()
    {
        var svc = CreateService();
        var result = svc.ActivateLicense("RAME-INVALIDKEY");
        Assert.Equal(LicenseActivationResult.Malformed, result);
        Assert.False(svc.IsLicensed);
    }

    [Fact]
    public void ActivateLicense_ValidKey_FiresLicenseStatusChangedOnce()
    {
        var svc = CreateService();
        var count = 0;
        svc.LicenseStatusChanged += () => count++;
        svc.ActivateLicense(BuildValidKey());
        Assert.Equal(1, count);
    }

    [Fact]
    public void DeactivateLicense_FiresLicenseStatusChangedOnce()
    {
        var svc = CreateService();
        svc.ActivateLicense(BuildValidKey());
        var count = 0;
        svc.LicenseStatusChanged += () => count++;
        svc.DeactivateLicense();
        Assert.Equal(1, count);
        Assert.False(svc.IsLicensed);
    }

    [Fact]
    public void LicenseFile_PersistsAcrossServiceInstances()
    {
        var svc1 = CreateService();
        svc1.ActivateLicense(BuildValidKey());

        var svc2 = CreateService(); // re-reads from file via Initialize()
        Assert.True(svc2.IsLicensed);
    }

    [Fact]
    public void DeactivateLicense_DeletesFile_NewInstanceIsUnlicensed()
    {
        var svc1 = CreateService();
        svc1.ActivateLicense(BuildValidKey());
        Assert.True(svc1.IsLicensed);

        svc1.DeactivateLicense();

        var svc2 = CreateService();
        Assert.False(svc2.IsLicensed);
    }

    // --- Evaluation limit tests ---

    public static IEnumerable<object[]> EvaluationLimitData =>
    [
        ["App", Constants.EvaluationMaxApps],
        ["Container", Constants.EvaluationMaxContainers],
        ["HiddenAccount", Constants.EvaluationMaxHiddenAccounts],
        ["Credential", Constants.EvaluationMaxCredentials],
    ];

    private static bool CanAdd(ILicenseService svc, string feature, int count) => feature switch
    {
        "App" => svc.CanAddApp(count),
        "Container" => svc.CanCreateContainer(count),
        "HiddenAccount" => svc.CanHideAccount(count),
        "Credential" => svc.CanAddCredential(count),
        _ => throw new ArgumentException(feature)
    };

    private static EvaluationFeature ToFeature(string feature) => feature switch
    {
        "App" => EvaluationFeature.Apps,
        "Container" => EvaluationFeature.Containers,
        "HiddenAccount" => EvaluationFeature.HiddenAccounts,
        "Credential" => EvaluationFeature.Credentials,
        _ => throw new ArgumentException(feature)
    };

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_AtLimit_Unlicensed_ReturnsFalse(string feature, int limit)
    {
        var svc = CreateService();
        Assert.False(CanAdd(svc, feature, limit));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_AtLimit_Licensed_ReturnsTrue(string feature, int limit)
    {
        var svc = CreateService();
        svc.ActivateLicense(BuildValidKey());
        Assert.True(CanAdd(svc, feature, limit));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_BelowLimit_Unlicensed_ReturnsTrue(string feature, int limit)
    {
        var svc = CreateService();
        Assert.True(CanAdd(svc, feature, limit - 1));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_OverLimit_StillBlocks(string feature, int limit)
    {
        var svc = CreateService();
        Assert.False(CanAdd(svc, feature, limit + 1));
    }

    // --- GetRestrictionMessage tests ---

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void GetRestrictionMessage_UnderLimit_ReturnsNull(string feature, int limit)
    {
        var svc = CreateService();
        Assert.Null(svc.GetRestrictionMessage(ToFeature(feature), limit - 1));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void GetRestrictionMessage_AtLimit_ReturnsNonNull(string feature, int limit)
    {
        var svc = CreateService();
        var message = svc.GetRestrictionMessage(ToFeature(feature), limit);
        Assert.NotNull(message);
        Assert.Contains(limit.ToString(), message);
    }

    // --- Nag suppression tests ---

    [Fact]
    public void ShouldShowNag_NeverShown_ReturnsTrue()
    {
        var svc = CreateService(); // fresh registry key — no LastNagShownDate
        Assert.True(svc.ShouldShowNag(DateTime.Today));
    }

    [Fact]
    public void ShouldShowNag_ShownToday_ReturnsFalse()
    {
        var svc = CreateService();
        svc.RecordNagShown(DateTime.Today);
        Assert.False(svc.ShouldShowNag(DateTime.Today));
    }

    [Fact]
    public void ShouldShowNag_ShownYesterday_ReturnsTrue()
    {
        var svc = CreateService();
        svc.RecordNagShown(DateTime.Today.AddDays(-1));
        Assert.True(svc.ShouldShowNag(DateTime.Today));
    }

    [Fact]
    public void ShouldShowNag_ForwardDate_ReturnsTrue()
    {
        var svc = CreateService();
        svc.RecordNagShown(DateTime.Today.AddDays(5)); // future date (user tampered)
        Assert.True(svc.ShouldShowNag(DateTime.Today));
    }

    [Fact]
    public void ShouldShowNag_WhenLicensed_ReturnsFalse()
    {
        var svc = CreateService();
        svc.ActivateLicense(BuildValidKey());
        Assert.False(svc.ShouldShowNag(DateTime.Today));
    }

    [Fact]
    public void ShouldShowNag_LicenseExpiresMidSession_ReturnsTrueAndTransitionsToUnlicensed()
    {
        // Simulate: activate a key expiring in 30 days, then call ShouldShowNag 31 days later.
        // This models the in-session expiry scenario (app runs across midnight past expiry).
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expiryDate = DateTime.Today.AddDays(30);
        var expiryDays = (uint)(expiryDate - epoch).TotalDays;
        var svc = CreateService();
        var activationResult = svc.ActivateLicense(BuildValidKey(expiryDays: expiryDays));
        Assert.Equal(LicenseActivationResult.Success, activationResult);
        Assert.True(svc.IsLicensed);

        // Advance time past expiry — ShouldShowNag should detect it and transition
        var statusChangedCount = 0;
        svc.LicenseStatusChanged += () => statusChangedCount++;
        var shouldNag = svc.ShouldShowNag(expiryDate.AddDays(1));

        Assert.True(shouldNag);
        Assert.False(svc.IsLicensed);
        Assert.Equal(1, statusChangedCount);
    }

    /// <summary>Helper mock for tests to avoid WMI calls.</summary>
    private class TestMachineIdProvider(byte[] machineIdHash) : IMachineIdProvider
    {
        public string MachineCode => MachineIdProvider.FormatMachineCode(machineIdHash);
        public byte[] MachineIdHash => (byte[])machineIdHash.Clone();
    }
}