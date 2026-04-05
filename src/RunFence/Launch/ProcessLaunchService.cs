#region

using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

#endregion

namespace RunFence.Launch;

public class ProcessLaunchService(
    ILoggingService log,
    ISplitTokenLauncher splitTokenLauncher,
    ILowIntegrityLauncher lowIntegrityLauncher,
    IInteractiveUserLauncher interactiveUserLauncher,
    ICurrentAccountLauncher currentAccountLauncher,
    IInteractiveLogonHelper logonHelper)
    : IProcessLaunchService
{
    public void Launch(AppEntry app, LaunchCredentials credentials,
        string? launcherArguments, string? launcherWorkingDirectory = null,
        AppSettings? settings = null, LaunchFlags flags = default)
    {
        var password = credentials.Password;
        var domain = credentials.Domain;
        var username = credentials.Username;
        var tokenSource = credentials.TokenSource;
        var useSplitToken = flags.UseSplitToken;
        var useLowIntegrity = flags.UseLowIntegrity;

        var finalArgs = ProcessLaunchHelper.DetermineArguments(app, launcherArguments);

        if (app.IsUrlScheme)
        {
            if (!ValidateUrlScheme(app.ExePath, out var error))
            {
                log.Error($"Blocked URL scheme at launch time: {app.ExePath} — {error}");
                throw new InvalidOperationException(error);
            }

            LaunchUrl(app.ExePath, new LaunchCredentials(password, domain, username, tokenSource),
                new LaunchFlags(useSplitToken, useLowIntegrity));
            return;
        }

        if (app.IsFolder)
        {
            var folderBrowserExe = settings?.FolderBrowserExePath ?? Constants.FolderBrowserExeName;
            var folderBrowserArgs = settings?.FolderBrowserArguments ?? "\"%1\"";
            LaunchFolder(app.ExePath, folderBrowserExe, folderBrowserArgs,
                new LaunchCredentials(password, domain, username, tokenSource),
                new LaunchFlags(useSplitToken, useLowIntegrity));
            return;
        }

        var target = new ProcessLaunchTarget(
            ExePath: app.ExePath,
            Arguments: finalArgs,
            WorkingDirectory: ProcessLaunchHelper.DetermineWorkingDirectory(app, launcherWorkingDirectory),
            EnvironmentVariables: app.EnvironmentVariables);
        LaunchCoreReturnPid(target, password, domain, username,
            useLowIntegrity, useSplitToken, label: app.Name, tokenSource: tokenSource);
    }

    public bool ValidateUrlScheme(string url, out string? error)
        => ProcessLaunchHelper.ValidateUrlScheme(url, out error);

    public void LaunchFolder(string folderPath, string folderBrowserExe, string folderBrowserArgs,
        LaunchCredentials credentials, LaunchFlags flags = default)
    {
        var password = credentials.Password;
        var domain = credentials.Domain;
        var username = credentials.Username;
        var tokenSource = credentials.TokenSource;

        folderBrowserExe = PathHelper.ResolveExePath(folderBrowserExe);
        folderPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var psi = CreateFolderLaunchPsi(folderPath, folderBrowserExe, folderBrowserArgs, tokenSource);

        if (tokenSource == LaunchTokenSource.Credentials && flags is { UseLowIntegrity: false, UseSplitToken: false })
            ProcessStartInfoHelper.SetCredentials(psi, username, domain, password);

        try
        {
            LaunchWithTokenStrategyReturnPid(psi, password, domain, username,
                flags.UseSplitToken, flags.UseLowIntegrity, tokenSource,
                $"folder {folderPath} with {folderBrowserExe}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error($"Folder launch failed for {folderPath}: credentials incorrect", ex);
            throw;
        }
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for a folder-browser launch.
    /// Wraps the browser executable in <c>cmd.exe /c</c> when it is a script (.cmd/.bat) and
    /// the token source requires it (Credentials or CurrentProcess). The
    /// <paramref name="folderBrowserArgs"/> template has <c>%1</c> substituted with the folder path
    /// (cmd-escaped when wrapping, raw otherwise).
    /// </summary>
    private static ProcessStartInfo CreateFolderLaunchPsi(
        string folderPath, string folderBrowserExe, string folderBrowserArgs,
        LaunchTokenSource tokenSource)
    {
        var ext = Path.GetExtension(folderBrowserExe);
        var isScript = ScriptExtensions.Contains(ext);

        // folderBrowserArgs is a user-configurable template string (e.g. "%1", --root "%1" --mode browse)
        // that can contain multiple custom arguments provided by the admin. We substitute %1 with the
        // folder path and pass the result as a single Arguments string — ArgumentList would require
        // parsing the template which is not appropriate here.
        var needsScriptWrap = isScript && tokenSource is LaunchTokenSource.Credentials or LaunchTokenSource.CurrentProcess;
        if (needsScriptWrap)
        {
            if (!PathHelper.IsPathSafeForCmd(folderBrowserExe))
                throw new InvalidOperationException("File path contains characters unsafe for cmd.exe execution.");
            if (PathHelper.ContainsCmdUnescapableChars(folderPath))
                throw new InvalidOperationException("Folder path contains characters unsafe for cmd.exe execution.");
            var escapedPath = ProcessLaunchHelper.EscapeCmdMetacharacters(folderPath);
            var resolvedArgs = folderBrowserArgs.Replace("%1", escapedPath);
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{folderBrowserExe}\" {resolvedArgs}",
                UseShellExecute = false,
                WorkingDirectory = folderPath
            };
        }

        return new ProcessStartInfo
        {
            FileName = folderBrowserExe,
            Arguments = folderBrowserArgs.Replace("%1", folderPath),
            UseShellExecute = false,
            WorkingDirectory = folderPath
        };
    }

    /// <summary>
    /// Creates a ProcessStartInfo, wrapping in cmd.exe /c if wrapInCmd is true.
    /// Used by Launch and LaunchExe for the shared script-wrapping pattern.
    /// </summary>
    private static ProcessStartInfo CreateProcessStartInfo(ProcessLaunchTarget target, bool wrapInCmd)
    {
        var exePath = target.ExePath;
        var arguments = target.Arguments;

        var resolvedWorkDir = target.WorkingDirectory
                              ?? Path.GetDirectoryName(exePath)
                              ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(resolvedWorkDir))
            resolvedWorkDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Arguments are raw command-line strings — set directly on psi.Arguments so they
        // reach the target process verbatim, with no extra quoting applied.
        // This is intentional and not a security issue: the argument string originates from
        // the app entry's DefaultArguments field or the trusted RunFence IPC channel.
        // When wrapping in cmd.exe /c, the string is appended after the quoted script path.
        if (wrapInCmd)
        {
            // CreateProcessWithLogonW cannot launch .cmd/.bat directly; wrap in cmd.exe /c
            if (!PathHelper.IsPathSafeForCmd(exePath))
                throw new InvalidOperationException("File path contains characters unsafe for cmd.exe execution.");
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = string.IsNullOrEmpty(arguments)
                    ? $"/c \"{exePath}\""
                    : $"/c \"{exePath}\" {arguments}",
                UseShellExecute = false,
                WorkingDirectory = resolvedWorkDir
            };
        }

        return new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? "",
            UseShellExecute = false,
            WorkingDirectory = resolvedWorkDir
        };
    }

    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cmd", ".bat" };

    // Types that require shell dispatch to launch (ShellExecute / cmd /c start).
    // CreateProcessWithLogonW and CreateProcessWithTokenW cannot launch these directly.
    private static readonly HashSet<string> ShellLaunchExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ps1", ".msi", ".reg", ".lnk" };

    public void LaunchExe(ProcessLaunchTarget target, LaunchCredentials credentials, LaunchFlags flags = default)
    {
        LaunchCoreReturnPid(target, credentials.Password, credentials.Domain, credentials.Username,
            flags.UseLowIntegrity, flags.UseSplitToken,
            label: target.ExePath, tokenSource: credentials.TokenSource);
    }

    public int LaunchExeReturnPid(ProcessLaunchTarget target, LaunchCredentials credentials, LaunchFlags flags = default)
    {
        return LaunchCoreReturnPid(target, credentials.Password, credentials.Domain, credentials.Username,
            flags.UseLowIntegrity, flags.UseSplitToken,
            label: target.ExePath, tokenSource: credentials.TokenSource);
    }

    /// <summary>
    /// Core process-launch logic shared by <see cref="Launch"/>, <see cref="LaunchExe"/>,
    /// and <see cref="LaunchExeReturnPid"/>. Handles shell-launch detection, script wrapping,
    /// credential injection, split-token / low-integrity branching, and Win32 logon-failure error
    /// reporting. Returns the process ID, or 0 when the PID cannot be determined.
    /// </summary>
    private int LaunchCoreReturnPid(ProcessLaunchTarget target, SecureString? password, string? domain, string? username,
        bool launchAsLowIntegrity, bool useSplitToken, string label,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials)
    {
        var ext = Path.GetExtension(target.ExePath);
        var isShellLaunch = ShellLaunchExtensions.Contains(ext);
        var isScript = ScriptExtensions.Contains(ext);

        if (isShellLaunch)
        {
            LaunchViaShellStart(target, password, domain, username, tokenSource,
                useSplitToken, launchAsLowIntegrity);
            return 0;
        }

        var needsScriptWrap = isScript && tokenSource is LaunchTokenSource.Credentials or LaunchTokenSource.CurrentProcess;
        var psi = CreateProcessStartInfo(target, needsScriptWrap);

        if (tokenSource == LaunchTokenSource.Credentials && !launchAsLowIntegrity && !useSplitToken)
            ProcessStartInfoHelper.SetCredentials(psi, username, domain, password);

        try
        {
            return LaunchWithTokenStrategyReturnPid(psi, password, domain, username,
                useSplitToken, launchAsLowIntegrity, tokenSource, label,
                extraEnvVars: target.EnvironmentVariables, hideWindow: target.HideWindow);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error($"Launch failed for {label}: credentials incorrect", ex);
            throw;
        }
    }

    private void LaunchViaShellStart(ProcessLaunchTarget target,
        SecureString? password, string? domain, string? username,
        LaunchTokenSource tokenSource, bool useSplitToken, bool useLowIntegrity)
    {
        var filePath = target.ExePath;

        if (!PathHelper.IsPathSafeForCmd(filePath))
            throw new InvalidOperationException("File path contains characters unsafe for cmd.exe execution.");

        // Build: cmd.exe /c start "" "path" [args]
        // Arguments are appended after the quoted path — some file types (e.g. .reg) ignore them.
        // Validate that arguments don't contain cmd-unescapable characters (", %, control chars),
        // then escape remaining metacharacters (& | < > ^ ! etc.) with ^.
        string argsString;
        if (string.IsNullOrEmpty(target.Arguments))
        {
            argsString = "";
        }
        else
        {
            if (PathHelper.ContainsCmdUnescapableChars(target.Arguments))
                throw new InvalidOperationException("Arguments contain characters unsafe for cmd.exe execution.");
            argsString = " " + ProcessLaunchHelper.EscapeCmdMetacharacters(target.Arguments);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" \"{filePath}\"{argsString}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(filePath)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        if (tokenSource == LaunchTokenSource.Credentials && !useSplitToken && !useLowIntegrity)
            ProcessStartInfoHelper.SetCredentials(psi, username, domain, password);

        try
        {
            LaunchWithTokenStrategyReturnPid(psi, password, domain, username,
                useSplitToken, useLowIntegrity, tokenSource, $"{filePath} (shell start)",
                extraEnvVars: target.EnvironmentVariables);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error($"Shell-start launch failed for {filePath}: credentials incorrect", ex);
            throw;
        }
    }

    public void LaunchUrl(string url, LaunchCredentials credentials, LaunchFlags flags = default)
    {
        var password = credentials.Password;
        var domain = credentials.Domain;
        var username = credentials.Username;
        var tokenSource = credentials.TokenSource;
        if (!ValidateUrlScheme(url, out var error))
            throw new InvalidOperationException($"URL scheme blocked: {error}");

        // Escape cmd.exe metacharacters with ^ so they are treated as literals.
        // ValidateUrlScheme already rejected " and % (no safe cmd.exe escape)
        // and control characters. The remaining metacharacters (& | ^ < > ! etc.)
        // are safely neutralized by ^ prefixing.
        var escapedUrl = ProcessLaunchHelper.EscapeCmdMetacharacters(url);
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c start \"\" " + escapedUrl,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (tokenSource == LaunchTokenSource.Credentials && flags is { UseSplitToken: false, UseLowIntegrity: false })
            ProcessStartInfoHelper.SetCredentials(psi, username, domain, password);

        try
        {
            LaunchWithTokenStrategyReturnPid(psi, password, domain, username,
                flags.UseSplitToken, flags.UseLowIntegrity, tokenSource, $"URL scheme: {url}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            log.Error("URL launch failed: credentials incorrect", ex);
            throw;
        }
    }

    /// <summary>
    /// Executes the split-token → credential re-apply → fallback chain (low IL / interactive user /
    /// interactive logon retry). Assumes the PSI has credentials pre-set when neither split-token
    /// nor low-integrity is requested; that responsibility belongs to the caller.
    /// Returns the actual PID for split-token and credential (Process.Start) paths;
    /// returns 0 for low-integrity, interactive user, and shell-start paths where the PID
    /// is not directly obtainable without additional overloads on those launchers.
    /// Win32Exception propagates to callers so each can log its own context-specific error message.
    /// </summary>
    private int LaunchWithTokenStrategyReturnPid(
        ProcessStartInfo psi, SecureString? password, string? domain, string? username,
        bool useSplitToken, bool useLowIntegrity, LaunchTokenSource tokenSource,
        string logLabel, Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false)
    {
        var isCurrentAccount = tokenSource == LaunchTokenSource.CurrentProcess;
        var needsTokenLaunch = tokenSource == LaunchTokenSource.InteractiveUser;
        var accountLabel = isCurrentAccount ? "current account" : needsTokenLaunch ? "interactive user" : username;

        // Best-effort: if RunFence.exe currently holds foreground rights (e.g. its window is focused),
        // grant any process the right to set the foreground window. The primary grant for IPC-triggered
        // launches is made in RunFence.Launcher (which is created by the shell with foreground rights).
        ProcessLaunchNative.AllowSetForegroundWindow(ProcessLaunchNative.ASFW_ANY);

        if (useSplitToken)
        {
            var pid = splitTokenLauncher.Launch(psi, password, domain, username, useLowIntegrity, tokenSource, extraEnvVars, hideWindow);
            if (pid >= 0)
            {
                log.Info($"Launched {logLabel} as {accountLabel} (split token{(useLowIntegrity ? "+low IL" : "")})");
                return pid;
            }
        }

        // Credentials were not set on PSI when split token was expected to handle them.
        // Set them now for the fallback launch path.
        if (useSplitToken && tokenSource == LaunchTokenSource.Credentials && !useLowIntegrity)
            ProcessStartInfoHelper.SetCredentials(psi, username, domain, password);

        if (useLowIntegrity)
        {
            lowIntegrityLauncher.Launch(psi, password, domain, username, tokenSource, extraEnvVars, hideWindow);
            log.Info($"Launched {logLabel} as {accountLabel} (low IL)");
            return 0;
        }

        if (needsTokenLaunch)
        {
            var pid = interactiveUserLauncher.Launch(psi, extraEnvVars, hideWindow);
            log.Info($"Launched {logLabel} as {accountLabel}");
            return pid;
        }

        if (isCurrentAccount)
        {
            var pid = currentAccountLauncher.Launch(psi, extraEnvVars, hideWindow);
            log.Info($"Launched {logLabel} as {accountLabel} (stripped privileges)");
            return pid;
        }

        if (extraEnvVars?.Count > 0 && tokenSource == LaunchTokenSource.Credentials)
        {
            IntPtr hTok = IntPtr.Zero;
            IntPtr pEnv = IntPtr.Zero;
            try
            {
                hTok = NativeTokenAcquisition.AcquireLogonToken(password, domain, username, log, logonHelper, tokenSource);
                if (ProcessLaunchNative.CreateEnvironmentBlock(out pEnv, hTok, false))
                {
                    var vars = NativeEnvironmentBlock.Read(pEnv);
                    foreach (var kv in extraEnvVars)
                        vars[kv.Key] = kv.Value;
                    psi.Environment.Clear();
                    foreach (var kv in vars)
                        psi.Environment[kv.Key] = kv.Value;
                }
            }
            finally
            {
                if (pEnv != IntPtr.Zero)
                    ProcessLaunchNative.DestroyEnvironmentBlock(pEnv);
                if (hTok != IntPtr.Zero)
                    NativeMethods.CloseHandle(hTok);
            }
        }

        psi.CreateNoWindow = hideWindow;
        var process = logonHelper.RunWithLogonRetry(domain, username, () => Process.Start(psi));
        log.Info($"Launched {logLabel} as {accountLabel}");
        return process?.Id ?? 0;
    }
}