namespace RunFence.Core.Models;

public class CredentialStore
{
    public byte[] ArgonSalt { get; set; } = new byte[Constants.Argon2SaltSize];
    public byte[] EncryptedCanary { get; set; } = [];
    public List<CredentialEntry> Credentials { get; set; } = [];
}