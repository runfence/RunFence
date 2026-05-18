namespace RunFence.Account.UI;

public sealed class AccountGridIconLifetimeManager : IDisposable
{
    private readonly Dictionary<DataGridViewRow, List<Image>> _rowIcons = [];
    private readonly HashSet<Image> _disposedIcons = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public void TrackOwned(DataGridViewRow row, Image image)
    {
        if (_disposed || ReferenceEquals(image, AccountGridHelper.EmptyIcon))
            return;

        if (!_rowIcons.TryGetValue(row, out var images))
        {
            images = [];
            _rowIcons[row] = images;
        }

        images.Add(image);
    }

    public void ReleaseRowIcons(DataGridViewRow row)
    {
        if (!_rowIcons.Remove(row, out var images))
            return;

        foreach (var image in images)
        {
            if (ReferenceEquals(image, AccountGridHelper.EmptyIcon) || !_disposedIcons.Add(image))
                continue;

            image.Dispose();
        }
    }

    public void ReleaseAllTrackedIcons()
    {
        foreach (var row in _rowIcons.Keys.ToList())
            ReleaseRowIcons(row);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseAllTrackedIcons();
        _rowIcons.Clear();
        _disposedIcons.Clear();
    }
}
