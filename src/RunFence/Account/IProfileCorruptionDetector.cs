namespace RunFence.Account;

public interface IProfileCorruptionDetector
{
    CorruptedProfile? Detect(string sid);
}
