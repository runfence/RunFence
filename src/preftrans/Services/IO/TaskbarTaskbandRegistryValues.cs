namespace PrefTrans.Services.IO;

public sealed record TaskbarTaskbandRegistryValues
{
    public byte[]? Favorites { get; init; }
    public byte[]? FavoritesResolve { get; init; }
}
