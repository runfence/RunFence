using System.Security.Cryptography;

namespace RunFence.Account.Lifecycle;

public static class EphemeralNameGenerator
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate()
    {
        var chars = new char[7];
        chars[0] = 'e';
        for (int i = 1; i < 7; i++)
            chars[i] = Chars[RandomNumberGenerator.GetInt32(Chars.Length)];
        return new string(chars);
    }
}