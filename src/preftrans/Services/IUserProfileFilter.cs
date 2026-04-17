namespace PrefTrans.Services;

public interface IUserProfileFilter
{
    string[] GetUserProfilePaths();
    bool ContainsUserProfilePath(string? value, string[] profilePaths);
    bool ContainsWindowsAppsPath(string? value);
    bool IsUwpProgId(string? progId);
}
