using PrefTrans.Settings;

namespace PrefTrans.Services;

public interface ISettingsWriter
{
    void WriteAll(UserSettings settings);
}
