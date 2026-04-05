using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Licensing;

public interface ILicenseService
{
    bool IsLicensed { get; }
    string MachineCode { get; }
    void Initialize();
    LicenseInfo GetLicenseInfo();
    LicenseActivationResult ActivateLicense(string key);
    void DeactivateLicense();
    bool ShouldShowNag(DateTime now);
    void RecordNagShown(DateTime now);
    bool CanAddApp(int currentCount);
    bool CanCreateContainer(int currentCount);
    bool CanHideAccount(int currentHiddenCount);
    bool CanAddCredential(int currentCredentialCount);
    bool CanAddFirewallAllowlistEntry(int currentCount);
    string? GetRestrictionMessage(EvaluationFeature feature, int currentCount);
    event Action? LicenseStatusChanged;
}

internal class LicenseService : ILicenseService, IRequiresInitialization
{
    private readonly IMachineIdProvider _machineIdProvider;
    private readonly LicenseValidator _validator;
    private readonly string _licenseFilePath;
    private readonly string _registryKeyPath;
    private readonly ILoggingService? _log;
    private readonly Lock _lock = new();
    private LicenseInfo _cachedInfo = LicenseInfo.Unlicensed;

    public event Action? LicenseStatusChanged;

    public LicenseService(IMachineIdProvider machineIdProvider, LicenseValidator validator,
        ILoggingService log)
        : this(machineIdProvider, validator, Constants.LicenseFilePath, Constants.LicenseRegistryKey, log)
    {
    }

    /// <summary>
    /// Test constructor with injectable paths for isolation.
    /// </summary>
    public LicenseService(
        IMachineIdProvider machineIdProvider,
        LicenseValidator validator,
        string licenseFilePath,
        string registryKeyPath,
        ILoggingService? log = null)
    {
        _machineIdProvider = machineIdProvider;
        _validator = validator;
        _licenseFilePath = licenseFilePath;
        _registryKeyPath = registryKeyPath;
        _log = log;
    }

    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _log?.Info("LicenseService: initializing.");
        LoadFromFile();
        _log?.Info($"LicenseService: initialized ({(IsLicensed ? "licensed" : "evaluation mode")}).");
    }

    public bool IsLicensed
    {
        get
        {
            lock (_lock)
                return _cachedInfo.IsValid;
        }
    }

    public string MachineCode => _machineIdProvider.MachineCode;

    public LicenseInfo GetLicenseInfo()
    {
        lock (_lock)
            return _cachedInfo;
    }

    public LicenseActivationResult ActivateLicense(string key)
    {
        var (result, info) = _validator.Validate(key, _machineIdProvider.MachineIdHash, DateTime.Today);
        if (result != LicenseActivationResult.Success)
            return result;

        lock (_lock)
        {
            SaveLicenseFile(key);
            _cachedInfo = info;
        }

        LicenseStatusChanged?.Invoke();
        return LicenseActivationResult.Success;
    }

    public void DeactivateLicense()
    {
        lock (_lock)
        {
            DeleteLicenseFile();
            _cachedInfo = LicenseInfo.Unlicensed;
        }

        LicenseStatusChanged?.Invoke();
    }

    public bool ShouldShowNag(DateTime now)
    {
        bool justExpired = false;
        lock (_lock)
        {
            if (_cachedInfo.IsValid)
            {
                if (_cachedInfo.ExpiryDate.HasValue && now.Date > _cachedInfo.ExpiryDate.Value.Date)
                {
                    // License expired: transition to evaluation mode and clean up the file
                    _cachedInfo = LicenseInfo.Unlicensed;
                    DeleteLicenseFile();
                    justExpired = true;
                }
                else
                    return false;
            }
        }

        // Fire outside the lock to avoid deadlock
        if (justExpired)
            LicenseStatusChanged?.Invoke();

        var lastShown = ReadLastNagDate();
        if (lastShown == null)
            return true;
        // Forward time protection: future datetime → treat as invalid → show nag
        if (lastShown.Value > now)
            return true;
        return now - lastShown.Value >= TimeSpan.FromHours(24);
    }

    public void RecordNagShown(DateTime now)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
            key?.SetValue(Constants.LastNagShownValueName, now.ToString("o"));
        }
        catch
        {
        }
    }

    public bool CanAddApp(int currentCount) =>
        IsLicensed || currentCount < Constants.EvaluationMaxApps;

    public bool CanCreateContainer(int currentCount) =>
        IsLicensed || currentCount < Constants.EvaluationMaxContainers;

    public bool CanHideAccount(int currentHiddenCount) =>
        IsLicensed || currentHiddenCount < Constants.EvaluationMaxHiddenAccounts;

    public bool CanAddCredential(int currentCredentialCount) =>
        IsLicensed || currentCredentialCount < Constants.EvaluationMaxCredentials;

    public bool CanAddFirewallAllowlistEntry(int currentCount) =>
        IsLicensed || currentCount < Constants.EvaluationMaxFirewallAllowlistEntries;

    public string? GetRestrictionMessage(EvaluationFeature feature, int currentCount)
    {
        return feature switch
        {
            EvaluationFeature.Apps when !CanAddApp(currentCount) =>
                $"Evaluation mode allows up to {Constants.EvaluationMaxApps} app entries. Activate a license to remove this limit.",
            EvaluationFeature.Containers when !CanCreateContainer(currentCount) =>
                $"Evaluation mode allows up to {Constants.EvaluationMaxContainers} AppContainer profiles. Activate a license to remove this limit.",
            EvaluationFeature.HiddenAccounts when !CanHideAccount(currentCount) =>
                $"Evaluation mode allows up to {Constants.EvaluationMaxHiddenAccounts} hidden accounts. Activate a license to remove this limit.",
            EvaluationFeature.Credentials when !CanAddCredential(currentCount) =>
                $"Evaluation mode allows up to {Constants.EvaluationMaxCredentials} stored credentials. Activate a license to remove this limit.",
            EvaluationFeature.FirewallAllowlist when !CanAddFirewallAllowlistEntry(currentCount) =>
                $"Evaluation mode allows up to {Constants.EvaluationMaxFirewallAllowlistEntries} firewall allowlist entries. Activate a license to remove this limit.",
            _ => null
        };
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
            {
                _log?.Warn($"License file not found: {_licenseFilePath}");
                return;
            }

            var key = File.ReadAllText(_licenseFilePath).Trim();
            if (string.IsNullOrEmpty(key))
            {
                _log?.Warn("License file is empty.");
                return;
            }

            var (result, info) = _validator.Validate(key, _machineIdProvider.MachineIdHash, DateTime.Today);
            if (result == LicenseActivationResult.Success)
                _cachedInfo = info;
            else
                _log?.Warn($"License validation failed: {result}");
        }
        catch (Exception ex)
        {
            _log?.Warn($"License load error: {ex.Message}");
        }
    }

    private void SaveLicenseFile(string key)
    {
        var dir = Path.GetDirectoryName(_licenseFilePath)!;
        Directory.CreateDirectory(dir);
        var tmp = _licenseFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, key);
        if (File.Exists(_licenseFilePath))
            File.Replace(tmp, _licenseFilePath, _licenseFilePath + ".bak");
        else
            File.Move(tmp, _licenseFilePath);
    }

    private void DeleteLicenseFile()
    {
        try
        {
            if (File.Exists(_licenseFilePath))
                File.Delete(_licenseFilePath);
        }
        catch
        {
        }
    }

    private DateTime? ReadLastNagDate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath);
            if (key?.GetValue(Constants.LastNagShownValueName) is string value && DateTime.TryParse(value, out var date))
                return date;
        }
        catch
        {
        }

        return null;
    }
}