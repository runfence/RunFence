using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class FileAssociationsSettingsIO(ISafeExecutor safe) : ISettingsIO
{
    public FileAssociationsSettings Read()
    {
        var fa = new FileAssociationsSettings();
        safe.Try(() =>
        {
            var associations = new Dictionary<string, FileAssociation>();
            foreach (var ext in Constants.TrackedFileExtensions)
            {
                safe.Try(() =>
                {
                    string? progId;

                    // Try UserChoice first (the authoritative source)
                    using (var ucKey = Registry.CurrentUser.OpenSubKey(
                               Constants.RegFileExts + @"\" + ext + @"\UserChoice"))
                    {
                        progId = ucKey?.GetValue("ProgId") as string;
                    }

                    // Fall back to per-user Classes default value
                    if (progId == null)
                    {
                        using var clsKey = Registry.CurrentUser.OpenSubKey(
                            Constants.RegUserClasses + @"\" + ext);
                        progId = clsKey?.GetValue("") as string;
                    }

                    if (progId != null)
                    {
                        var openCommand = ResolveOpenCommand(progId);
                        associations[ext] = new FileAssociation { ProgId = progId, OpenCommand = openCommand };
                    }
                }, "reading");
            }

            if (associations.Count > 0)
                fa.Associations = associations;
        }, "reading");
        return fa;
    }

    public void Write(FileAssociationsSettings fa)
    {
        if (fa.Associations == null)
            return;
        bool changed = false;
        foreach (var (ext, assoc) in fa.Associations)
        {
            if (assoc.ProgId == null)
                continue;
            if (WriteExtensionAssociation(ext, assoc))
                changed = true;
        }

        if (changed)
            NativeMethods.SHChangeNotify(Constants.SHCNE_ASSOCCHANGED, Constants.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    private bool WriteExtensionAssociation(string ext, FileAssociation assoc)
    {
        // Reject keys containing backslashes or forward slashes — they would escape the intended
        // registry subkey and write to an arbitrary path.
        if (ext.Contains('\\') || ext.Contains('/'))
            return false;
        if (assoc.ProgId!.Contains('\\') || assoc.ProgId.Contains('/'))
            return false;

        bool changed = false;
        safe.Try(() =>
        {
            // Check if per-user Classes already points to the correct ProgId
            bool alreadyCorrect = false;
            safe.Try(() =>
            {
                using var existingKey = Registry.CurrentUser.OpenSubKey(
                    Constants.RegUserClasses + @"\" + ext);
                var existing = existingKey?.GetValue("") as string;
                if (string.Equals(existing, assoc.ProgId, StringComparison.OrdinalIgnoreCase))
                    alreadyCorrect = true;
            }, "writing");

            if (alreadyCorrect)
                return;

            // Delete UserChoice subkey to remove hash-protected association
            safe.Try(() =>
            {
                using var extsKey = Registry.CurrentUser.OpenSubKey(
                    Constants.RegFileExts + @"\" + ext, writable: true);
                extsKey?.DeleteSubKeyTree("UserChoice", throwOnMissingSubKey: false);
            }, "writing");

            // Set per-user Classes default to the ProgId
            using var clsKey = Registry.CurrentUser.CreateSubKey(
                Constants.RegUserClasses + @"\" + ext);
            clsKey.SetValue("", assoc.ProgId, RegistryValueKind.String);
            changed = true;

            // Write the ProgId class open command if available.
            // No command validation is applied here by design: preftrans runs under the target user's
            // credentials (not elevated), so any written command executes only with that user's
            // privilege level. Arbitrary command validation would be both incomplete and unnecessary.
            if (assoc.OpenCommand != null)
            {
                safe.Try(() =>
                {
                    using var cmdKey = Registry.CurrentUser.CreateSubKey(
                        Constants.RegUserClasses + @"\" + assoc.ProgId + @"\shell\open\command");
                    cmdKey.SetValue("", assoc.OpenCommand, RegistryValueKind.String);
                }, "writing");
            }
        }, "writing");
        return changed;
    }

    private static string? ResolveOpenCommand(string progId)
    {
        // Try per-user classes first, then machine-wide
        using (var key = Registry.CurrentUser.OpenSubKey(
                   Constants.RegUserClasses + @"\" + progId + @"\shell\open\command"))
        {
            var cmd = key?.GetValue("") as string;
            if (!string.IsNullOrEmpty(cmd))
                return cmd;
        }

        using (var key = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
        {
            return key?.GetValue("") as string;
        }
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.FileAssociations = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.FileAssociations != null) Write(s.FileAssociations); }
}