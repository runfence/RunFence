using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IGlobalIcmpSettingsApplier
{
    void ApplyGlobalIcmpSetting(AppDatabase database);
}
