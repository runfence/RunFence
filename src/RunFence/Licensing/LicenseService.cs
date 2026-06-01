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
    private readonly ILicenseNagService _licenseNagService;
    private readonly LicenseNagEligibilityService _licenseNagEligibilityService;
    private readonly ISessionProvider _sessionProvider;
    private readonly ILoggingService? _log;
    private readonly Lock _lock = new();
    private LicenseInfo _cachedInfo = LicenseInfo.Unlicensed;
    private bool _initialized;

    public event Action? LicenseStatusChanged;

    public LicenseService(
        IMachineIdProvider machineIdProvider,
        ILicenseStore store,
        ILicenseValidator validator,
        IFeatureRestrictionService featureRestrictionService,
        ILicenseMessageFormatter messageFormatter,
        ILicenseNagService licenseNagService,
        LicenseNagEligibilityService licenseNagEligibilityService,
        ISessionProvider sessionProvider,
        ILoggingService? log = null)
    {
        _machineIdProvider = machineIdProvider;
        _store = store;
        _validator = validator;
        _featureRestrictionService = featureRestrictionService;
        _messageFormatter = messageFormatter;
        _licenseNagService = licenseNagService;
        _licenseNagEligibilityService = licenseNagEligibilityService;
        _sessionProvider = sessionProvider;
        _log = log;
    }

    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        _log?.Info("LicenseService: initializing.");
        LoadFromFile();
        _licenseNagEligibilityService.ApplyNagEligibilityLatch();
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
            _store.Remove();
            _cachedInfo = LicenseInfo.Unlicensed;
        }

        LicenseStatusChanged?.Invoke();
    }

    public bool ShouldShowNag(DateTime now)
    {
        var raiseStatusChanged = false;
        lock (_lock)
        {
            if (_cachedInfo.IsValid)
            {
                if (_cachedInfo.ExpiryDate.HasValue && now.Date > _cachedInfo.ExpiryDate.Value.Date)
                {
                    _cachedInfo = LicenseInfo.Unlicensed;
                    _store.Remove();
                    raiseStatusChanged = true;
                }
                else
                {
                    return false;
                }
            }
        }

        if (raiseStatusChanged)
            LicenseStatusChanged?.Invoke();

        var session = _sessionProvider.GetSession();
        if (!_licenseNagEligibilityService.IsSessionEligibleForNag(session.Database))
            return false;

        return _licenseNagService.ShouldShowNagByCadence(now);
    }

    public void RecordNagShown(DateTime now) => _licenseNagService.RecordNagShown(now);

    public bool CanAddApp(int currentCount) =>
        _featureRestrictionService.GetRestriction(EvaluationFeature.Apps, currentCount, IsLicensed).Allowed;

    public bool CanCreateContainer(int currentCount) =>
        _featureRestrictionService.GetRestriction(EvaluationFeature.Containers, currentCount, IsLicensed).Allowed;

    public bool CanHideAccount(int currentHiddenCount) =>
        _featureRestrictionService.GetRestriction(EvaluationFeature.HiddenAccounts, currentHiddenCount, IsLicensed).Allowed;

    public bool CanAddCredential(int currentCredentialCount) =>
        _featureRestrictionService.GetRestriction(EvaluationFeature.Credentials, currentCredentialCount, IsLicensed).Allowed;

    public bool CanAddFirewallAllowlistEntry(int currentCount) =>
        _featureRestrictionService.GetRestriction(EvaluationFeature.FirewallAllowlist, currentCount, IsLicensed).Allowed;

    public string? GetRestrictionMessage(EvaluationFeature feature, int currentCount)
    {
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
}
