using RunFence.Core;

namespace RunFence.Persistence;

/// <summary>
/// Production implementation that uses the interactive-user startup folder,
/// falling back to <see cref="Environment.SpecialFolder.Startup"/>, and reads
/// paths from <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public class AutoStartShortcutStore(IProfilePathResolver profilePathResolver) : IAutoStartShortcutStore
{
    private static readonly string ShortcutName = !string.IsNullOrEmpty(DebugHelper.AppId)
        ? $"RunFence ({DebugHelper.AppId}).lnk"
        : "RunFence.lnk";

    public string RunFenceExePath =>
        Path.Combine(AppContext.BaseDirectory, "RunFence.exe");

    public string CmdWrapperPath =>
        Path.Combine(AppContext.BaseDirectory, "RunFence-autostart.cmd");

    public string PrimaryShortcutPath =>
        Path.Combine(GetInteractiveUserStartupFolder(), ShortcutName);

    public IReadOnlyCollection<string> ShortcutPaths =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PrimaryShortcutPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName)
        };

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);

    private string GetInteractiveUserStartupFolder()
    {
        try
        {
            var sid = NativeTokenHelper.TryGetInteractiveUserSid();
            if (sid != null)
            {
                var profilePath = profilePathResolver.TryGetProfilePath(sid.Value);
                if (profilePath != null)
                {
                    return Path.Combine(profilePath,
                        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
                }
            }
        }
        catch
        {
            /* fall through to default */
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }
}
