using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Launching.Environment;

public sealed class EnvironmentBlock : IDisposable
{
    public IntPtr Pointer { get; private set; }

    private Action<IntPtr> _release;

    private EnvironmentBlock(IntPtr pointer, Action<IntPtr> release)
    {
        Pointer = pointer;
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public static EnvironmentBlock Empty()
        => new(IntPtr.Zero, static _ => { });

    public static EnvironmentBlock Own(IntPtr pointer, Action<IntPtr> release)
        => new(pointer, release);

    public static EnvironmentBlock Build(IReadOnlyDictionary<string, string> variables)
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
        return Own(ptr, Marshal.FreeHGlobal);
    }

    public void MergeInPlace(IReadOnlyDictionary<string, string>? extraVariables)
    {
        if (Pointer == IntPtr.Zero || extraVariables is null || extraVariables.Count == 0)
            return;

        var vars = NativeEnvironmentBlockReader.Read(Pointer);
        foreach (var (key, value) in extraVariables)
            vars[key] = value;

        using var newEnv = Build(vars);
        Replace(newEnv.Detach(), Marshal.FreeHGlobal);
    }

    public IntPtr Detach()
    {
        var pointer = Pointer;
        Pointer = IntPtr.Zero;
        return pointer;
    }

    public void Dispose()
    {
        if (Pointer == IntPtr.Zero)
            return;

        var pointer = Pointer;
        Pointer = IntPtr.Zero;
        var release = _release;
        _release = static _ => { };
        release(pointer);
    }

    private void Replace(IntPtr pointer, Action<IntPtr> release)
    {
        Dispose();
        Pointer = pointer;
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }
}
