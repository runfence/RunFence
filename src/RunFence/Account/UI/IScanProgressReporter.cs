namespace RunFence.Account.UI;

/// <summary>
/// Abstracts the UI controls used to report bulk ACL scan progress,
/// decoupling <see cref="AccountBulkScanHandler"/> from concrete WinForms controls.
/// </summary>
public interface IScanProgressReporter
{
    void SetScanEnabled(bool enabled);
    void SetStatus(string text);
}
