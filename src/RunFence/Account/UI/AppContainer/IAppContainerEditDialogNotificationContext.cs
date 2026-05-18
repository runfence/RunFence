using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

internal interface IAppContainerEditDialogNotificationContext
{
    bool IsCreateMode { get; }

    string PendingValidationCaption { get; }

    AppContainerOperationStatus? PendingNotificationStatus { get; }
}

internal interface IAppContainerEditDialogResultContext : IAppContainerEditDialogNotificationContext
{
    AppContainerEntry? CreatedEntry { get; set; }

    AppContainerOperationStatus? LastOperationStatus { get; set; }

    new AppContainerOperationStatus? PendingNotificationStatus { get; set; }
}
