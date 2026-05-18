namespace RunFence.Account.OrphanedProfiles;

public class ProfileSizeCalculator : IProfileSizeCalculator
{
    public long CalculateSizeBytes(string profilePath, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        const long bytesPerMegabyte = 1024L * 1024L;

        if (!Directory.Exists(profilePath))
        {
            progress?.Report(0);
            return 0;
        }

        long totalBytes = 0;
        long lastReportedMegabytes = -1;
        var pendingPaths = new Stack<string>();
        pendingPaths.Push(profilePath);

        progress?.Report(0);
        lastReportedMegabytes = 0;

        while (pendingPaths.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPath = pendingPaths.Pop();

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(currentPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            if ((attributes & FileAttributes.Directory) != 0)
            {
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(currentPath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                using var enumerator = entries.GetEnumerator();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    bool hasNext;
                    try
                    {
                        hasNext = enumerator.MoveNext();
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        break;
                    }

                    if (!hasNext)
                        break;

                    pendingPaths.Push(enumerator.Current);
                }

                continue;
            }

            try
            {
                totalBytes += new FileInfo(currentPath).Length;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            var currentMegabytes = totalBytes / bytesPerMegabyte;
            if (currentMegabytes == lastReportedMegabytes)
                continue;

            progress?.Report(currentMegabytes);
            lastReportedMegabytes = currentMegabytes;
        }

        return totalBytes;
    }
}
