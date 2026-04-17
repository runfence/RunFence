using PrefTrans.Services.IO;
using PrefTrans.Settings;

namespace PrefTrans.Services;

public class SettingsWriter(ISettingsIO[] ioServices) : ISettingsWriter
{
    public void WriteAll(UserSettings settings)
    {
        foreach (var io in ioServices)
            io.WriteFrom(settings);
    }
}
