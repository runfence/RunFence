using System.Runtime.InteropServices;

namespace RunFence.Launching.Environment;

public static class NativeEnvironmentBlockReader
{
    public static Dictionary<string, string> Read(IntPtr env)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (env == IntPtr.Zero)
            return result;

        var offset = 0;
        while (true)
        {
            var entry = Marshal.PtrToStringUni(IntPtr.Add(env, offset));
            if (string.IsNullOrEmpty(entry))
                break;

            var eq = entry.IndexOf('=');
            if (eq > 0)
                result[entry[..eq]] = entry[(eq + 1)..];

            offset += (entry.Length + 1) * 2;
        }

        return result;
    }
}
