using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public interface ISettingsFilter
{
    void FilterUserProfilePaths(UserSettings settings);
}
