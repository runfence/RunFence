namespace RunFence.UI;

public sealed class WinFormsApplicationExitService : IApplicationExitService
{
    public void Exit() => Application.Exit();
}
