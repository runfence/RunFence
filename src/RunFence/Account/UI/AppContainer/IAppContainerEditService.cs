using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public interface IAppContainerEditService
{
    Task<AppContainerEditResult> ApplyEditChanges(
        AppContainerEntry existing,
        string displayName,
        List<string> capabilities,
        bool loopback,
        List<string> newComClsids,
        bool isEphemeral);

    Task<AppContainerCreateResult> CreateNewContainer(
        string profileName,
        string displayName,
        bool isEphemeral,
        List<string> capabilities,
        bool loopback,
        List<string> comClsids);
}
