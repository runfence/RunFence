using RunFence.Infrastructure;

namespace RunFence.Account.Lifecycle;

public static class EphemeralNameGenerator
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly IRandomSource RandomSource = new CryptographicRandomSource();

    public static string Generate()
        => Generate(RandomSource);

    public static string Generate(IRandomSource randomSource)
    {
        var chars = new char[7];
        chars[0] = 'e';
        for (int i = 1; i < 7; i++)
            chars[i] = Chars[randomSource.NextInt32(Chars.Length)];
        return new string(chars);
    }
}
