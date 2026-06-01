using System.Security.AccessControl;

namespace RunFence.Account;

public sealed class LogonScriptStateRollbackStore
{
    public Snapshot Capture(
        string scriptsIniPath,
        string gptIniPath,
        string wrapperScriptPath,
        string legacyWrapperScriptPath)
    {
        return Snapshot.Capture(scriptsIniPath, gptIniPath, wrapperScriptPath, legacyWrapperScriptPath);
    }

    public void Restore(Snapshot snapshot)
    {
        snapshot.Restore();
    }

    public sealed class Snapshot
    {
        private const AccessControlSections CapturedSecuritySections =
            AccessControlSections.Owner |
            AccessControlSections.Group |
            AccessControlSections.Access;

        private readonly FileSnapshot _scriptsIni;
        private readonly FileSnapshot _gptIni;
        private readonly FileSnapshot _wrapperScript;
        private readonly FileSnapshot _legacyWrapperScript;

        private Snapshot(
            FileSnapshot scriptsIni,
            FileSnapshot gptIni,
            FileSnapshot wrapperScript,
            FileSnapshot legacyWrapperScript)
        {
            _scriptsIni = scriptsIni;
            _gptIni = gptIni;
            _wrapperScript = wrapperScript;
            _legacyWrapperScript = legacyWrapperScript;
        }

        public static Snapshot Capture(
            string scriptsIniPath,
            string gptIniPath,
            string wrapperScriptPath,
            string legacyWrapperScriptPath)
        {
            return new Snapshot(
                CaptureFile(scriptsIniPath),
                CaptureFile(gptIniPath),
                CaptureFile(wrapperScriptPath),
                CaptureFile(legacyWrapperScriptPath));
        }

        public void Restore()
        {
            RestoreCapturedFile(_scriptsIni);
            RestoreCapturedFile(_gptIni);
            RestoreCapturedFile(_wrapperScript);
            RestoreCapturedFile(_legacyWrapperScript);
        }

        private static FileSnapshot CaptureFile(string path)
        {
            if (!File.Exists(path))
                return new FileSnapshot(path, false, null, null, null);

            FileSecurity? security = null;
            try
            {
                security = new FileInfo(path).GetAccessControl(CapturedSecuritySections);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }

            return new FileSnapshot(
                path,
                true,
                File.ReadAllBytes(path),
                File.GetAttributes(path),
                security);
        }

        private static void RestoreCapturedFile(FileSnapshot snapshot)
        {
            if (snapshot.Exists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
                ClearReadOnlyIfPresent(snapshot.Path);
                File.WriteAllBytes(snapshot.Path, snapshot.Bytes ?? []);

                if (snapshot.Security != null)
                {
                    var clonedSecurity = new FileSecurity();
                    clonedSecurity.SetSecurityDescriptorSddlForm(
                        snapshot.Security.GetSecurityDescriptorSddlForm(CapturedSecuritySections),
                        CapturedSecuritySections);
                    new FileInfo(snapshot.Path).SetAccessControl(clonedSecurity);
                }

                if (snapshot.Attributes != null)
                    File.SetAttributes(snapshot.Path, snapshot.Attributes.Value);

                return;
            }

            if (File.Exists(snapshot.Path))
            {
                ClearReadOnlyIfPresent(snapshot.Path);
                File.Delete(snapshot.Path);
            }
        }

        private static void ClearReadOnlyIfPresent(string path)
        {
            if (!File.Exists(path))
                return;

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        private sealed record FileSnapshot(
            string Path,
            bool Exists,
            byte[]? Bytes,
            FileAttributes? Attributes,
            FileSecurity? Security);
    }
}
