using System.Reflection;

namespace RunFence.Account;

public static class KnownPackages
{
    // Install winget first — it's a prerequisite for subsequent MSIX package installs
    public static readonly InstallablePackage Winget = new(
        "winget (App Installer)",
        "Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction Stop",
        DetectExeName: "winget.exe");

    public static readonly InstallablePackage WindowsTerminal = new(
        "Windows Terminal",
        "Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.WindowsTerminal_8wekyb3d8bbwe' -ErrorAction Stop",
        DetectExeName: "wt.exe");

    public static readonly InstallablePackage ClaudeCode = new(
        "Claude Code + jq + rg",
        ReadEmbeddedScript("install-claude-code.ps1"),
        DetectProfileRelativePath: @".local\bin\claude.exe");

    public static readonly IReadOnlyList<InstallablePackage> All = [Winget, WindowsTerminal, ClaudeCode];

    private static string ReadEmbeddedScript(string filename)
    {
        var resourceName = $"RunFence.Account.{filename}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}