namespace RunFence.Security;

public interface IInputInjectionBlockerService
{
    bool IsEnabled { get; }
    void ApplyConfigSetting(bool blockInputInjection);
    void SetTemporarilyDisabled();
    void SetTimedDisable(TimeSpan duration);
    void ReEnable();
    void UpdateExemptedSids(IReadOnlyCollection<string> sids);
}
