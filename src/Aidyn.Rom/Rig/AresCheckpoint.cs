using System.Buffers.Binary;
using System.Numerics;

namespace Aidyn.Rom.Rig;

/// <summary>
/// The RAM of an ares save state (<c>.bs1</c>–<c>.bs9</c>), for checking the port against the
/// checkpoints the user has captured (see docs/reference-rig.md).
/// </summary>
/// <remarks>
/// ares stores RDRAM whole, as 32-bit little-endian words, but doesn't say where in the file. Rather
/// than fingerprint the code segment against a ROM (<c>tools/bst.py</c>), this finds the game's flag
/// table (<c>GameStateFunnel</c>), whose header is unmistakable, and takes RDRAM's start from where the
/// header sits and the address its own pointers give it. So it needs no ROM and reads 1.0's states and
/// the debug ROM's alike.
/// </remarks>
public sealed class AresCheckpoint
{
    private const int RdramSize = 0x800000;
    private const int FlagCount = 5627, BitFlagCount = 5001;
    /// <summary>The funnel's header, then its flag entries (each 8 bytes, the value at +6).</summary>
    private const int HeaderSize = 0x20, EntrySize = 8, ValueOffset = 6;

    private readonly byte[] _state;
    private readonly int _base;
    private readonly uint _funnel;
    private bool[]? _flags;
    private IReadOnlyList<CheckpointCharacter>? _party;
    private Build? _build;
    private CheckpointPlace? _place;
    private IReadOnlyList<CheckpointMemory>? _memories;

    private AresCheckpoint(byte[] state, int rdramStart, uint funnel)
    {
        _state = state;
        _base = rdramStart;
        _funnel = funnel;
    }

    /// <summary>Where the flag table is (<c>GameStateFunnel</c>), a KSEG0 address.</summary>
    public uint FlagTableAddress => _funnel;

    /// <summary>Every flag's value, by number.</summary>
    public IReadOnlyList<bool> Flags => _flags ??= [.. Enumerable.Range(0, FlagCount)
        .Select(i => ReadByte(_funnel + HeaderSize + (uint)(i * EntrySize + ValueOffset)) != 0)];

    public static AresCheckpoint Read(string path) => Read(File.ReadAllBytes(path));

    public static AresCheckpoint Read(byte[] state)
    {
        if (state.Length < 4 || state[0] != 'B' || state[1] != 'S' || state[2] != 'T' || state[3] != '1')
            throw new InvalidDataException("Not an ares state (no BST1 magic).");

        (int Offset, uint Address)? found = null;
        // The words are little-endian in the file: the flag count's bytes are 2 and 3 of the header's
        // first word, the bit flag count's are 6 and 7.
        for (var i = 4; i + HeaderSize <= state.Length; i++)
        {
            if (state[i + 2] != FlagCount % 256 || state[i + 3] != FlagCount / 256
                || state[i + 6] != BitFlagCount % 256 || state[i + 7] != BitFlagCount / 256)
                continue;

            var entries = Word(state, i + 8);
            var inputs = Word(state, i + 12);
            if ((entries & 3) != 0 || entries < 0x80000000 + HeaderSize || entries >= 0x80000000 + RdramSize
                || inputs != entries + FlagCount * EntrySize)
                continue;
            var address = entries - HeaderSize;
            var start = i - (int)(address & 0x7FFFFF);
            if (start < 4 || start + RdramSize > state.Length)
                continue;
            if (found is not null)
                throw new InvalidDataException("The flag table is in this state more than once.");
            found = (start, address);
        }
        if (found is not { } f)
            throw new InvalidDataException("Can't find the flag table in this state, so can't tell where RAM is.");
        return new AresCheckpoint(state, f.Offset, f.Address);
    }

    /// <summary>
    /// Where the globals the port reads are in a build of the game: <c>gGlobals</c> with its
    /// <c>party</c> pointer, the <c>TerrainPointer</c>, and the <c>voxelChart</c>.
    /// </summary>
    private sealed record Build(uint Globals, uint PartyPointer, uint TerrainPointer, uint VoxelChart);

    /// <summary>1.0's addresses, then the debug build's. A state doesn't say which it's of, so the first whose party is there is.</summary>
    private static readonly Build[] Builds =
    [
        new(0x800D8088, 0x800D9684, 0x800E303C, 0x800E0A74),
        new(0x800E68A8, 0x800E7EA0, 0x800F1BBC, 0x800EF5E4),
    ];

    /// <summary>The build this state is of, found once, by which one's party pointer leads to a party.</summary>
    private Build WhichBuild() => _build ??= Builds.FirstOrDefault(b => PartyAt(b.PartyPointer) is not null)
        ?? throw new InvalidDataException("Can't find the party in this state, so can't tell which build it's of.");

    /// <summary>
    /// The party's character sheets, in slot order (<c>Party::Members</c>, <c>CharSheet</c>), as the
    /// console holds them.
    /// </summary>
    public IReadOnlyList<CheckpointCharacter> Party => _party ??= ReadParty();

    /// <summary>
    /// Where the player is: the map set and chunk (<c>gGlobals.gameVars.mapDatA</c>, <c>mapShort1</c> and
    /// <c>mapShort2</c>, at <c>gGlobals</c> + 0x38C, 0x372 and 0x374 in both builds), and the position and
    /// facing the zone engine last noted for a save (<c>gameVars.MapFloatDatEntry</c>, at <c>gGlobals</c> +
    /// 0xFE4: <c>playerVec3</c>, then <c>playerVec2</c> at +0xC), which it notes every two seconds.
    /// </summary>
    public CheckpointPlace Place => _place ??= ReadPlace();

    /// <summary>The in-game clock (<c>TerrainStruct.InGameTime</c>, at <c>TerrainPointer</c>'s + 0x20).</summary>
    public uint Time => ReadWord(ReadWord(WhichBuild().TerrainPointer) + 0x20);

    /// <summary>Time played, in 60 Hz ticks (<c>TerrainStruct.PlayTime</c>, at <c>TerrainPointer</c>'s + 0x34).</summary>
    public uint PlayTime => ReadWord(ReadWord(WhichBuild().TerrainPointer) + 0x34);

    /// <summary>
    /// What map objects remember (<c>voxelChart</c>, 19 entries of 12 bytes), the slots in use in slot
    /// order. 1.0 has the chart at 0x800E0A74, the debug build at 0x800EF5E4.
    /// </summary>
    public IReadOnlyList<CheckpointMemory> Memories => _memories ??= ReadMemories();

    private CheckpointPlace ReadPlace()
    {
        var globals = WhichBuild().Globals;
        var entry = globals + 0xFE4;
        return new CheckpointPlace(
            Half(globals + 0x38C), Half(globals + 0x372), Half(globals + 0x374),
            new Vector3(Float(entry), Float(entry + 4), Float(entry + 8)),
            new Vector2(Float(entry + 0xC), Float(entry + 0x10)));
    }

    private const int ChartEntries = 19;
    private const uint ChartEntrySize = 12;

    private List<CheckpointMemory> ReadMemories()
    {
        var chart = WhichBuild().VoxelChart;
        var memories = new List<CheckpointMemory>();
        for (var slot = 0u; slot < ChartEntries; slot++)
        {
            var entry = chart + slot * ChartEntrySize;
            if (ReadByte(entry + 2) != 0)
                memories.Add(new CheckpointMemory((short)Half(entry), ReadByte(entry + 3), ReadByte(entry + 4), ReadByte(entry + 5),
                    ReadByte(entry + 6), ReadByte(entry + 7), ReadWord(entry + 8)));
        }
        return memories;
    }

    /// <summary>A big-endian unsigned halfword at any 2-aligned address, by its bytes.</summary>
    private int Half(uint address) => ReadByte(address) << 8 | ReadByte(address + 1);

    private float Float(uint address) => BitConverter.Int32BitsToSingle((int)ReadWord(address));

    /// <summary>The party's gold (<c>Party::Gold</c>).</summary>
    public uint Gold => ReadWord(FindParty() + 0x14);

    /// <summary>
    /// The party's inventory, by slot (<c>PartyInventory</c>): 252 slots of an <c>ItemInstance</c> and a
    /// quantity each, after the object's vtable. Slots from 128 are items with slots of their own.
    /// </summary>
    public IReadOnlyList<CheckpointStack> Inventory
    {
        get
        {
            var inventory = ReadWord(FindParty() + 0x10);
            var stacks = new List<CheckpointStack>();
            for (var slot = 0u; slot < InventorySlots; slot++)
            {
                var entry = inventory + 4 + slot * InventoryEntrySize;
                if (ReadWord(entry + 0x14) is var quantity and > 0 and < 0x80000000)
                    stacks.Add(new CheckpointStack((int)slot, Item(entry), (int)quantity));
            }
            return stacks;
        }
    }

    private const uint InventorySlots = 252, InventoryEntrySize = 0x18, GearSlots = 12;

    /// <summary>
    /// An <c>ItemInstance</c>: the ID, the price at <c>0xA</c>, then pointers to its <c>StatMod</c> (stat
    /// and amount, signed bytes) and its <c>SpellCharges</c> (the charges at 4).
    /// </summary>
    private CheckpointItem Item(uint instance)
    {
        var statMod = ReadWord(instance + 0xC);
        var charges = ReadWord(instance + 0x10);
        return new CheckpointItem(
            (ushort)(ReadByte(instance) << 8 | ReadByte(instance + 1)),
            ReadByte(instance + 0xA) << 8 | ReadByte(instance + 0xB),
            InRam(statMod) ? (ReadByte(statMod), (sbyte)ReadByte(statMod + 1)) : null,
            InRam(charges) ? ReadByte(charges + 4) : null);
    }

    private CheckpointItem? OptionalItem(uint pointer) => InRam(pointer) ? Item(pointer) : null;

    private uint FindParty() => PartyAt(WhichBuild().PartyPointer) ?? throw new InvalidDataException("Can't find the party in this state.");

    /// <summary>The party a <c>gGlobals.party</c> pointer leads to, if what it points to is one.</summary>
    private uint? PartyAt(uint pointer)
    {
        var party = ReadWord(pointer);
        if (!InRam(party))
            return null;
        var members = Enumerable.Range(0, 4).Select(i => ReadWord(party + (uint)(i * 4))).ToList();
        return members.All(m => m == 0 || InRam(m)) && members.Count(m => m != 0) == ReadByte(party + 0x24)
            && members.Where(m => m != 0).All(m => ReadByte(m) == 0x02)
            ? party
            : null;
    }

    private List<CheckpointCharacter> ReadParty()
    {
        var party = FindParty();
        var sheets = new List<CheckpointCharacter>();
        for (var slot = 0u; slot < 4; slot++)
        {
            var sheet = ReadWord(party + slot * 4);
            if (sheet == 0)
                continue;
            // CharSheet: the ID, then pointers to CharExp at 0x14, CharStats_s at 0x18, CharSkills at 0x1C
            // and the SpellBook at 0x2C, which points to its SpellInstances (the ID, and the level at 0x14).
            var exp = ReadWord(sheet + 0x14);
            var stats = ReadWord(sheet + 0x18);
            var skills = ReadWord(sheet + 0x1C);
            var book = ReadWord(sheet + 0x2C);
            var spells = new List<(ushort, int)>();
            if (InRam(book) && ReadWord(book) is var list && InRam(list))
            {
                for (var i = 0u; i < ReadByte(book + 4); i++)
                {
                    if (ReadWord(list + i * 4) is var spell && InRam(spell))
                        spells.Add(((ushort)(ReadByte(spell) << 8 | ReadByte(spell + 1)), ReadByte(spell + 0x14)));
                }
            }
            // The armor pointer at 0x20 points to body armor's and the shield's, the weapon's is at 0x24, and
            // the CharGear at 0x28 holds the gear slots' pointers and how many are worn.
            var armor = ReadWord(sheet + 0x20);
            var gear = ReadWord(sheet + 0x28);
            var slots = InRam(gear) ? ReadWord(gear) : 0;
            sheets.Add(new CheckpointCharacter(
                (ushort)(ReadByte(sheet) << 8 | ReadByte(sheet + 1)),
                Bytes(stats, 7), Signed(stats + 7, 7), Bytes(stats + 0xE, 7), Signed(stats + 0x15, 7),
                Signed(skills, 24), Signed(skills + 0x18, 24),
                ReadWord(exp + 4), ReadWord(exp + 8), ReadByte(exp + 1), ReadByte(exp + 2), ReadByte(exp + 3), ReadByte(exp + 0xC), spells)
            {
                Weapon = OptionalItem(ReadWord(sheet + 0x24)),
                Armor = InRam(armor) ? OptionalItem(ReadWord(armor)) : null,
                Shield = InRam(armor) ? OptionalItem(ReadWord(armor + 4)) : null,
                Gear = InRam(slots) ? [.. Enumerable.Range(0, (int)GearSlots).Select(i => OptionalItem(ReadWord(slots + (uint)i * 4)))] : [],
                GearCount = InRam(gear) ? ReadByte(gear + 4) : 0,
            });
        }
        return sheets;
    }

    private int[] Bytes(uint address, int count) => [.. Enumerable.Range(0, count).Select(i => (int)ReadByte(address + (uint)i))];

    private int[] Signed(uint address, int count) => [.. Enumerable.Range(0, count).Select(i => (int)(sbyte)ReadByte(address + (uint)i))];

    private static bool InRam(uint address) => address is >= 0x80000000 and < 0x80000000 + RdramSize;

    /// <summary>A byte of RAM, by KSEG0 address (0x80000000–0x807FFFFF).</summary>
    public byte ReadByte(uint address) => _state[_base + (Physical(address) ^ 3)];

    /// <summary>A big-endian word of RAM, by 4-byte-aligned KSEG0 address.</summary>
    public uint ReadWord(uint address)
    {
        if ((address & 3) != 0)
            throw new ArgumentException($"{address:x8} isn't word-aligned.", nameof(address));
        return Word(_state, _base + Physical(address));
    }

    /// <summary>A big-endian word at a word-aligned file offset, whose bytes the file holds reversed.</summary>
    private static uint Word(byte[] state, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(offset, 4));

    private static int Physical(uint address)
    {
        if (address is < 0x80000000 or >= 0x80000000 + RdramSize)
            throw new ArgumentOutOfRangeException(nameof(address), $"{address:x8} isn't in KSEG0's RDRAM.");
        return (int)(address & 0x7FFFFF);
    }
}

/// <summary>A party member's character sheet as the console holds it (<c>CharSheet</c>).</summary>
/// <param name="Trained">The stats' trained values (<c>CharStats_s.Base</c>), Intelligence to Level.</param>
/// <param name="Current">Trained less damage (<c>health</c>).</param>
/// <param name="Base">Trained plus spells and potions (<c>magicMod</c>).</param>
/// <param name="Modified">Everything (<c>Modded</c>).</param>
/// <param name="SkillBase">The 12 skills, 11 weapon skills and the shield (<c>CharSkills</c>); −1 can't be learnt.</param>
/// <param name="SkillModified">The same, modified.</param>
/// <param name="Flags">True Name 1, heavy 2, Solar 4 (<c>CharExp::flags</c>).</param>
/// <param name="Spells">The spellbook's spells' IDs and levels, in its order.</param>
public sealed record CheckpointCharacter(
    ushort Id,
    IReadOnlyList<int> Trained,
    IReadOnlyList<int> Current,
    IReadOnlyList<int> Base,
    IReadOnlyList<int> Modified,
    IReadOnlyList<int> SkillBase,
    IReadOnlyList<int> SkillModified,
    uint TotalExp,
    uint SpendableExp,
    int School,
    int Protection,
    int Damage,
    int Flags,
    IReadOnlyList<(ushort Spell, int Level)> Spells)
{
    public CheckpointItem? Weapon { get; init; }

    public CheckpointItem? Armor { get; init; }

    public CheckpointItem? Shield { get; init; }

    /// <summary>The 12 gear slots, empty ones null.</summary>
    public IReadOnlyList<CheckpointItem?> Gear { get; init; } = [];

    /// <summary>How many gear slots are used (<c>CharGear::usedItems</c>).</summary>
    public int GearCount { get; init; }
}

/// <summary>Where the player is (<c>mapFloatDat</c>).</summary>
/// <param name="MapSet">The map set's number (<c>mapDatA</c>, <c>EnumMapDatA</c>).</param>
/// <param name="Row">The chunk's row, from 1 for A (<c>mapShort1</c>).</param>
/// <param name="Column">The chunk's column (<c>mapShort2</c>).</param>
/// <param name="Position">The centre of the player's sphere, in the chunk's coordinates.</param>
/// <param name="Facing">The original's <c>facing</c>, in X and Z: the opposite of the way the player faces.</param>
public sealed record CheckpointPlace(int MapSet, int Row, int Column, Vector3 Position, Vector2 Facing);

/// <summary>A memory a map object holds (<c>VoxelChartEntry</c>).</summary>
/// <param name="Index">The object's index in its map (<c>arg0</c>).</param>
/// <param name="Lifetime">Map loads it lasts (<c>arg7</c>).</param>
/// <param name="MapSet">The map set the object is in (<c>MapDatA</c>).</param>
/// <param name="Row">The object's chunk's row (<c>MapShortA</c>), already moved from the centre chunk to the object's own.</param>
/// <param name="Column">Its column (<c>MapShortB</c>).</param>
/// <param name="Type">
/// What it is: the object's type for a container opened (1) or a monster party beaten (5), 9 for a
/// conversation's random line, and 0x49 + the skill for a conversation's skill check. A conversation's
/// are written as type 9 whether a dialogue object or a monster party that talks started it.
/// </param>
/// <param name="Value">The last four bytes as a word: a random line's roll is a float, 0–1; a skill check's step an integer.</param>
public sealed record CheckpointMemory(int Index, int Lifetime, int MapSet, int Row, int Column, int Type, uint Value);

/// <summary>An item as the console holds one (<c>ItemInstance</c>).</summary>
/// <param name="StatMod">Its stat and amount, if it has a <c>StatMod</c>: for a battery, the Stamina it has left.</param>
/// <param name="Charges">Its spell's charges, if it has a spell.</param>
public sealed record CheckpointItem(ushort Id, int Price, (int Stat, int Amount)? StatMod, int? Charges);

/// <summary>A stack in the party's inventory, by slot.</summary>
public sealed record CheckpointStack(int Slot, CheckpointItem Item, int Quantity);
