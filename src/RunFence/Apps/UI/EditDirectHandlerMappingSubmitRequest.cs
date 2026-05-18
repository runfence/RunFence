using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record EditDirectHandlerMappingSubmitRequest(
    string Key,
    DirectHandlerEntry CurrentEntry,
    string CurrentValue,
    string? NewValue);
