using PrefTrans.Services.IO;
using PrefTrans.Settings;

namespace PrefTrans.Services;

public class SettingsReader(ISettingsIO[] ioServices, ISettingsFilter settingsFilter) : ISettingsReader
{
    public UserSettings ReadAll()
    {
        var settings = new UserSettings();
        foreach (var io in ioServices)
            io.ReadInto(settings);
        settingsFilter.FilterUserProfilePaths(settings);
        return settings;
    }
}
