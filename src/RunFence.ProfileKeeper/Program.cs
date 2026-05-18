using System.Security.Principal;
using RunFence.Launching.Processes;

namespace RunFence.ProfileKeeper;

public static class Program
{
    /*
      This fixes the corrupted profile issue and at the same time serves as a bootstrap process to derive token from.
      
      CreateProcessWithTokenW(LOGON_WITH_PROFILE) creates a profile-load lease tied to the token-launched process. When that process launches an AppX target,
      the visible/real app detaches into the AppX activation infrastructure and is no longer treated by ProfSvc as preserving that profile lease. When the
      original token-launched process exits, ProfSvc attempts to unload the profile even though the detached AppX app is still running and using HKCU /
      UsrClass.dat.

      - JobKeeper -> AppX Notepad -> JobKeeper exits: corrupted.
      - totalcmd -> AppX Notepad -> totalcmd exits: corrupted.
      - totalcmd -> cmd -> regedit -> close cmd -> close totalcmd: not corrupted, because regedit is not AppX-detached in the same way.

      So the precise corruption condition is:

      1. RunFence launches a process with CreateProcessWithTokenW(LOGON_WITH_PROFILE).
      2. That process launches an AppX app.
      3. The AppX app detaches from the creating process/profile lease.
      4. The original token-launched process exits.
      5. ProfSvc tries to unload the profile.
      6. Detached AppX app still holds profile/class hive resources.
      7. Next profile load sees the corrupted/half-unloaded state and TEMP profile behavior appears.
  */
    public static void Main(string[] args)
    {
        if (args.Length != 0)
            return;

        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("ProfileKeeper could not resolve its token SID.");
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("ProfileKeeper could not resolve its executable path.");
        var runner = new ProfileKeeperRunner(
            new ProfileKeeperIdentity(Environment.ProcessId, sid, Path.GetFullPath(processPath)),
            ProfileKeeperOptions.Default,
            new ProcessSnapshotReader(),
            new ProfileKeeperStateEvaluator(),
            new ProfileKeeperProcessTerminator(),
            TimeProvider.System);
        runner.Run(CancellationToken.None);
    }
}
