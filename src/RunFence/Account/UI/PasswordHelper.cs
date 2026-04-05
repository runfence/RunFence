using System.Security.Cryptography;

namespace RunFence.Account.UI;

public static class PasswordHelper
{
    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()_+\"'[]{}:.,/\\";
    private const string AllChars = UpperChars + LowerChars + DigitChars + SymbolChars;

    public static char[] GenerateRandomPassword()
    {
        var length = RandomNumberGenerator.GetInt32(15, 21);
        var password = new char[length];
        for (int i = 0; i < length; i++)
            password[i] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];

        // Ensure at least one character from each category using distinct positions
        var usedPositions = new HashSet<int>();
        EnsureCategory(password, UpperChars, usedPositions);
        EnsureCategory(password, LowerChars, usedPositions);
        EnsureCategory(password, DigitChars, usedPositions);
        EnsureCategory(password, SymbolChars, usedPositions);

        return password;
    }

    private static void EnsureCategory(char[] password, string category, HashSet<int> usedPositions)
    {
        if (password.Any(category.Contains))
            return;

        // Missing category — replace a random position not already used for another category fix
        int pos;
        do
        {
            pos = RandomNumberGenerator.GetInt32(password.Length);
        } while (usedPositions.Contains(pos));

        usedPositions.Add(pos);
        password[pos] = category[RandomNumberGenerator.GetInt32(category.Length)];
    }
}