using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public interface ISettingsIO
{
    void ReadInto(UserSettings settings);
    void WriteFrom(UserSettings settings);
}
