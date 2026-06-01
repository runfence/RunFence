using System.Xml;
using System.Xml.Linq;
using RunFence.Core;
using RunFence.Launching.Resolution;

namespace RunFence.AppxLauncher;

public sealed class AppxManifestLaunchMetadataResolver : IAppxManifestLaunchMetadataResolver
{
    private const string Desktop4ManifestNamespace = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private const string Uap10ManifestNamespace = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";

    public AppxManifestLaunchMetadataResult Resolve(string appxExecutablePath, string arguments)
    {
        try
        {
            if (!WindowsAppsPackagePathParser.TryParsePackagePath(appxExecutablePath, out var packagePath))
            {
                return AppxManifestLaunchMetadataResult.Failed(AppxLaunchResult.Failed(
                    AppxLaunchExitCode.ManifestResolutionFailed,
                    "ResolvePackagePath",
                    $"AppX executable path is not under a valid WindowsApps package root: '{appxExecutablePath}'."));
            }

            var manifestPath = Path.Combine(
                packagePath.InstallRoot,
                packagePath.PackageFullName,
                "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return AppxManifestLaunchMetadataResult.Failed(AppxLaunchResult.Failed(
                    AppxLaunchExitCode.ManifestResolutionFailed,
                    "ReadManifest",
                    $"AppX manifest does not exist: '{manifestPath}'."));
            }

            var manifest = XDocument.Load(manifestPath, LoadOptions.None);
            var applications = manifest
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Application", StringComparison.Ordinal))
                .ToList();
            var application = applications.FirstOrDefault(element =>
                PathEquals((string?)element.Attribute("Executable"), packagePath.RelativeExecutablePath));
            if (application == null)
            {
                return AppxManifestLaunchMetadataResult.Failed(AppxLaunchResult.Failed(
                    AppxLaunchExitCode.ManifestResolutionFailed,
                    "ResolveApplication",
                    $"AppX manifest '{manifestPath}' does not contain an Application entry for '{packagePath.RelativeExecutablePath}'."));
            }

            var applicationId = ((string?)application.Attribute("Id"))?.Trim();
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                return AppxManifestLaunchMetadataResult.Failed(AppxLaunchResult.Failed(
                    AppxLaunchExitCode.ManifestResolutionFailed,
                    "ResolveApplicationId",
                    $"AppX manifest '{manifestPath}' contains an Application entry without an Id."));
            }

            var protocol = SelectProtocol(application.Descendants(), arguments);
            var entryPoint = ((string?)application.Attribute("EntryPoint"))?.Trim();
            var supportsMultipleInstances = application.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "SupportsMultipleInstances", StringComparison.Ordinal) &&
                (string.Equals(attribute.Name.NamespaceName, Desktop4ManifestNamespace, StringComparison.Ordinal) ||
                 string.Equals(attribute.Name.NamespaceName, Uap10ManifestNamespace, StringComparison.Ordinal)) &&
                string.Equals(attribute.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase));
            var metadata = new AppxManifestLaunchMetadata(
                packagePath.PackageFamilyName,
                $"{packagePath.PackageFamilyName}!{applicationId}",
                appxExecutablePath,
                protocol,
                string.Equals(entryPoint, "Windows.FullTrustApplication", StringComparison.Ordinal),
                supportsMultipleInstances);
            return AppxManifestLaunchMetadataResult.Succeeded(metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return AppxManifestLaunchMetadataResult.Failed(AppxLaunchResult.Failed(
                AppxLaunchExitCode.ManifestResolutionFailed,
                "ReadManifest",
                ex));
        }
    }

    private static bool PathEquals(string? manifestPath, string packageRelativePath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            return false;

        var normalizedManifestPath = manifestPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();
        var normalizedPackagePath = packageRelativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();

        return string.Equals(normalizedManifestPath, normalizedPackagePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? SelectProtocol(IEnumerable<XElement> applicationDescendants, string arguments)
    {
        var protocolNames = applicationDescendants
            .Where(element => string.Equals(element.Name.LocalName, "Protocol", StringComparison.Ordinal))
            .Select(element => element.Attribute("Name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
        if (protocolNames.Count == 0)
            return null;

        var preferredProtocol = TryReadLeadingProtocol(arguments);
        if (!string.IsNullOrWhiteSpace(preferredProtocol))
        {
            var preferredMatch = protocolNames.FirstOrDefault(name =>
                string.Equals(name, preferredProtocol, StringComparison.OrdinalIgnoreCase));
            if (preferredMatch != null)
                return preferredMatch;
        }

        return protocolNames[0];
    }

    private static string? TryReadLeadingProtocol(string arguments)
    {
        var parsedArguments = CommandLineHelper.ParseProcessArguments(arguments);
        if (parsedArguments.Length == 0)
            return null;

        var firstArgument = parsedArguments[0];
        var separatorIndex = firstArgument.IndexOf(':');
        if (separatorIndex <= 0)
            return null;

        var protocol = firstArgument[..separatorIndex];
        if (!char.IsAsciiLetter(protocol[0]))
            return null;

        for (var i = 1; i < protocol.Length; i++)
        {
            var character = protocol[i];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '-' and not '.')
                return null;
        }

        return protocol;
    }
}
