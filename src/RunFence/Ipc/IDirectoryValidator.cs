namespace RunFence.Ipc;

public interface IDirectoryValidator
{
    DirectoryValidationHandle ValidateAndHold(string path, string callerSid);
}