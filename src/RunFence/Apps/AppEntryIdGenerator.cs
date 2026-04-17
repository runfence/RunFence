using RunFence.Core.Models;

namespace RunFence.Apps;

public class AppEntryIdGenerator : IAppEntryIdGenerator
{
    public string GenerateUniqueId(IEnumerable<string> existingIds)
    {
        var existingSet = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 100; i++)
        {
            var id = AppEntry.GenerateId();
            if (!existingSet.Contains(id))
                return id;
        }

        throw new InvalidOperationException("Could not generate a unique app ID after 100 attempts.");
    }
}
