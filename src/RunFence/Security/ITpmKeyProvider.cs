namespace RunFence.Security;

public interface ITpmKeyProvider
{
    bool IsAvailable();                                 // probe TPM Platform Crypto Provider
    void CreateKey(string keyName, int keySize);        // create persistent RSA key with PCR binding
    void DeleteKey(string keyName);                     // delete key (ignore not-found)
    byte[] Encrypt(string keyName, byte[] data);        // RSA-OAEP-SHA256 encrypt
    byte[] Decrypt(string keyName, byte[] data);        // RSA-OAEP-SHA256 decrypt (throws on PCR mismatch)
    byte[] DecryptExact(string keyName, byte[] data, int expectedLength); // fixed-size RSA-OAEP-SHA256 decrypt
}
