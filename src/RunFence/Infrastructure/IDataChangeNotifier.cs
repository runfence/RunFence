namespace RunFence.Infrastructure;

public interface IDataChangeNotifier
{
    void NotifyDataChanged();
    event Action? DataChanged;
}