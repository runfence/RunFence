namespace RunFence.Core;

/// <summary>
/// RFC 4648 Base32 encoding and decoding using the standard alphabet (A-Z 2-7).
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes <paramref name="data"/> to a Base32 string (no padding).</summary>
    public static string Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;
        var result = new System.Text.StringBuilder();
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;
        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xFF;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int index = 0x1F & (buffer >> (bitsLeft - 5));
            bitsLeft -= 5;
            result.Append(Alphabet[index]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Decodes a Base32 string (no padding required) to bytes.
    /// Throws <see cref="FormatException"/> on invalid characters.
    /// </summary>
    public static byte[] Decode(string base32)
    {
        var result = new System.Collections.Generic.List<byte>();
        int buffer = 0;
        int bitsLeft = 0;
        foreach (char c in base32.ToUpperInvariant())
        {
            if (c == '=')
                break;
            var charIndex = Alphabet.IndexOf(c);
            if (charIndex < 0)
                throw new FormatException($"Invalid base32 character: {c}");
            buffer <<= 5;
            buffer |= charIndex & 0x1F;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                result.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return result.ToArray();
    }
}
