using System.Text;

namespace HBHT.Rom.Text;

/// <summary>Turns the game's byte strings into .NET strings.</summary>
public static class GameText
{
    /// <summary>
    /// Decodes a fixed-size name field. The name ends at the first NUL; anything after it is
    /// leftover bytes, not text.
    /// </summary>
    public static string Field(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>Decrypts a copy of <paramref name="encrypted"/> and decodes it up to the first NUL.</summary>
    public static string Decrypt(ReadOnlySpan<byte> encrypted, byte seedA, ushort seedB)
    {
        Span<byte> plain = encrypted.Length <= 256 ? stackalloc byte[encrypted.Length] : new byte[encrypted.Length];
        encrypted.CopyTo(plain);
        StringCipher.Decrypt(plain, seedA, seedB);
        return Field(plain);
    }
}
