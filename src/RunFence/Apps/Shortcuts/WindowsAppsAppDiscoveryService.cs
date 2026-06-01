using System.Xml.Linq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launching.Resolution;

namespace RunFence.Apps.Shortcuts;

public sealed class WindowsAppsAppDiscoveryService(
    IProgramFilesPathProvider programFilesPathProvider,
    IAppxPackageQueryService packageQueryService,
    IFileContentService fileContentService)
    : IWindowsAppsAppDiscoveryService
{
    public IReadOnlyList<DiscoveredApp> DiscoverApps()
    {
        var latestApps = new Dictionary<string, CandidateApp>(StringComparer.OrdinalIgnoreCase);
        var programFilesRoots = GetProgramFilesRoots();

        foreach (var package in packageQueryService.QueryPackages())
        {
            if (!TryNormalizeInstallLocation(package.InstallLocation, programFilesRoots, out var installLocation))
                continue;

            AddPackageApps(package, installLocation, latestApps);
        }

        return latestApps.Values
            .Select(app => new DiscoveredApp(app.Name, app.TargetPath))
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private HashSet<string> GetProgramFilesRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var programFilesRoot in programFilesPathProvider.GetProgramFilesRoots())
        {
            try
            {
                roots.Add(Path.GetFullPath(programFilesRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
            }
        }

        return roots;
    }

    private void AddPackageApps(
        RegisteredAppxPackage package,
        string installLocation,
        Dictionary<string, CandidateApp> latestApps)
    {
        if (!WindowsAppsPackagePathParser.TryParsePackageFolderName(package.PackageFullName, out _, out var version, out _, out _))
            return;

        var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
        var manifest = TryLoadManifest(manifestPath);
        if (manifest == null)
            return;

        foreach (var application in GetApplications(installLocation, manifest))
        {
            var logicalKey = package.PackageFamilyName + "|" + application.RelativeExecutablePath;
            if (latestApps.TryGetValue(logicalKey, out var current) && current.Version.CompareTo(version) >= 0)
                continue;

            latestApps[logicalKey] = new CandidateApp(application.Name, application.TargetPath, version);
        }
    }

    private XDocument? TryLoadManifest(string manifestPath)
    {
        try
        {
            using var stream = fileContentService.OpenRead(manifestPath);
            return XDocument.Load(stream, LoadOptions.None);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<ManifestApplication> GetApplications(string packageDirectory, XDocument manifest)
    {
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var application in manifest.Descendants().Where(element => element.Name.LocalName == "Application"))
        {
            var executable = application.Attribute("Executable")?.Value;
            if (!TryBuildTargetPath(packageDirectory, executable, out var targetPath, out var relativeExecutablePath))
                continue;

            if (!seenTargets.Add(targetPath))
                continue;

            yield return new ManifestApplication(GetApplicationName(application, targetPath), relativeExecutablePath, targetPath);
        }
    }

    private bool TryBuildTargetPath(
        string packageDirectory,
        string? executable,
        out string targetPath,
        out string relativeExecutablePath)
    {
        targetPath = string.Empty;
        relativeExecutablePath = string.Empty;

        if (string.IsNullOrWhiteSpace(executable))
            return false;

        var normalizedRelativePath = executable
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();
        if (!normalizedRelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(normalizedRelativePath))
        {
            return false;
        }

        try
        {
            var normalizedPackageDirectory = Path.GetFullPath(packageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidateTargetPath = Path.GetFullPath(Path.Combine(normalizedPackageDirectory, normalizedRelativePath));
            if (!candidateTargetPath.StartsWith(normalizedPackageDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!File.Exists(candidateTargetPath))
                return false;

            targetPath = candidateTargetPath;
            relativeExecutablePath = normalizedRelativePath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryNormalizeInstallLocation(
        string installLocation,
        HashSet<string> programFilesRoots,
        out string normalizedInstallLocation)
    {
        normalizedInstallLocation = string.Empty;

        if (string.IsNullOrWhiteSpace(installLocation))
            return false;

        try
        {
            normalizedInstallLocation = Path.GetFullPath(installLocation)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        foreach (var programFilesRoot in programFilesRoots)
        {
            if (normalizedInstallLocation.StartsWith(programFilesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetApplicationName(XElement application, string targetPath)
    {
        var displayName = TryGetPlainDisplayName(application.Attribute("DisplayName")?.Value);
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        var visualElementsName = application
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.EndsWith("VisualElements", StringComparison.Ordinal))
            ?.Attribute("DisplayName")
            ?.Value;
        displayName = TryGetPlainDisplayName(visualElementsName);
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        return Path.GetFileNameWithoutExtension(targetPath);
    }

    private static string? TryGetPlainDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? null
            : displayName.Trim();
    }

    private sealed record CandidateApp(string Name, string TargetPath, Version Version);

    private sealed record ManifestApplication(string Name, string RelativeExecutablePath, string TargetPath);
}
