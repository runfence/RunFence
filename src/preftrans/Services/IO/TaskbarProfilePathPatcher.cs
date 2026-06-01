using System.Text;

namespace PrefTrans.Services.IO;

public class TaskbarProfilePathPatcher
{
    public byte[]? PatchProfilePath(byte[]? blob, string sourceProfile, string targetProfile)
    {
        if (blob == null)
            return null;

        var sourceBytes = Encoding.Unicode.GetBytes(sourceProfile);
        var targetBytes = Encoding.Unicode.GetBytes(targetProfile);
        int bytesDelta = targetBytes.Length - sourceBytes.Length;
        int charsDelta = bytesDelta / 2;

        if (IndexOfBytes(blob, sourceBytes, 0) < 0)
            return blob;

        var result = new List<byte>(blob.Length + Math.Max(0, bytesDelta) * 8);
        int position = 0;

        while (position < blob.Length)
        {
            int matchPosition = IndexOfBytes(blob, sourceBytes, position);
            if (matchPosition < 0)
            {
                result.AddRange(new ArraySegment<byte>(blob, position, blob.Length - position));
                break;
            }

            int bytesToCopy = matchPosition - position;
            result.AddRange(new ArraySegment<byte>(blob, position, bytesToCopy));

            if (bytesDelta != 0 && matchPosition >= 2 && bytesToCopy >= 2)
            {
                int oldCharCount = blob[matchPosition - 2] | (blob[matchPosition - 1] << 8);
                int newCharCount = oldCharCount + charsDelta;
                if (oldCharCount >= sourceProfile.Length && oldCharCount <= 4096 &&
                    newCharCount is > 0 and <= 4096)
                {
                    int countIndex = result.Count;
                    result[countIndex - 2] = (byte)(newCharCount & 0xFF);
                    result[countIndex - 1] = (byte)((newCharCount >> 8) & 0xFF);
                }
            }

            result.AddRange(targetBytes);
            position = matchPosition + sourceBytes.Length;
        }

        return result.ToArray();
    }

    public bool ContainsPathUtf16(byte[]? data, string path)
    {
        if (data == null || data.Length < 2 || string.IsNullOrEmpty(path))
            return false;

        var text = Encoding.Unicode.GetString(data);
        return text.Contains(path, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryResolvePinnedShortcutDestinationPath(string taskBarFolder, string importedName, out string destinationPath)
    {
        destinationPath = string.Empty;

        var fileName = importedName.Trim();
        if (fileName.Length == 0)
            return false;
        if (Path.IsPathRooted(fileName))
            return false;
        if (fileName.IndexOf(Path.DirectorySeparatorChar) >= 0 || fileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            return false;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        if (!string.Equals(Path.GetExtension(fileName), ".lnk", StringComparison.OrdinalIgnoreCase))
            return false;

        var fullTaskbarFolder = Path.GetFullPath(taskBarFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidatePath = Path.GetFullPath(Path.Combine(fullTaskbarFolder, fileName));
        var folderPrefix = fullTaskbarFolder + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        destinationPath = candidatePath;
        return true;
    }

    private static int IndexOfBytes(byte[] data, byte[] pattern, int startPosition)
    {
        int end = data.Length - pattern.Length;
        for (int i = startPosition; i <= end; i++)
        {
            bool matches = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] == pattern[j])
                    continue;

                matches = false;
                break;
            }

            if (matches)
                return i;
        }

        return -1;
    }
}
