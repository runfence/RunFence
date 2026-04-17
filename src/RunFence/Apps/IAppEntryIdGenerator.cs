namespace RunFence.Apps;

public interface IAppEntryIdGenerator
{
    string GenerateUniqueId(IEnumerable<string> existingIds);
}
