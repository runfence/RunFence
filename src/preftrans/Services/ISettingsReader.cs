using PrefTrans.Settings;

namespace PrefTrans.Services;

public interface ISettingsReader
{
    UserSettings ReadAll();
}
