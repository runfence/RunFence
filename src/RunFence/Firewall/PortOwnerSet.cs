namespace RunFence.Firewall;

public sealed class PortOwnerSet
{
    public bool HasUnknownOwner { get; private set; }

    public HashSet<string> OwnerSids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddOwner(string? ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            HasUnknownOwner = true;
            return;
        }

        OwnerSids.Add(ownerSid);
    }
}
