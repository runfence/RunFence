namespace RunFence.RunAs;

public interface IRunAsUserAccountCreator
{
    Task<RunAsCreatedAccountResult?> CreateNewAccountAsync(string filePath);
}
