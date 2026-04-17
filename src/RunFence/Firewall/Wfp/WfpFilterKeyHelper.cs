using System.Security.Cryptography;
using System.Text;

namespace RunFence.Firewall.Wfp;

/// <summary>
/// Derives deterministic WFP filter GUIDs from a namespace prefix and account SID so that
/// filters can be found and deleted without storing IDs in the database.
/// </summary>
internal static class WfpFilterKeyHelper
{
    public static Guid DeriveKey(string prefix, string sid)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + sid));
        return new Guid(hash[..16]);
    }
}
