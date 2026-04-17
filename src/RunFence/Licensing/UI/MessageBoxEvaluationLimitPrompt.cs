namespace RunFence.Licensing.UI;

/// <summary>
/// Default implementation of <see cref="IEvaluationLimitPrompt"/> that shows a MessageBox.
/// </summary>
public class MessageBoxEvaluationLimitPrompt : IEvaluationLimitPrompt
{
    public void ShowLimitMessage(string message, IWin32Window? owner)
        => MessageBox.Show(owner, message, "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
