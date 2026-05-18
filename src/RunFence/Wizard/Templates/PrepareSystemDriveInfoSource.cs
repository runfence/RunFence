namespace RunFence.Wizard.Templates;

public class PrepareSystemDriveInfoSource : IPrepareSystemDriveInfoSource
{
    public IReadOnlyList<PrepareSystemDriveInfo> GetNonSystemFixedDrives()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var drives = new List<PrepareSystemDriveInfo>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            var rootPath = TryGetRootPath(drive);
            if (rootPath == null)
                continue;

            try
            {
                if (string.Equals(rootPath, systemDrive, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                drives.Add(CreateInfo(drive));
            }
            catch (Exception ex)
            {
                drives.Add(new PrepareSystemDriveInfo(
                    rootPath,
                    IsReady: false,
                    DriveFormat: null,
                    TotalSize: null,
                    InspectionError: ex.Message));
            }
        }

        return drives;
    }

    public PrepareSystemDriveInfo InspectDrive(string drivePath)
    {
        try
        {
            return CreateInfo(new DriveInfo(drivePath));
        }
        catch (Exception ex)
        {
            return new PrepareSystemDriveInfo(drivePath, IsReady: false, DriveFormat: null, TotalSize: null, InspectionError: ex.Message);
        }
    }

    private static PrepareSystemDriveInfo CreateInfo(DriveInfo drive)
    {
        try
        {
            var isReady = drive.IsReady;
            if (!isReady)
                return new PrepareSystemDriveInfo(drive.RootDirectory.FullName, false, null, null);

            string? driveFormat = null;
            long? totalSize = null;
            try
            {
                driveFormat = drive.DriveFormat;
            }
            catch
            {
            }

            try
            {
                totalSize = drive.TotalSize;
            }
            catch
            {
            }

            return new PrepareSystemDriveInfo(drive.RootDirectory.FullName, true, driveFormat, totalSize);
        }
        catch (Exception ex)
        {
            return new PrepareSystemDriveInfo(
                drive.RootDirectory.FullName,
                IsReady: false,
                DriveFormat: null,
                TotalSize: null,
                InspectionError: ex.Message);
        }
    }

    private static string? TryGetRootPath(DriveInfo drive)
    {
        try
        {
            return drive.RootDirectory.FullName;
        }
        catch
        {
            return null;
        }
    }
}
