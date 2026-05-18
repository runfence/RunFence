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
    private readonly ILicenseStore _store;
    private readonly ILicenseValidator _validator;
    private readonly IFeatureRestrictionService _featureRestrictionService;
    private readonly ILicenseMessageFormatter _messageFormatter;
    private readonly ISessionProvider _sessionProvider;
    private readonly ISessionSaver _sessionSaver;
    private readonly IEvaluationCredentialCounter _credentialCounter;
    private readonly string _registryKeyPath;
    private readonly ILoggingService? _log;
    private readonly Lock _lock = new();
    private LicenseInfo _cachedInfo = LicenseInfo.Unlicensed;

    public event Action? LicenseStatusChanged;

    public LicenseService(
        IMachineIdProvider machineIdProvider,
        ILicenseStore store,
        ILicenseValidator validator,
        IFeatureRestrictionService featureRestrictionService,
        ILicenseMessageFormatter messageFormatter,
        ISessionProvider sessionProvider,
        ISessionSaver sessionSaver,
        IEvaluationCredentialCounter credentialCounter,
        ILoggingService? log = null)
        : this(machineIdProvider, store, validator, featureRestrictionService, messageFormatter, PathConstants.LicenseRegistryKey,
            sessionProvider, sessionSaver, credentialCounter, log)
    {
    }

    /// <summary>
    /// Test constructor with injectable paths for isolation.
    /// </summary>
    public LicenseService(
        IMachineIdProvider machineIdProvider,
        ILicenseStore store,
        ILicenseValidator validator,
        IFeatureRestrictionService featureRestrictionService,
        ILicenseMessageFormatter messageFormatter,
        string registryKeyPath,
        ISessionProvider sessionProvider,
        ISessionSaver sessionSaver,
        IEvaluationCredentialCounter credentialCounter,
        ILoggingService? log = null)
    {
        _machineIdProvider = machineIdProvider;
        _store = store;
        _validator = validator;
        _featureRestrictionService = featureRestrictionService;
        _messageFormatter = messageFormatter;
        _sessionProvider = sessionProvider;
        _sessionSaver = sessionSaver;
        _credentialCounter = credentialCounter;
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
        ApplyNagEligibilityLatch();
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

    public string MachineCode => _machineIdProvider.GetMachineIdentity().Status == MachineIdentityStatus.Available
        ? _machineIdProvider.MachineCode
        : "Unavailable";

    public LicenseInfo GetLicenseInfo()
    {
        lock (_lock)
            return _cachedInfo;
    }

    public LicenseActivationResult ActivateLicense(string key)
    {
        var validation = _validator.Validate(key, DateTime.Today);
        if (validation.Status == LicenseValidationStatus.MachineIdentityUnavailable)
            return LicenseActivationResult.MachineIdentityUnavailable;
        if (validation.Status != LicenseValidationStatus.Valid)
            return validation.Status switch
            {
                LicenseValidationStatus.WrongVersion => LicenseActivationResult.WrongVersion,
                LicenseValidationStatus.Expired => LicenseActivationResult.Expired,
                LicenseValidationStatus.SignatureInvalid => LicenseActivationResult.InvalidSignature,
                LicenseValidationStatus.MachineMismatch => LicenseActivationResult.WrongMachine,
                _ => LicenseActivationResult.Malformed
            };

        lock (_lock)
        {
            var saveResult = _store.Save(key);
            if (saveResult.Status != LicenseStoreStatus.Succeeded)
            {
                _log?.Error($"LicenseService: failed to persist license ({saveResult.Status}): {saveResult.ErrorText}");
                return LicenseActivationResult.PersistenceFailed;
            }
            _cachedInfo = validation.ParsedLicenseInfo;
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
                    // Expiry transition is applied by nag/status flow here;
                    // Can* checks and restriction messages stay pure limit evaluation.
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

        var session = _sessionProvider.GetSession();
        if (!session.Database.Settings.NagEligible)
            return false;

        var lastShown = ReadLastNagDate();
        if (lastShown == null)
            return true;

        // Forward time protection: future datetime â†’ treat as invalid â†’ show nag
        if (lastShown.Value > now)
            return true;

        return now - lastShown.Value >= TimeSpan.FromHours(24);
    }

    public void RecordNagShown(DateTime now)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
            key.SetValue(PathConstants.LastNagShownValueName, now.ToString("o"));
        }
        catch
        {
        }
    }

    public bool CanAddApp(int currentCount) =>
        // Pure quota check; no expiry transition or side effects here.
        _featureRestrictionService.GetRestriction(EvaluationFeature.Apps, currentCount, IsLicensed).Allowed;

    public bool CanCreateContainer(int currentCount) =>
        // Pure quota check; no expiry transition or side effects here.
        _featureRestrictionService.GetRestriction(EvaluationFeature.Containers, currentCount, IsLicensed).Allowed;

    public bool CanHideAccount(int currentHiddenCount) =>
        // Pure quota check; no expiry transition or side effects here.
        _featureRestrictionService.GetRestriction(EvaluationFeature.HiddenAccounts, currentHiddenCount, IsLicensed).Allowed;

    public bool CanAddCredential(int currentCredentialCount) =>
        // Pure quota check; no expiry transition or side effects here.
        _featureRestrictionService.GetRestriction(EvaluationFeature.Credentials, currentCredentialCount, IsLicensed).Allowed;

    public bool CanAddFirewallAllowlistEntry(int currentCount) =>
        // Pure quota check; no expiry transition or side effects here.
        _featureRestrictionService.GetRestriction(EvaluationFeature.FirewallAllowlist, currentCount, IsLicensed).Allowed;

    public string? GetRestrictionMessage(EvaluationFeature feature, int currentCount)
    {
        // Restriction messages are derived from current quotas only; they do not mutate expiry state.
        var restriction = _featureRestrictionService.GetRestriction(feature, currentCount, IsLicensed);
        return restriction.Allowed ? null : _messageFormatter.FormatRestrictionMessage(restriction);
    }

    private void LoadFromFile()
    {
        try
        {
            var stored = _store.Load();
            if (stored.Status == LicenseStoreStatus.NotFound)
                return;
            if (stored.Status != LicenseStoreStatus.Succeeded || string.IsNullOrWhiteSpace(stored.LicenseKey))
            {
                _log?.Warn($"LicenseService: license store load failed ({stored.Status}): {stored.ErrorText}");
                return;
            }

            var validation = _validator.Validate(stored.LicenseKey, DateTime.Today);
            if (validation.Status == LicenseValidationStatus.Valid)
                _cachedInfo = validation.ParsedLicenseInfo;
            else
                _log?.Warn($"License validation failed: {validation.Status}");
        }
        catch (Exception ex)
        {
            _log?.Warn($"License load error: {ex.Message}");
        }
    }

    private void DeleteLicenseFile()
    {
        _store.Remove();
    }

    private void ApplyNagEligibilityLatch()
    {
        var session = _sessionProvider.GetSession();
        if (session.Database.Settings.NagEligible)
            return;
        if (session.Database.Apps.Count == 0)
            return;

        var credentialCount = _credentialCounter.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials);
        if (credentialCount == 0)
            return;

        session.Database.Settings.NagEligible = true;
        try
        {
            _sessionSaver.SaveConfig();
        }
        catch (Exception ex)
        {
            _log?.Warn($"LicenseService: failed to persist NagEligible=true: {ex.Message}");
        }
    }

    private DateTime? ReadLastNagDate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath);
            if (key?.GetValue(PathConstants.LastNagShownValueName) is string value && DateTime.TryParse(value, out var date))
                return date;
        }
        catch
        {
        }

        return null;
    }
}
