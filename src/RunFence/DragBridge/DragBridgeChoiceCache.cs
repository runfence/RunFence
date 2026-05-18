using System.Security.Cryptography;
using System.Text;

namespace RunFence.DragBridge;

/// <summary>
/// Remembers the user's choice for a given target identity and paths combination.
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
    /// Attempts to retrieve a previously remembered choice for the given target identity and paths.
    /// Returns true and sets <paramref name="action"/> when a remembered choice exists.
    /// </summary>
    public bool TryGetChoice(string targetIdentityKey, IEnumerable<string> paths, out DragBridgeAccessAction action)
    {
        var key = MakeChoiceKey(targetIdentityKey, paths);
        return _rememberedChoices.TryGetValue(key, out action);
    }

    /// <summary>
    /// Stores the user's choice for the given target identity and paths, evicting the oldest
    /// entry when the cache is at capacity.
    /// </summary>
    public void RememberChoice(string targetIdentityKey, IEnumerable<string> paths, DragBridgeAccessAction action)
    {
        var key = MakeChoiceKey(targetIdentityKey, paths);
        if (_rememberedChoices.ContainsKey(key))
        {
            _rememberedChoices[key] = action;
            return;
        }

        while (_rememberedChoices.Count >= MaxCapacity && _rememberedChoicesOrder.Count > 0)
        {
            var oldest = _rememberedChoicesOrder.Dequeue();
            _rememberedChoices.Remove(oldest);
        }

        _rememberedChoicesOrder.Enqueue(key);
        _rememberedChoices[key] = action;
    }

    private static string MakeChoiceKey(string targetIdentityKey, IEnumerable<string> inaccessiblePaths)
    {
        var raw = targetIdentityKey + "|" + string.Join("|", inaccessiblePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
