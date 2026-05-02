using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Launching.Environment;

public static class EnvironmentBlockBuilder
{
    public static IntPtr Build(IReadOnlyDictionary<string, string> variables)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in variables)
        {
            sb.Append(key);
            sb.Append('=');
            sb.Append(value);
            sb.Append('\0');
        }

        sb.Append('\0');

        var bytes = Encoding.Unicode.GetBytes(sb.ToString());
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }
}
