using RunFence.Core.Models;

namespace RunFence.Firewall;

public static class FirewallPendingDomainHelper
{
    public static void AddUnique(
        List<FirewallPendingDomainResolution> list,
        HashSet<string> keys,
        IReadOnlyList<FirewallPendingDomainResolution> additions)
    {
        foreach (var item in additions)
        {
            var key = $"{item.Sid}\0{item.Domain}";
            if (keys.Add(key))
                list.Add(item);
        }
    }
}
