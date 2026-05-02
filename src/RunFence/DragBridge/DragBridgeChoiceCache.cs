using System.Security.Cryptography;
using System.Text;

namespace RunFence.DragBridge;

/// <summary>
/// Remembers the user's choice for a given (targetSid, paths) combination.
/// Only <see cref="DragBridgeAccessAction.CopyToTemp"/> choices are cached — grant choices are
/// permanent (access exists on next drag, so the dialog never reappears for those).
/// Capacity-limited to <see cref="MaxCapacity"/> entries with oldest-inserted-first eviction.
/// </summary>
public class DragBridgeChoiceCache
{
    public const int MaxCapacity = 100;

    private readonly Dictionary<string, DragBridgeAccessAction> _rememberedChoices = new();
    private readonly Queue<string> _rememberedChoicesOrder = new();

    /// <summary>
    /// Attempts to retrieve a previously remembered choice for the given target SID and paths.
    /// Returns true and sets <paramref name="action"/> when a remembered choice exists.
    /// </summary>
    public bool TryGetChoice(string targetSid, IEnumerable<string> paths, out DragBridgeAccessAction action)
    {
        var key = MakeChoiceKey(targetSid, paths);
        return _rememberedChoices.TryGetValue(key, out action);
    }

    /// <summary>
    /// Stores the user's choice for the given target SID and paths, evicting the oldest
    /// entry when the cache is at capacity.
    /// </summary>
    public void RememberChoice(string targetSid, IEnumerable<string> paths, DragBridgeAccessAction action)
    {
        var key = MakeChoiceKey(targetSid, paths);
        if (_rememberedChoices.Count >= MaxCapacity)
        {
            var oldest = _rememberedChoicesOrder.Dequeue();
            _rememberedChoices.Remove(oldest);
        }

        _rememberedChoicesOrder.Enqueue(key);
        _rememberedChoices[key] = action;
    }

    private static string MakeChoiceKey(string targetSid, IEnumerable<string> inaccessiblePaths)
    {
        var raw = targetSid + "|" + string.Join("|", inaccessiblePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
