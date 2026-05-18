namespace RunFence.Wizard.Templates;

public interface IPrepareSystemDriveInfoSource
{
    IReadOnlyList<PrepareSystemDriveInfo> GetNonSystemFixedDrives();

    PrepareSystemDriveInfo InspectDrive(string drivePath);
}
