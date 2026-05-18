using System.Reflection;

namespace RunFence.Account;

public static class KnownPackages
{
    // Install winget first — it's a prerequisite for subsequent MSIX package installs
    public static readonly InstallablePackage Winget = new(
        "winget (App Installer)",
        """
        Write-Host "> Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe'"
        Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction Stop
        Write-Host "> Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.Winget.Source_8wekyb3d8bbwe'"
        Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.Winget.Source_8wekyb3d8bbwe' -ErrorAction Stop
        Write-Host '> & "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe" source update'
        & "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe" source update
        if ($LASTEXITCODE -ne 0) { throw "winget source update failed. Exit code: $LASTEXITCODE" }
        """,
        DetectExeName: "winget.exe");

    public static readonly InstallablePackage WindowsTerminal = new(
        "Windows Terminal",
        """
        Write-Host "> Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.WindowsTerminal_8wekyb3d8bbwe'"
        Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.WindowsTerminal_8wekyb3d8bbwe' -ErrorAction Stop
        """,
        DetectExeName: "wt.exe",
        RequiredPackages: [Winget]);

    // Intentionally uses the embedded upstream package installer source for this known package.
    public static readonly InstallablePackage ClaudeCode = new(
        "Claude Code + codex + jq + rg",
        ReadEmbeddedScript("install-claude-code.ps1"),
        DetectProfileRelativePath: @".local\bin\claude.exe",
        RequiredPackages: [Winget]);

    public static readonly IReadOnlyList<InstallablePackage> All = [Winget, WindowsTerminal, ClaudeCode];

    public static IReadOnlyList<InstallablePackage> ExpandWithDependencies(IEnumerable<InstallablePackage> packages)
    {
        var ordered = new List<InstallablePackage>();
        var visited = new HashSet<InstallablePackage>();

        foreach (var package in packages)
            AddPackageWithDependencies(package, ordered, visited);

        return ordered;
    }

    private static string ReadEmbeddedScript(string filename)
    {
        var resourceName = $"RunFence.Account.{filename}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AddPackageWithDependencies(
        InstallablePackage package,
        List<InstallablePackage> ordered,
        HashSet<InstallablePackage> visited)
    {
        if (!visited.Add(package))
            return;

        if (package.RequiredPackages != null)
        {
            foreach (var requiredPackage in package.RequiredPackages)
                AddPackageWithDependencies(requiredPackage, ordered, visited);
        }

        ordered.Add(package);
    }
}
