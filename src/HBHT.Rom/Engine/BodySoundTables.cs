namespace HBHT.Rom.Engine;

/// <summary>An audio key as the ROM holds it: <c>Ids</c> index the sound list its user names.</summary>
public sealed record RomSoundKey(int Frac, int Request, IReadOnlyList<int> Ids);

/// <summary>An entity's audio key profile: its kind and its keys.</summary>
public sealed record RomBodySounds(int Kind, IReadOnlyList<RomSoundKey> Keys);

/// <summary>
/// Bodies' sounds (<c>audiokey.cpp</c>, docs/systems/audio.md): the audio keys in the DB block, one profile per entity
/// list position, and the code segment's footstep keys and sets, the sound lists the keys name, each weapon's and each
/// spell's.
/// </summary>
/// <param name="Profiles">By entity list position. Positions past the end read as kind 0, silent.</param>
/// <param name="Footsteps">The footstep keys; their ids index <paramref name="StepSounds"/>. Entry 0 is empty.</param>
/// <param name="StepSets">By set: each surface's sneak, walk and run footstep key, 0 for none.</param>
/// <param name="StepSounds">The Borg12s step ids name.</param>
/// <param name="VoiceSounds">The Borg12s voice ids and the weapons' ids name.</param>
/// <param name="SpellSounds">Each spell's cast, as a Borg12, by spell DB position.</param>
/// <param name="WeaponSounds">By weapon DB position: its two voice ids.</param>
public sealed record BodySoundTables(
    IReadOnlyList<RomBodySounds> Profiles,
    IReadOnlyList<RomSoundKey?> Footsteps,
    IReadOnlyList<IReadOnlyList<(int Sneak, int Walk, int Run)>> StepSets,
    IReadOnlyList<int> StepSounds,
    IReadOnlyList<int> VoiceSounds,
    IReadOnlyList<int> SpellSounds,
    IReadOnlyDictionary<int, (int, int)> WeaponSounds)
{
    /// <summary>The collision surfaces a step set covers.</summary>
    public const int Surfaces = 26;

    /// <summary>The footstep set each body kind uses (<c>AudioKeys.ProcessPlayer</c>), kinds 1 to 5.</summary>
    public static readonly int[] KindStepSet = [-1, 0, 0, 1, 2, 3];

    /// <summary>How loud each body kind's footsteps are (doubles at 1.0 <c>0x800D2070</c>, <c>0x800D2080</c>).</summary>
    public static readonly double[] KindLoudness = [0, 0.5, 0.75, 1, 1, 1];

    public static BodySoundTables Read(RomImage rom)
    {
        var table = rom.Table(DbTable.AudioKeys).ToArray();
        int U16(ReadOnlySpan<byte> d, int at) => Be.U16(d, at);
        int U32(ReadOnlySpan<byte> d, int at) => (int)Be.U32(d, at);

        // Three u32 offsets, then three u16 counts.
        int profiles = U32(table, 0), keys = U32(table, 4), indices = U32(table, 8);
        int profileCount = U16(table, 12), keyCount = U16(table, 14);
        var keyList = new List<RomSoundKey>();
        for (var k = 0; k < keyCount; k++)
            keyList.Add(Key(table, keys + k * 8));
        var profileList = new List<RomBodySounds>();
        for (var p = 0; p < profileCount; p++)
        {
            var at = profiles + p * 8;
            int count = U16(table, at + 2), start = U16(table, at + 4);
            var profileKeys = new List<RomSoundKey>();
            for (var i = start; i < start + count; i++)
                profileKeys.Add(keyList[U16(table, indices + i * 2)]);
            profileList.Add(new RomBodySounds(U16(table, at), profileKeys));
        }

        var footstepData = rom.Table(CodeTable.FootstepEntries);
        var footsteps = new List<RomSoundKey?> { null };
        for (var at = 8; at + 8 <= footstepData.Length; at += 8)
            footsteps.Add(Key(footstepData, at));

        var setData = rom.Table(CodeTable.StepSets).ToArray();
        var sets = new List<IReadOnlyList<(int, int, int)>>();
        for (var at = 0; at + 0x50 <= setData.Length; at += 0x50)
            sets.Add([.. Enumerable.Range(0, Surfaces).Select(s => ((int)setData[at + s * 3], (int)setData[at + s * 3 + 1], (int)setData[at + s * 3 + 2]))]);

        // A weapon's row is its place in the list of weapons with models (bytes, ending with 0xFF).
        var withModels = rom.Table(CodeTable.WeaponsWithModels);
        var weaponData = rom.Table(CodeTable.WeaponSounds);
        var weapons = new Dictionary<int, (int, int)>();
        for (var row = 0; row < withModels.Length && withModels[row] != 0xFF && (row + 1) * 8 <= weaponData.Length; row++)
            weapons[withModels[row]] = (U16(weaponData, row * 8), U16(weaponData, row * 8 + 2));

        return new BodySoundTables(profileList, footsteps, sets,
            Be.S32s(rom.Table(CodeTable.StepSounds)), Be.S32s(rom.Table(CodeTable.VoiceSounds)),
            Be.S32s(rom.Table(CodeTable.SpellSounds)), weapons);
    }

    /// <summary>A key: a u16 frac, the request, a count of ids and four ids.</summary>
    private static RomSoundKey Key(ReadOnlySpan<byte> data, int at) =>
        new(Be.U16(data, at), data[at + 2], [.. data.Slice(at + 4, Math.Min((int)data[at + 3], 4)).ToArray()]);
}
