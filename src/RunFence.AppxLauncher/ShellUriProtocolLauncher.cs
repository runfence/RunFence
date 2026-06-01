using System.ComponentModel;

namespace RunFence.AppxLauncher;

public sealed class ShellUriProtocolLauncher : IShellUriProtocolLauncher
{
    public AppxLaunchResult Launch(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.ShellExecuteFailed,
                "ShellExecute",
                "URI must not be empty.");
        }

        var result = ShellExecuteNative.ShellExecuteW(
            IntPtr.Zero,
            "open",
            uri,
            null,
            null,
            ShellExecuteNative.ShowNormal);
        var code = result.ToInt32();
        if (code > ShellExecuteNative.SuccessThreshold)
            return AppxLaunchResult.Succeeded("ShellExecute");

        var win32Error = new Win32Exception(code).Message;
        return AppxLaunchResult.Failed(
            AppxLaunchExitCode.ShellExecuteFailed,
            "ShellExecute",
            $"ShellExecute failed for URI '{uri}' with result {code}: {win32Error}");
    }
}
