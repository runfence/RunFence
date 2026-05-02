using System.Security.Cryptography;

namespace RunFence.Account;

public static class PasswordHelper
{
    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()_+\"'[]{}:.,/\\";
    private const string AllChars = UpperChars + LowerChars + DigitChars + SymbolChars;
    private static readonly string[] Categories = [UpperChars, LowerChars, DigitChars, SymbolChars];

    public static char[] GenerateRandomPassword()
    {
        var length = RandomNumberGenerator.GetInt32(15, 21);
        var password = new char[length];
        for (int i = 0; i < length; i++)
            password[i] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];

        // Ensure at least one character from each category using distinct positions.
        // Two passes handle the rare case where a first-pass insertion overwrites the sole
        // representative of an already-satisfied category: second-pass positions are excluded
        // from those used in the first pass (via usedPositions), preventing circular overwrites.
        var usedPositions = new HashSet<int>();
        for (int pass = 0; pass < 2; pass++)
            foreach (var cat in Categories)
                EnsureCategory(password, cat, usedPositions);

        return password;
    }

    private static void EnsureCategory(char[] password, string category, HashSet<int> usedPositions)
    {
        if (password.Any(category.Contains))
            return;

        // Missing category — replace a random position not already used for another category fix
        if (password.Length <= usedPositions.Count)
            return;
        int pos;
        do
        {
            pos = RandomNumberGenerator.GetInt32(password.Length);
        } while (usedPositions.Contains(pos));

        usedPositions.Add(pos);
        password[pos] = category[RandomNumberGenerator.GetInt32(category.Length)];
    }
}
