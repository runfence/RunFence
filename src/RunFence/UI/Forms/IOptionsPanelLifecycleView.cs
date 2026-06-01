using RunFence.Core.Models;

namespace RunFence.UI.Forms;

public interface IOptionsPanelLifecycleView
{
    bool IsDisposed { get; }
    bool StartWithoutPinEnabled { get; }
    void BuildDynamicContent();
    void SetCallerSidNames(IReadOnlyDictionary<string, string> sidNames, Action<string, string> onSidNameLearned);
    void ApplyLoadedState(OptionsPanelState state, bool startWithoutPinEnabled, bool blockIcmpWhenInternetBlocked);
    void RefreshCallerAndConfigLists();
    void SaveSettings();
}
