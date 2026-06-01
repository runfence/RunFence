#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using System.Diagnostics;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockAccountProcessLauncher(
    IAccountProcessLauncher real,
    NonElevatedMockStore store) : IAccountProcessLauncher
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; uses Process.Start with credentials for real accounts,
    // current-user launch for current/interactive SID, no-op for fake-store accounts

    public ProcessInfo? Launch(ProcessLaunchTarget target, AccountLaunchIdentity identity)
    {
        // Fake store accounts have no real OS presence — no-op
        if (store.IsFakeUser(identity.Sid))
            return new ProcessInfo(default);

        var creds = identity.Credentials!.Value;
        var psi = ProcessLaunchHelper.BuildProcessStartInfo(target);

        if (creds.TokenSource == LaunchTokenSource.Credentials && creds.Password != null)
        {
            psi.CreateNoWindow = false;
            psi.UserName = creds.Username;
            psi.Domain = creds.Domain;
            // ProcessStartInfo.Password requires System.Security.SecureString (BCL API boundary).
            // Convert from ProtectedString at the interop boundary.
            creds.Password.UseUnicodeSnapshot(snapshot =>
            {
                var ss = new System.Security.SecureString();
                IntPtr passwordPtr = snapshot.DangerousGetIntPtr();
                for (int i = 0; i < snapshot.CharCount; i++)
                    ss.AppendChar((char)System.Runtime.InteropServices.Marshal.ReadInt16(passwordPtr, i * sizeof(char)));
                psi.Password = ss;
            });
            return ProcessInfo.FromManagedProcess(Process.Start(psi));
        }

        // No credentials: launch directly if current or interactive user
        if (SidResolutionHelper.CanLaunchWithoutPassword(identity.Sid))
            return ProcessInfo.FromManagedProcess(Process.Start(psi));

        return new ProcessInfo(default);
    }
}
