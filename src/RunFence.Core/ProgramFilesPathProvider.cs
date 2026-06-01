namespace RunFence.Core;

public sealed class ProgramFilesPathProvider : IProgramFilesPathProvider
{
    public IReadOnlyList<string> GetProgramFilesRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        return roots.ToArray();
    }

    private static void AddIfPresent(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            roots.Add(NormalizeRoot(path));
        }
        catch
        {
        }
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
