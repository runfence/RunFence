using System.Text.RegularExpressions;

namespace RunFence.SecurityScanner;

public partial class GroupPolicyDataAccess : IGroupPolicyDataAccess
{
    [GeneratedRegex(@"^\d+CmdLine=(.+)$")]
    private static partial Regex IniCmdLineRegex();

    public string GetGpScriptsDir(string userSid) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "GroupPolicyUsers", userSid, "User", "Scripts");

    public string GetMachineGpScriptsDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "GroupPolicy", "Machine", "Scripts");

    public string GetMachineGpUserScriptsDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "GroupPolicy", "User", "Scripts");

    public List<string> GetMachineGpScriptPaths()
    {
        var paths = new List<string>();
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var machineIni = Path.Combine(sysDir, "GroupPolicy", "Machine", "Scripts", "scripts.ini");
        paths.AddRange(ParseIniScriptPaths(machineIni, "Startup"));
        paths.AddRange(ParseIniScriptPaths(machineIni, "Shutdown"));
        var userIni = Path.Combine(sysDir, "GroupPolicy", "User", "Scripts", "scripts.ini");
        paths.AddRange(ParseIniScriptPaths(userIni, "Logon"));
        return paths;
    }

    public string GetSharedWrapperScriptsDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RunFence", "scripts");

    public List<string> GetLogonScriptPaths(string userSid)
    {
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var iniPath = Path.Combine(sysDir, "GroupPolicyUsers", userSid, "User", "Scripts", "scripts.ini");
        return ParseIniScriptPaths(iniPath, "Logon");
    }

    private static List<string> ParseIniScriptPaths(string iniPath, string sectionName)
    {
        var paths = new List<string>();
        try
        {
            if (!File.Exists(iniPath))
                return paths;

            var sectionHeader = $"[{sectionName}]";
            bool inSection = false;
            foreach (var line in File.ReadAllLines(iniPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inSection = trimmed.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSection)
                    continue;

                var match = IniCmdLineRegex().Match(trimmed);
                if (match.Success)
                {
                    var cmdLine = match.Groups[1].Value.Trim();
                    var exePath = CommandLineParser.ExtractExecutablePath(cmdLine);
                    if (!string.IsNullOrEmpty(exePath))
                        paths.Add(exePath);
                }
            }
        }
        catch
        {
            /* scripts.ini not accessible */
        }

        return paths;
    }
}