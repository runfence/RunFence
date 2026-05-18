namespace RunFence.Core.Models;

public class CredentialStore
{
    public byte[] ArgonSalt { get; set; } = new byte[Constants.Argon2SaltSize];
    public byte[] EncryptedCanary { get; set; } = [];
    public List<CredentialEntry> Credentials { get; set; } = [];

    public CredentialStore CreateSnapshot()
        => new()
        {
            ArgonSalt = ArgonSalt?.ToArray() ?? new byte[Constants.Argon2SaltSize],
            EncryptedCanary = EncryptedCanary?.ToArray() ?? [],
            Credentials = (Credentials ?? [])
                .Select(credential => new CredentialEntry
                {
                    Id = credential.Id,
                    Sid = credential.Sid,
                    EncryptedPassword = credential.EncryptedPassword?.ToArray() ?? []
                })
                .ToList()
        };

    public void ReplaceWithSnapshot(CredentialStore snapshot)
    {
        var clone = snapshot.CreateSnapshot();
        ArgonSalt = clone.ArgonSalt;
        EncryptedCanary = clone.EncryptedCanary;
        Credentials = clone.Credentials;
    }
}
