using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Ipc;

namespace RunFence.Startup;

/// <summary>
/// Sets up the IPC server and firewall enforcement when the main form handle is created.
/// Extracted from AppLifecycleStarter to give startup IPC bootstrapping a clear owner.
/// </summary>
public class StartupIpcBootstrapper(
    IStartupIpcHost mainForm,
    IIpcServerService ipcServer,
    IIpcMessageHandler ipcHandler,
    IFirewallEnforcementOrchestrator firewallEnforcementOrchestrator,
    DynamicPortRangeChecker portRangeChecker,
    UiThreadDatabaseAccessor dbAccessor,
    ILoggingService log)
{
    public void SetupIpcOnHandleCreated()
    {
        mainForm.FormClosing += (_, _) => ipcServer.Stop();

        bool ipcStarted = false;
        mainForm.HandleCreated += (_, _) =>
        {
            if (ipcStarted)
                return;
            ipcStarted = true;
            // Post SetStartupComplete via BeginInvoke so it is placed in the message queue
            // *after* any tray-click messages that were queued while the main thread was
            // blocked before Application.Run. This ensures those clicks are dropped.
            mainForm.BeginInvokeOnUiThread(mainForm.SetStartupComplete);

            // Run EnforceAll on a background thread: ComFirewallRuleManager creates a fresh
            // COM instance per call (HNetCfg.FwPolicy2, ThreadingModel=Both) so MTA is safe.
            // Snapshot the database so the background thread reads consistent state.
            var enforcementSnapshot = dbAccessor.CreateSnapshot();
            Task.Run(() =>
            {
                try
                {
                    var enforcementResult = firewallEnforcementOrchestrator.EnforceAll(enforcementSnapshot);

                    bool hasLocalhostBlocking = enforcementSnapshot.Accounts.Any(a =>
                        a.Firewall is { AllowLocalhost: false, FilterEphemeralLoopback: true });
                    if (hasLocalhostBlocking)
                        mainForm.BeginInvokeOnUiThread(() => _ = portRangeChecker.CheckIfNeededAsync(
                            new FirewallAccountSettings { AllowLocalhost = false }));

                    if (enforcementResult.HasFailures)
                    {
                        var warningText = string.Join(
                            Environment.NewLine,
                            enforcementResult.Failures.Select(failure =>
                                $"{failure.Layer}{(string.IsNullOrEmpty(failure.AccountSid) ? string.Empty : $" ({failure.AccountSid})")}: {failure.Message}"));
                        mainForm.BeginInvokeOnUiThread(() => MessageBox.Show(
                            $"Firewall enforcement reported startup failures:{Environment.NewLine}{warningText}{Environment.NewLine}{Environment.NewLine}Firewall rules may not be fully applied.",
                            "RunFence — Firewall Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"Firewall enforcement failed: {ex.Message}");
                    mainForm.BeginInvokeOnUiThread(() => MessageBox.Show(
                        $"Firewall enforcement failed at startup:\n{ex.Message}\n\nFirewall rules may not be fully applied.",
                        "RunFence — Firewall Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
            });

            ipcServer.Start((message, context) =>
                ipcHandler.HandleIpcMessage(message, context));
        };
    }
}
