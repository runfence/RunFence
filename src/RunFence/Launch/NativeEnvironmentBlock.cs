using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Launch;

/// <summary>
/// RAII wrapper for a native environment block pointer, plus static helpers for reading,
/// building, and merging native Unicode environment blocks.
/// When <see cref="IsOverridden"/> is <c>true</c>, the block was allocated via
/// <see cref="Marshal.AllocHGlobal"/> and must be freed with <see cref="Marshal.FreeHGlobal"/>.
/// Otherwise it was created by <c>CreateEnvironmentBlock</c> (userenv.dll) and must be freed
/// with <see cref="ProcessLaunchNative.DestroyEnvironmentBlock"/>.
/// </summary>
/// <remarks>
/// Implemented as a class (not a struct) because it holds an unmanaged native resource:
/// value-type semantics would allow silent copies that share the same pointer, risking double-free
/// or premature release. Reference semantics ensure single ownership and safe transfer across
/// method boundaries (e.g. <c>PrepareEnvironmentBlock</c> return).
/// </remarks>
public class NativeEnvironmentBlock : IDisposable
{
    public IntPtr Pointer { get; private set; }

    /// <summary>
    /// True when the block was allocated via <see cref="Marshal.AllocHGlobal"/> (e.g. after
    /// merging extra environment variables or overriding the profile environment).
    /// False when it was created by <c>CreateEnvironmentBlock</c>.
    /// </summary>
    private bool IsOverridden { get; set; }

    /// <summary>Initializes an empty (zero-pointer) wrapper that owns no resource.</summary>
    public NativeEnvironmentBlock() { }

    public NativeEnvironmentBlock(IntPtr pointer, bool isOverridden)
    {
        Pointer = pointer;
        IsOverridden = isOverridden;
    }

    public void Dispose()
    {
        if (Pointer == IntPtr.Zero)
            return;
        if (IsOverridden)
            Marshal.FreeHGlobal(Pointer);
        else
            ProcessLaunchNative.DestroyEnvironmentBlock(Pointer);
        Pointer = IntPtr.Zero;
    }

    /// <summary>
    /// Reads a native Unicode environment block into a dictionary.
    /// Keys starting with '=' (per-drive CWD entries like "=C:=C:\path") are intentionally skipped.
    /// </summary>
    public static Dictionary<string, string> Read(IntPtr env)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (true)
        {
            var entry = Marshal.PtrToStringUni(IntPtr.Add(env, offset));
            if (string.IsNullOrEmpty(entry))
                break;

            var eq = entry.IndexOf('=');
            // eq > 0 (not >= 0) intentionally skips per-drive CWD entries of the form "=C:=C:\path"
            // where '=' is the first character. These are internal Win32 environment block entries
            // and are not regular environment variables.
            if (eq > 0)
                result[entry[..eq]] = entry[(eq + 1)..];

            offset += (entry.Length + 1) * 2; // Unicode chars
        }

        return result;
    }

    /// <summary>
    /// Allocates a new native Unicode environment block from the given dictionary.
    /// The caller is responsible for freeing the returned pointer via <see cref="Marshal.FreeHGlobal"/>.
    /// </summary>
    public static IntPtr Build(Dictionary<string, string> vars)
    {
        var sb = new StringBuilder();
        foreach (var kvp in vars)
        {
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
            sb.Append('\0');
        }

        sb.Append('\0'); // terminating null

        var bytes = Encoding.Unicode.GetBytes(sb.ToString());
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    /// <summary>
    /// Merges extra environment variables into this block in-place.
    /// Reads the current block, applies the overrides, builds a new <see cref="Marshal.AllocHGlobal"/> block,
    /// frees the old block using the correct deallocation path (<see cref="Marshal.FreeHGlobal"/> when
    /// <see cref="IsOverridden"/>, otherwise <see cref="ProcessLaunchNative.DestroyEnvironmentBlock"/>),
    /// and updates <see cref="Pointer"/> and <see cref="IsOverridden"/> accordingly.
    /// No-op if <paramref name="extraEnvVars"/> is null/empty or <see cref="Pointer"/> is Zero.
    /// </summary>
    public void MergeInPlace(Dictionary<string, string>? extraEnvVars)
    {
        if (Pointer == IntPtr.Zero || extraEnvVars == null || extraEnvVars.Count == 0)
            return;
        var vars = Read(Pointer);
        foreach (var kv in extraEnvVars)
            vars[kv.Key] = kv.Value;
        var newEnv = Build(vars);
        if (IsOverridden)
            Marshal.FreeHGlobal(Pointer);
        else
            ProcessLaunchNative.DestroyEnvironmentBlock(Pointer);
        Pointer = newEnv;
        IsOverridden = true;
    }
}
