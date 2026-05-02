namespace RunFence.Infrastructure;

public interface IUserConfirmationService
{
    bool Confirm(string message, string title);
}
