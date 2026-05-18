namespace RunFence.Launch;

public interface IWindowsAppsPackagePathRepairer
{
    string? TryRepair(string exePath);
}
