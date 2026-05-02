using RunFence.Core.Models;

namespace RunFence.Persistence;

public interface IConfigImportFileParser
{
    AppDatabase ParseMainConfig(string path);
    AppConfig ParseAdditionalConfig(string path);
}
