using RunFence.Core.Models;
using System.Windows.Forms;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogSubmitResult(
    DialogResult? DialogResult,
    AppEntry? Result,
    bool HasUnsavedMutations,
    string? StatusText = null,
    bool StatusIsError = false,
    string? NotificationMessage = null,
    bool NotificationIsWarning = false);
