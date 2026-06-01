using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public sealed class DesktopAppxActivationLauncher : IDesktopAppxActivationLauncher
{
    private static readonly Guid DesktopAppxActivatorClsid = new("168EB462-775F-42AE-9111-D714B2306C2E");
    private const uint DaxaoNonpackagedExe = 2;
    private const uint DaxaoCheckForAppinstallerUpdates = 16;
    private const uint DaxaoCentennialProcess = 32;
    private const uint DesktopAppxActivationOptions =
        DaxaoNonpackagedExe | DaxaoCheckForAppinstallerUpdates | DaxaoCentennialProcess;

    public AppxLaunchResult Launch(AppxManifestLaunchMetadata metadata, string arguments)
    {
        object? activatorObject = null;
        try
        {
            var activatorType = Type.GetTypeFromCLSID(DesktopAppxActivatorClsid)
                                ?? throw new InvalidOperationException("DesktopAppXActivator COM type is unavailable.");
            activatorObject = Activator.CreateInstance(activatorType)!;
            var activator = (IDesktopAppXActivator)activatorObject;
            activator.ActivateWithOptions(
                metadata.AppUserModelId,
                metadata.Command,
                arguments,
                DesktopAppxActivationOptions,
                0,
                out var processId);

            return AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions", $"Activated process {processId}.");
        }
        catch (Exception ex)
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.DesktopAppxActivationFailed,
                "DesktopAppxActivateWithOptions",
                ex);
        }
        finally
        {
            if (activatorObject != null && Marshal.IsComObject(activatorObject))
                Marshal.FinalReleaseComObject(activatorObject);
        }
    }
}
