using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace RunFence.Core;

public static class DebugHelper
{
#if DEBUG
    public static bool UseAdminOperationMocks { get; } = !new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);
    public const bool IsDebugBuild = true;

    /// <summary>
    /// 8-character hex hash of the exe directory. Non-null only in debug builds — used to isolate
    /// per-instance data directories, pipe names, registry keys, and display names so that multiple
    /// side-by-side debug builds never conflict with each other or with production.
    /// </summary>
    public static readonly string? AppId = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..8];

    /// <summary>Underscore-prefixed <see cref="AppId"/> for use in names (e.g. "RunFence_ABC12345"). Empty string when null.</summary>
    public static readonly string AppIdSuffix = "_" + AppId;
#else
    public static bool UseAdminOperationMocks => false;
    public const bool IsDebugBuild = false;
    public static string? AppId => null;

    /// <summary>Underscore-prefixed <see cref="AppId"/> for use in names. Empty string in release builds.</summary>
    public static string AppIdSuffix => "";
#endif
}
