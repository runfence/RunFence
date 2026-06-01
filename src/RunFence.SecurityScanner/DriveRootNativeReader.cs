using System.Runtime.InteropServices;

namespace RunFence.SecurityScanner;

public interface IDriveRootNativeReader
{
    IEnumerable<string> GetDriveRoots();
}

public class DriveRootNativeReader : IDriveRootNativeReader
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string lpDeviceName, char[]? lpTargetPath, uint ucchMax);

    public IEnumerable<string> GetDriveRoots()
    {
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;
            if (drive.DriveType == DriveType.Network)
                continue;

            var root = drive.RootDirectory.FullName;
            if (string.Equals(root, systemDrive, StringComparison.OrdinalIgnoreCase))
                continue;

            var driveName = root.TrimEnd('\\');
            var buf = new char[256];
            var len = QueryDosDevice(driveName, buf, (uint)buf.Length);
            if (len > 0)
            {
                var firstNull = Array.IndexOf(buf, '\0', 0, (int)len);
                var devicePath = firstNull >= 0 ? new string(buf, 0, firstNull) : new string(buf, 0, (int)len);
                if (devicePath.StartsWith(@"\??\", StringComparison.Ordinal))
                    continue;
            }

            yield return root;
        }
    }
}
