namespace Aidyn.Rom.Text;

/// <summary>
/// The stream cipher the game applies to its text (the decomp's <c>decrypt_string</c>).
/// </summary>
/// <remarks>
/// A 16-byte key is derived from a fixed base key and two seed values, then evolves as it
/// decrypts: each ciphertext byte is folded back into the key, along with the byte's
/// position. Decryption therefore has to run from the start of each string.
/// </remarks>
public static class StringCipher
{
    /// <summary>Seeds used for <c>RomString</c> tables and cinematic captions.</summary>
    public const byte RomStringSeedA = 0x10;
    public const ushort RomStringSeedB = 0x103;

    // Stored XORed with 0x7F in the ROM code, which is undone before use.
    private static ReadOnlySpan<byte> BaseKey =>
    [
        0x20, 0x4A, 0x45, 0x53, 0x55, 0x53, 0x20, 0x49, 0x53, 0x20, 0x4C, 0x4F, 0x52, 0x44, 0x21, 0x00,
    ];

    /// <summary>Decrypts <paramref name="data"/> in place.</summary>
    public static void Decrypt(Span<byte> data, byte seedA, ushort seedB)
    {
        Span<byte> key = stackalloc byte[16];
        DeriveKey(key, seedA, seedB);

        for (var i = 0; i < data.Length; i++)
        {
            ref var k0 = ref key[i & 15];
            ref var k1 = ref key[(i + 1) & 15];
            ref var k2 = ref key[(i + 2) & 15];

            var cipher = data[i];
            data[i] = (byte)((cipher ^ k2) + k1 - k0);
            k1 ^= cipher;
            k2 ^= (byte)i;
        }
    }

    // The decomp's pseudocode shifts seedB by 7 and mixes only 14 key bytes. Solving for the key
    // from the stat descriptions, whose plaintext is known, showed it's the high byte and all 16.
    private static void DeriveKey(Span<byte> key, byte seedA, ushort seedB)
    {
        BaseKey.CopyTo(key);
        var a = (byte)(((seedB >> 8) + seedA) ^ 0x80);
        var b = (byte)(((byte)seedB - seedA) ^ 2);

        for (var i = 0; i < key.Length; i++)
            key[i] = i % 2 == 0 ? (byte)(b ^ (byte)(a + key[i])) : (byte)(a ^ (byte)(key[i] - b));
    }
}
