using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Rig;

/// <summary>
/// The original's save, a 7,168-byte note on the Controller Pak, field by field as the file holds it (see
/// docs/systems/saves.md). <see cref="Decode"/> and <see cref="Encode"/> round-trip it byte for byte; <see cref="PakSaves"/>
/// turns it into the port's save and back.
/// </summary>
/// <remarks>
/// <c>PakSave.Make</c> (<c>0x8001A08C</c>) builds it: the header, the place, the flags and the minimaps' pieces, the party's
/// bit stream (<c>SaveBlock.Write</c>, <c>0x80019810</c>), the map objects' memories, and last a CRC (<c>Crc.Seal</c>,
/// <c>0x800CBC0C</c>). Multi-byte fields outside the bit stream are big-endian. Bytes not listed are zero.
/// </remarks>
public sealed record PakSave
{
    public const int Size = 0x1C00, FlagBits = 5001, ThumbnailWidth = 64, ThumbnailHeight = 32, MinimapBits = 192;
    public const int PartySheets = 4, KeptSheets = 5, GeneralStacks = 128, ReservedSlots = 80, KeyGearSlots = 13, ChartEntries = 19;

    private const int PlaceOffset = 0x40, FlagsOffset = 0x80, ThumbnailOffset = 0x300, MinimapOffset = 0x1300, StreamOffset = 0x1340,
        ChartOffset = 0x1B00, CrcOffset = 0x1BFC, HeaderNameLength = PlaceOffset - 0xC, AlaronNameLength = 20;

    /// <summary>Time played, in 60 Hz ticks.</summary>
    public uint PlayTime { get; init; }

    /// <summary>The party's four entities in slot order, 0 for an empty slot: the load menu's portraits.</summary>
    public IReadOnlyList<ushort> Portraits { get; init; } = [0, 0, 0, 0];

    /// <summary>Alaron's name, which the load menu shows.</summary>
    public string Name { get; init; } = "";

    public required PakPlace Place { get; init; }

    /// <summary>The plain flags set, 0 to 5000.</summary>
    public IReadOnlySet<int> Flags { get; init; } = new HashSet<int>();

    /// <summary>The load menu's picture, 64 × 32 pixels of RGBA 5551, row by row.</summary>
    public IReadOnlyList<ushort> Thumbnail { get; init; } = new ushort[ThumbnailWidth * ThumbnailHeight];

    /// <summary>The minimaps' pieces seen, numbered across the eight minimaps in the code's order (<c>CodeTable.MinimapSections</c>).</summary>
    public IReadOnlySet<int> Minimaps { get; init; } = new HashSet<int>();

    /// <summary>The in-game clock.</summary>
    public uint Clock { get; init; }

    public uint Gold { get; init; }

    /// <summary>The party's three movement timers, in 7,200 ticks, which nothing reads back.</summary>
    public IReadOnlyList<int> Timers { get; init; } = [0, 0, 0];

    /// <summary>The party in slot order, Alaron first; null for an empty slot.</summary>
    public IReadOnlyList<PakSheet?> Party { get; init; } = new PakSheet?[PartySheets];

    /// <summary>Alaron's name as his own block keeps it, 20 bytes at most.</summary>
    public string AlaronName { get; init; } = "";

    /// <summary>
    /// Alaron's levels of the Elemental, Naming and Necromancy spells, by their place in the spell table: 12, 11 and 13 slots.
    /// </summary>
    public IReadOnlyList<int> AlaronSpells { get; init; } = new int[AlaronSpellSlots];

    /// <summary>
    /// The kept sheets, Abrecan's, Arturo's, Becan's, Godric's and Keelin's (<see cref="KeptEntities"/>): a member's own while
    /// they're in the party, else the one kept when they left, else null.
    /// </summary>
    public IReadOnlyList<PakSheet?> Kept { get; init; } = new PakSheet?[KeptSheets];

    /// <summary>The inventory's 128 general slots, in order.</summary>
    public IReadOnlyList<PakStack> Inventory { get; init; } = [.. Enumerable.Repeat(new PakStack(0, null), GeneralStacks)];

    /// <summary>How many of each reserved item the party holds, by the item's place in <c>CodeTable.ReservedItems</c>.</summary>
    public IReadOnlyList<int> Reserved { get; init; } = new int[ReservedSlots];

    /// <summary>The unique gear with slots of its own, by its place in <c>CodeTable.KeyEquipment</c>.</summary>
    public IReadOnlyList<PakItem?> KeyGear { get; init; } = new PakItem?[KeyGearSlots];

    /// <summary>The weather's intensity, fog and storm, each × 1000, and what falls: 0 none, 1 rain, 2 snow.</summary>
    public PakWeather Weather { get; init; } = new(0, 0, 0, 0);

    /// <summary>The sound and music volumes × 1000.</summary>
    public int SoundVolume { get; init; } = 1000;

    public int MusicVolume { get; init; } = 650;

    public bool HighResolution { get; init; }

    /// <summary>What map objects remember (<c>voxelChart</c>), as RAM holds it.</summary>
    public PakChart Chart { get; init; } = PakChart.Empty;

    /// <summary>The entities whose sheets <see cref="Kept"/> holds, in order.</summary>
    public static readonly IReadOnlyList<ushort> KeptEntities = [0x023E, 0x0240, 0x0242, 0x0248, 0x0249];

    /// <summary>The slots of the Elemental, Naming and Necromancy spells.</summary>
    public const int AlaronSpellSlots = 12 + 11 + 13;

    /// <summary>The spell table's slots of a sheet's first block, the unschooled spells, and of its second.</summary>
    public const int UnschooledSlots = 10, SchooledSlots = 13;

    /// <summary>A save from a note's bytes, which the CRC must hold for.</summary>
    /// <param name="schoolOf">Each entity's school: a Chaos character's sheet is longer.</param>
    public static PakSave Decode(ReadOnlySpan<byte> file, Func<int, MagicSchool> schoolOf)
    {
        if (file.Length < Size)
            throw new InvalidDataException($"A save is {Size:N0} bytes, not {file.Length:N0}.");
        file = file[..Size];
        if (!Crc.Holds(file))
            throw new InvalidDataException("The save's CRC is wrong: the game calls it corrupt.");

        var bits = new BitReader(file[StreamOffset..ChartOffset]);
        var clock = bits.Read(32);
        var gold = bits.Read(32);
        int[] timers = [(int)bits.Read(12), (int)bits.Read(12), (int)bits.Read(12)];
        var party = new PakSheet?[PartySheets];
        var alaronName = "";
        var alaronSpells = new int[AlaronSpellSlots];
        for (var i = 0; i < PartySheets; i++)
        {
            party[i] = PakSheet.Read(ref bits, true, schoolOf);
            if (i == 0)
            {
                alaronName = Text(bits.ReadBytes(AlaronNameLength));
                for (var s = 0; s < AlaronSpellSlots; s++)
                    alaronSpells[s] = (int)bits.Read(4);
            }
        }
        var kept = new PakSheet?[KeptSheets];
        for (var i = 0; i < KeptSheets; i++)
            kept[i] = PakSheet.Read(ref bits, false, schoolOf);
        var inventory = new PakStack[GeneralStacks];
        for (var i = 0; i < GeneralStacks; i++)
        {
            var quantity = (int)bits.Read(8);
            inventory[i] = new PakStack(quantity, PakItem.Read(ref bits));
        }
        var reserved = new int[ReservedSlots];
        for (var i = 0; i < ReservedSlots; i++)
            reserved[i] = (int)bits.Read(8);
        var keyGear = new PakItem?[KeyGearSlots];
        for (var i = 0; i < KeyGearSlots; i++)
            keyGear[i] = PakItem.Read(ref bits);
        var weather = new PakWeather((int)bits.Read(32), (int)bits.Read(32), (int)bits.Read(32), (int)bits.Read(8));
        var sound = (int)bits.Read(32);
        var music = (int)bits.Read(32);
        var high = bits.Read(1) != 0;

        var portraits = new ushort[4];
        for (var i = 0; i < portraits.Length; i++)
            portraits[i] = BinaryPrimitives.ReadUInt16BigEndian(file[(4 + i * 2)..]);
        var thumbnail = new ushort[ThumbnailWidth * ThumbnailHeight];
        for (var i = 0; i < thumbnail.Length; i++)
            thumbnail[i] = BinaryPrimitives.ReadUInt16BigEndian(file[(ThumbnailOffset + i * 2)..]);

        return new PakSave
        {
            PlayTime = BinaryPrimitives.ReadUInt32BigEndian(file),
            Portraits = portraits,
            Name = Text(file[0xC..PlaceOffset]),
            Place = PakPlace.Read(file[PlaceOffset..]),
            Flags = SetBits(file[FlagsOffset..], FlagBits),
            Thumbnail = thumbnail,
            Minimaps = SetBits(file[MinimapOffset..], MinimapBits),
            Clock = clock,
            Gold = gold,
            Timers = timers,
            Party = party,
            AlaronName = alaronName,
            AlaronSpells = alaronSpells,
            Kept = kept,
            Inventory = inventory,
            Reserved = reserved,
            KeyGear = keyGear,
            Weather = weather,
            SoundVolume = sound,
            MusicVolume = music,
            HighResolution = high,
            Chart = PakChart.Read(file[ChartOffset..CrcOffset]),
        };
    }

    /// <summary>The note's bytes, with the CRC sealed.</summary>
    public byte[] Encode(Func<int, MagicSchool> schoolOf)
    {
        if (Party.Count != PartySheets || Kept.Count != KeptSheets || Inventory.Count != GeneralStacks || Reserved.Count != ReservedSlots
            || KeyGear.Count != KeyGearSlots || AlaronSpells.Count != AlaronSpellSlots || Portraits.Count != 4 || Timers.Count != 3
            || Thumbnail.Count != ThumbnailWidth * ThumbnailHeight)
            throw new InvalidOperationException("A save's lists have the lengths the file gives them.");
        var file = new byte[Size];
        BinaryPrimitives.WriteUInt32BigEndian(file, PlayTime);
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4 + i * 2), Portraits[i]);
        WriteText(file.AsSpan(0xC, HeaderNameLength - 1), Name);
        Place.Write(file.AsSpan(PlaceOffset));
        WriteBits(file.AsSpan(FlagsOffset), Flags, FlagBits);
        for (var i = 0; i < Thumbnail.Count; i++)
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(ThumbnailOffset + i * 2), Thumbnail[i]);
        WriteBits(file.AsSpan(MinimapOffset), Minimaps, MinimapBits);

        var bits = new BitWriter(file.AsSpan(StreamOffset, ChartOffset - StreamOffset));
        bits.Write(Clock, 32);
        bits.Write(Gold, 32);
        foreach (var timer in Timers)
            bits.Write((uint)timer, 12);
        for (var i = 0; i < PartySheets; i++)
        {
            PakSheet.Write(ref bits, Party[i], true, schoolOf);
            if (i == 0)
            {
                var name = new byte[AlaronNameLength];
                WriteText(name.AsSpan(0, AlaronNameLength - 1), AlaronName);
                foreach (var b in name)
                    bits.Write(b, 8);
                foreach (var level in AlaronSpells)
                    bits.Write((uint)level, 4);
            }
        }
        foreach (var sheet in Kept)
            PakSheet.Write(ref bits, sheet, false, schoolOf);
        foreach (var stack in Inventory)
        {
            bits.Write((uint)stack.Quantity, 8);
            PakItem.Write(ref bits, stack.Quantity == 0 ? null : stack.Item);
        }
        foreach (var quantity in Reserved)
            bits.Write((uint)quantity, 8);
        foreach (var item in KeyGear)
            PakItem.Write(ref bits, item);
        bits.Write((uint)Weather.Intensity, 32);
        bits.Write((uint)Weather.Fog, 32);
        bits.Write((uint)Weather.Storm, 32);
        bits.Write((uint)Weather.Precipitation, 8);
        bits.Write((uint)SoundVolume, 32);
        bits.Write((uint)MusicVolume, 32);
        bits.Write(HighResolution ? 1u : 0u, 1);

        Chart.Write(file.AsSpan(ChartOffset, CrcOffset - ChartOffset));
        Crc.Seal(file);
        return file;
    }

    /// <summary>A name in the save's bytes, up to its NUL.</summary>
    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }

    private static void WriteText(Span<byte> to, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        if (bytes.Length > to.Length)
            throw new InvalidOperationException($"\"{text}\" is longer than the save keeps, {to.Length} bytes.");
        bytes.CopyTo(to);
    }

    /// <summary>The bits set of a run of bits, lowest first in each byte.</summary>
    private static HashSet<int> SetBits(ReadOnlySpan<byte> bytes, int count)
    {
        var set = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            if ((bytes[i >> 3] >> (i & 7) & 1) != 0)
                set.Add(i);
        }
        return set;
    }

    private static void WriteBits(Span<byte> bytes, IEnumerable<int> set, int count)
    {
        foreach (var i in set)
        {
            if (i < 0 || i >= count)
                throw new InvalidOperationException($"Bit {i} isn't one of the {count} the save keeps.");
            bytes[i >> 3] |= (byte)(1 << (i & 7));
        }
    }

    /// <summary>
    /// The save's CRC (<c>CRC.cpp</c>): the MSB-first CRC-32 of polynomial 0x04C11DB7 over the whole file, from 0, with no final
    /// XOR, its last word read as zero; plus 8, in the last word (<c>Crc.Seal</c>, <c>0x800CBC0C</c>). <c>Crc.Check</c>
    /// (<c>0x800CBBD4</c>) takes the 8 off and expects the CRC of the whole file to be 0.
    /// </summary>
    internal static class Crc
    {
        private static readonly uint[] Table = [.. Enumerable.Range(0, 256).Select(i =>
        {
            var v = (uint)i << 24;
            for (var k = 0; k < 8; k++)
                v = (v & 0x80000000) != 0 ? v << 1 ^ 0x04C11DB7 : v << 1;
            return v;
        })];

        private static uint Compute(ReadOnlySpan<byte> data, int zeroFrom)
        {
            var crc = 0u;
            for (var i = 0; i < data.Length; i++)
                crc = (crc << 8 | (i >= zeroFrom ? 0u : data[i])) ^ Table[crc >> 24];
            return crc;
        }

        public static bool Holds(ReadOnlySpan<byte> file) =>
            Compute(file, file.Length - 4) + 8 == BinaryPrimitives.ReadUInt32BigEndian(file[^4..]);

        public static void Seal(Span<byte> file) => BinaryPrimitives.WriteUInt32BigEndian(file[^4..], Compute(file, file.Length - 4) + 8);
    }
}

/// <summary>
/// Where the player stands (<c>Zones.SavedPosition</c>, <c>0x800D906C</c>, which loading starts the zone engine from).
/// </summary>
/// <param name="Position">The centre of the player's sphere, in the chunk's coordinates.</param>
/// <param name="Facing">The original's <c>facing</c> in X and Z, the opposite of the way the player faces.</param>
/// <param name="Camera">The camera's eye, about 5 behind the player and 2.3 above.</param>
/// <param name="Row">The chunk's row, from 1 for A.</param>
/// <param name="MapSet">The map set's number.</param>
/// <param name="Marks">Two halfwords the zone engine keeps, 0xFFFF in every save.</param>
public sealed record PakPlace(Vector3 Position, Vector2 Facing, Vector3 Camera, int Row, int Column, int MapSet, int Marks = 0xFFFF)
{
    internal static PakPlace Read(ReadOnlySpan<byte> b)
    {
        static float F(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadSingleBigEndian(b[at..]);
        static int H(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16BigEndian(b[at..]);
        if (H(b, 0x26) != H(b, 0x28))
            throw new InvalidDataException($"The place's marks differ ({H(b, 0x26):x4}, {H(b, 0x28):x4}), which no save seen has.");
        return new PakPlace(new Vector3(F(b, 0), F(b, 4), F(b, 8)), new Vector2(F(b, 0xC), F(b, 0x10)),
            new Vector3(F(b, 0x14), F(b, 0x18), F(b, 0x1C)), H(b, 0x20), H(b, 0x22), H(b, 0x24), H(b, 0x26));
    }

    internal void Write(Span<byte> b)
    {
        float[] floats = [Position.X, Position.Y, Position.Z, Facing.X, Facing.Y, Camera.X, Camera.Y, Camera.Z];
        for (var i = 0; i < floats.Length; i++)
            BinaryPrimitives.WriteSingleBigEndian(b[(i * 4)..], floats[i]);
        int[] halves = [Row, Column, MapSet, Marks, Marks];
        for (var i = 0; i < halves.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(b[(0x20 + i * 2)..], (ushort)halves[i]);
    }
}

/// <summary>
/// A character sheet in the save (<c>WriteSheet</c>, <c>0x8001918C</c>; a party member's, <c>WritePartySheet</c>,
/// <c>0x800193AC</c>, adds the spell effects and potions). Loading builds the sheet from the entity and takes these.
/// </summary>
/// <param name="Trained">The trained stats, Intelligence to Level.</param>
/// <param name="Current">Trained less damage.</param>
/// <param name="Skills">The 12 skills, then Hafted, Missile, Pole, Sword, Thrown and the shield: 0xF can't be learnt.</param>
/// <param name="Gear">The 12 gear slots.</param>
/// <param name="Unschooled">The levels of the unschooled spells, by their place in the spell table.</param>
/// <param name="Schooled">The levels of the school's spells (the Star spells for Chaos), then zeroes to 13.</param>
/// <param name="Effects">A party member's 15 spell effects, which the original never saves.</param>
/// <param name="Potions">A party member's 7 potions working, which the original never saves.</param>
public sealed record PakSheet(
    int Entity,
    uint TotalExp,
    uint SpendableExp,
    IReadOnlyList<int> Trained,
    IReadOnlyList<int> Current,
    IReadOnlyList<int> Skills,
    PakItem? Armor,
    PakItem? Shield,
    PakItem? Weapon,
    IReadOnlyList<PakItem?> Gear,
    IReadOnlyList<int> Unschooled,
    IReadOnlyList<int> Schooled,
    IReadOnlyList<PakEffect?> Effects,
    IReadOnlyList<PakPotion?> Potions)
{
    /// <summary>The bits of a sheet without its effects and potions, and with them.</summary>
    private const int KeptBits = 720, PartyBits = 0x500, ChaosPadding = 13 * 4, EffectCount = 15, PotionCount = 7;

    /// <summary>The order the stats are saved in, Stamina first, and each one's bits.</summary>
    internal static readonly (int Stat, int Bits)[] StatOrder = [(5, 8), (3, 6), (6, 6), (1, 5), (0, 5), (4, 5), (2, 5)];

    /// <summary>The skills saved, by their place in <see cref="SkillSlots"/>: all 12, five weapon skills and the shield.</summary>
    public static readonly IReadOnlyList<int> SavedSkills = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 15, 16, 17, 20, 21, 23];

    internal static PakSheet? Read(ref BitReader bits, bool party, Func<int, MagicSchool> schoolOf)
    {
        var id = (int)bits.Read(8);
        if (id == 0)
        {
            bits.Skip((party ? PartyBits : KeptBits) - 8);
            return null;
        }
        var entity = 0x200 + id;
        var totalExp = bits.Read(24);
        var spendableExp = bits.Read(24);
        int[] Stats(ref BitReader bits)
        {
            var stats = new int[7];
            foreach (var (stat, width) in StatOrder)
                stats[stat] = (int)bits.Read(width);
            return stats;
        }
        var trained = Stats(ref bits);
        var current = Stats(ref bits);
        var skills = new int[SavedSkills.Count];
        for (var i = 0; i < skills.Length; i++)
            skills[i] = (int)bits.Read(4);
        var armor = PakItem.Read(ref bits);
        var shield = PakItem.Read(ref bits);
        var weapon = PakItem.Read(ref bits);
        var gear = new PakItem?[12];
        for (var i = 0; i < gear.Length; i++)
            gear[i] = PakItem.Read(ref bits);
        var unschooled = new int[PakSave.UnschooledSlots];
        for (var i = 0; i < unschooled.Length; i++)
            unschooled[i] = (int)bits.Read(4);
        var schooled = new int[PakSave.SchooledSlots];
        for (var i = 0; i < schooled.Length; i++)
            schooled[i] = (int)bits.Read(4);
        // The code pads the school's block by 13 less the school's count, and Chaos's count is 0 (WriteSpells, 0x80018DC4).
        if (schoolOf(entity) == MagicSchool.Chaos)
            bits.Skip(ChaosPadding);
        var effects = new PakEffect?[party ? EffectCount : 0];
        var potions = new PakPotion?[party ? PotionCount : 0];
        for (var i = 0; i < effects.Length; i++)
            effects[i] = PakEffect.Read(ref bits);
        for (var i = 0; i < potions.Length; i++)
            potions[i] = PakPotion.Read(ref bits);
        return new PakSheet(entity, totalExp, spendableExp, trained, current, skills, armor, shield, weapon, gear, unschooled, schooled, effects, potions);
    }

    internal static void Write(ref BitWriter bits, PakSheet? sheet, bool party, Func<int, MagicSchool> schoolOf)
    {
        if (sheet is null)
        {
            bits.Skip(party ? PartyBits : KeptBits);
            return;
        }
        if (sheet.Entity is <= 0x200 or > 0x2FF)
            throw new InvalidOperationException($"Entity 0x{sheet.Entity:X4} can't be saved: the save keeps 0x0201 to 0x02FF.");
        bits.Write((uint)(sheet.Entity - 0x200), 8);
        bits.Write(sheet.TotalExp, 24);
        bits.Write(sheet.SpendableExp, 24);
        foreach (var stats in new[] { sheet.Trained, sheet.Current })
        {
            foreach (var (stat, width) in StatOrder)
                bits.Write((uint)stats[stat], width);
        }
        foreach (var skill in sheet.Skills)
            bits.Write((uint)skill, 4);
        PakItem.Write(ref bits, sheet.Armor);
        PakItem.Write(ref bits, sheet.Shield);
        PakItem.Write(ref bits, sheet.Weapon);
        foreach (var gear in sheet.Gear)
            PakItem.Write(ref bits, gear);
        foreach (var level in sheet.Unschooled.Concat(sheet.Schooled))
            bits.Write((uint)level, 4);
        if (schoolOf(sheet.Entity) == MagicSchool.Chaos)
            bits.Skip(ChaosPadding);
        if (!party)
            return;
        for (var i = 0; i < EffectCount; i++)
            PakEffect.Write(ref bits, i < sheet.Effects.Count ? sheet.Effects[i] : null);
        for (var i = 0; i < PotionCount; i++)
            PakPotion.Write(ref bits, i < sheet.Potions.Count ? sheet.Potions[i] : null);
    }
}

/// <summary>
/// An item in the save, 28 bits (<c>WriteItem</c>, <c>0x80018970</c>): its DB ID, its spell's charges and its stat mod.
/// </summary>
/// <param name="StatMod">
/// <c>stat &lt;&lt; 7 | (amount + 50) &amp; 0x7F</c>, or 0 to take the item's own: a battery's Stamina left.
/// </param>
public sealed record PakItem(int Id, int Charges, int StatMod)
{
    public (int Stat, int Amount)? Stat => StatMod == 0 ? null : (StatMod >> 7, (StatMod & 0x7F) - 50);

    public static int MakeStatMod(int stat, int amount) => stat << 7 | (amount + 50) & 0x7F;

    internal static PakItem? Read(ref BitReader bits)
    {
        var id = (int)bits.Read(13);
        var charges = (int)bits.Read(5);
        var statMod = (int)bits.Read(10);
        return id == 0 && charges == 0 && statMod == 0 ? null : new PakItem(id, charges, statMod);
    }

    internal static void Write(ref BitWriter bits, PakItem? item)
    {
        if (item is null)
        {
            bits.Skip(28);
            return;
        }
        if (item.Id is < 0 or >= 0x2000 || item.Charges is < 0 or >= 32 || item.StatMod is < 0 or >= 0x400)
            throw new InvalidOperationException($"Item 0x{item.Id:X4} with {item.Charges} charges doesn't fit the save's 28 bits.");
        bits.Write((uint)item.Id, 13);
        bits.Write((uint)item.Charges, 5);
        bits.Write((uint)item.StatMod, 10);
    }
}

/// <summary>A stack in one of the inventory's general slots. A slot with no quantity holds no item.</summary>
public sealed record PakStack(int Quantity, PakItem? Item);

/// <summary>A spell effect on a party member, 28 bits (<c>WriteEffect</c>). The timer is in 7,200 ticks.</summary>
public sealed record PakEffect(int Power, int Kind, int Margin, bool Harmful, int Timer)
{
    internal static PakEffect? Read(ref BitReader bits)
    {
        var effect = new PakEffect((int)bits.Read(4), (int)bits.Read(6), (int)bits.Read(5), bits.Read(1) != 0, (int)bits.Read(12));
        return effect == new PakEffect(0, 0, 0, false, 0) ? null : effect;
    }

    internal static void Write(ref BitWriter bits, PakEffect? e)
    {
        e ??= new PakEffect(0, 0, 0, false, 0);
        bits.Write((uint)e.Power, 4);
        bits.Write((uint)e.Kind, 6);
        bits.Write((uint)e.Margin, 5);
        bits.Write(e.Harmful ? 1u : 0u, 1);
        bits.Write((uint)e.Timer, 12);
    }
}

/// <summary>A potion working on a party member, 20 bits (<c>WritePotion</c>). The timer is in 7,200 ticks.</summary>
public sealed record PakPotion(int Kind, int Power, int Timer)
{
    internal static PakPotion? Read(ref BitReader bits)
    {
        var potion = new PakPotion((int)bits.Read(4), (int)bits.Read(4), (int)bits.Read(12));
        return potion == new PakPotion(0, 0, 0) ? null : potion;
    }

    internal static void Write(ref BitWriter bits, PakPotion? p)
    {
        p ??= new PakPotion(0, 0, 0);
        bits.Write((uint)p.Kind, 4);
        bits.Write((uint)p.Power, 4);
        bits.Write((uint)p.Timer, 12);
    }
}

/// <summary>The weather as saved: the roll's intensity, fog and storm, each × 1000 and truncated, and what falls.</summary>
public sealed record PakWeather(int Intensity, int Fog, int Storm, int Precipitation);

/// <summary>
/// What map objects remember (<c>voxelChart</c>), copied from RAM: how many entries are used (<c>0x800E0A70</c>), the stack
/// of slot numbers (<c>0x800E0B58</c>, the used first), and the 19 entries of 12 bytes (<c>0x800E0A74</c>).
/// </summary>
public sealed record PakChart(int Count, IReadOnlyList<int> Slots, IReadOnlyList<CheckpointMemory?> Entries)
{
    private const int EntrySize = 12, EntriesOffset = 0x14;

    public static PakChart Empty { get; } = new(0, [.. Enumerable.Range(0, PakSave.ChartEntries)], new CheckpointMemory?[PakSave.ChartEntries]);

    /// <summary>A chart of these memories, in slots 0 on, as a chart they were made in order holds them.</summary>
    public static PakChart Of(IReadOnlyList<CheckpointMemory> memories)
    {
        if (memories.Count > PakSave.ChartEntries)
            throw new InvalidOperationException($"The chart holds {PakSave.ChartEntries} memories, not {memories.Count}.");
        return new(memories.Count, Empty.Slots,
            [.. memories, .. Enumerable.Repeat<CheckpointMemory?>(null, PakSave.ChartEntries - memories.Count)]);
    }

    /// <summary>The memories, in slot order, as <see cref="AresCheckpoint.Memories"/> reads them. A freed slot is zeroed.</summary>
    public IEnumerable<CheckpointMemory> Used => Entries.OfType<CheckpointMemory>();

    internal static PakChart Read(ReadOnlySpan<byte> b)
    {
        var entries = new CheckpointMemory?[PakSave.ChartEntries];
        for (var i = 0; i < entries.Length; i++)
        {
            var e = b.Slice(EntriesOffset + i * EntrySize, EntrySize);
            if (e.IndexOfAnyExcept((byte)0) < 0)
                continue;
            if (e[2] != 1)
                throw new InvalidDataException($"The chart's entry {i} isn't empty but isn't marked used either.");
            entries[i] = new CheckpointMemory(BinaryPrimitives.ReadInt16BigEndian(e), e[3], e[4], e[5], e[6], e[7], BinaryPrimitives.ReadUInt32BigEndian(e[8..]));
        }
        return new PakChart(b[0], [.. b.Slice(1, PakSave.ChartEntries).ToArray().Select(s => (int)s)], entries);
    }

    internal void Write(Span<byte> b)
    {
        b[0] = (byte)Count;
        for (var i = 0; i < PakSave.ChartEntries; i++)
            b[1 + i] = (byte)Slots[i];
        for (var i = 0; i < PakSave.ChartEntries; i++)
        {
            if (Entries[i] is not { } m)
                continue;
            var e = b.Slice(EntriesOffset + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteInt16BigEndian(e, (short)m.Index);
            e[2] = 1;
            (e[3], e[4], e[5], e[6], e[7]) = ((byte)m.Lifetime, (byte)m.MapSet, (byte)m.Row, (byte)m.Column, (byte)m.Type);
            BinaryPrimitives.WriteUInt32BigEndian(e[8..], m.Value);
        }
    }
}


/// <summary>Reads the save's bit stream: a field of n bits takes the next n, its lowest bit first (<c>SaveBits.Read</c>, <c>0x8000277C</c>).</summary>
internal ref struct BitReader(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;
    private int _bit;

    public uint Read(int count)
    {
        var value = 0u;
        for (var k = 0; k < count; k++, _bit++)
        {
            if (_bit >> 3 >= _bytes.Length)
                throw new InvalidDataException("The save's party runs past the room it has.");
            value |= (uint)(_bytes[_bit >> 3] >> (_bit & 7) & 1) << k;
        }
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
            bytes[i] = (byte)Read(8);
        return bytes;
    }

    public void Skip(int count) => _bit += count;
}

/// <summary>
/// Writes the save's bit stream (<c>SaveBits.Write</c>, <c>0x80002700</c>), over zeroes: a skip leaves them. Like the
/// original, it writes a value's low bits and drops the rest, so Sholeh's Intelligence of 35 is saved as 3.
/// </summary>
internal ref struct BitWriter(Span<byte> bytes)
{
    private readonly Span<byte> _bytes = bytes;
    private int _bit;

    public void Write(uint value, int count)
    {
        for (var k = 0; k < count; k++, _bit++)
        {
            if (_bit >> 3 >= _bytes.Length)
                throw new InvalidOperationException("The party doesn't fit the room the save has for it.");
            _bytes[_bit >> 3] |= (byte)((value >> k & 1) << (_bit & 7));
        }
    }

    public void Skip(int count) => _bit += count;
}
