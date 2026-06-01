using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public interface IGroupSelectionLoadView
{
    string? GetSelectedGroupSid();
    void SetMembersLoading(bool isMembersLoading);
}

public sealed class GroupSelectionLoadController(
    GroupDescriptionEditor descriptionEditor,
    IGroupGridPopulator gridPopulator,
    ILocalGroupQueryService groupMembership,
    ILoggingService log)
{
    private IGroupSelectionLoadView _view = null!;

    public void Initialize(IGroupSelectionLoadView view)
    {
        _view = view;
    }

    public async Task HandleSelectionChangedAsync(string? selectedSid)
    {
        descriptionEditor.BeginLoad(selectedSid);

        if (selectedSid == null)
        {
            gridPopulator.ClearMembers();
            _view.SetMembersLoading(false);
            return;
        }

        _view.SetMembersLoading(true);

        var descriptionTask = Task.Run(() => groupMembership.GetGroupDescription(selectedSid));
        var membersTask = gridPopulator.PopulateMembers(selectedSid);

        string? description = null;
        var descriptionFailed = false;
        var membersFailed = false;

        try
        {
            description = await descriptionTask;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load description for group {selectedSid}", ex);
            descriptionFailed = true;
        }

        try
        {
            await membersTask;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load members for group {selectedSid}", ex);
            membersFailed = true;
        }

        if (!SidComparer.SidEquals(selectedSid, _view.GetSelectedGroupSid()))
            return;

        if (membersFailed)
            gridPopulator.ClearMembers();

        descriptionEditor.CompleteLoad(selectedSid, description, descriptionFailed);
        _view.SetMembersLoading(false);
    }

    public async Task LoadDescriptionAfterRefreshAsync(string? selectedSid)
    {
        if (descriptionEditor.IsEditingGroup(selectedSid))
            return;

        descriptionEditor.BeginLoad(selectedSid);
        if (selectedSid == null)
            return;

        string? description = null;
        var failed = false;
        try
        {
            description = await Task.Run(() => groupMembership.GetGroupDescription(selectedSid));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load description for group {selectedSid}", ex);
            failed = true;
        }

        descriptionEditor.CompleteLoad(selectedSid, description, failed);
    }
}
