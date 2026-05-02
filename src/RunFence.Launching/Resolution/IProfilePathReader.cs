namespace RunFence.Launching.Resolution;

public interface IProfilePathReader
{
    string? GetProfilePath(string sid);
}
