namespace HBHT.Rom.Engine;

/// <summary>A Borg a spell's or potion's visual shows, with the heap its actor asks for (0 for a scene, whose size the original never uses).</summary>
/// <param name="Borg">The Borg7 actor (roles 0 to 2) or Borg5 scene (roles 5 and 6).</param>
/// <param name="Memory">The memory the actor needs, in bytes: <c>CombatSpellFx.AddVisual</c> skips it when the largest free block is smaller.</param>
public readonly record struct VisualBorg(int Borg, int Memory);

/// <summary>
/// What each spell and potion shows in a battle (<c>combatspellvisuals.cpp</c>, <c>CombatSpellFx.AddVisual</c>, <c>0x8008B81C</c> in 1.0).
/// The code holds it as five <c>switch</c> functions on the kind with jump tables in the code segment, one for each role (the kind
/// is a spell's place in <c>SpellList</c>, or a potion's index), and two immediates:
/// the caster's actor (role 0, <c>0x8008CB40</c>), the target's (role 1, <c>0x8008CCE0</c>) and whether it stands at the head
/// (<c>0x8008CCA8</c>), the target's aura scene (role 5, <c>0x8008CE78</c>), a potion's actor (role 2, <c>0x8008D038</c>) and aura
/// scene (role 6, <c>0x8008D0FC</c>), and the actors a refused cast (role 3, <c>addiu s3, zero, 0x1CAA</c> at <c>0x8008B8D8</c>) and a
/// resisted one (role 4, <c>0x37F1</c> at <c>0x8008B8E0</c>) play. The decoded tables are the same in all three ROMs.
/// </summary>
/// <remarks>
/// Each function is a bound check (<c>sltiu</c>, <c>beqz</c>, with an <c>addiu v1, a0, -base</c> before it when the first kind isn't
/// 0), a <c>lui</c>/<c>addiu</c> of the jump table, and a <c>jr</c> through it. Every case block is straight-line
/// <c>addiu</c>/<c>ori</c>/<c>lui</c> on <c>v0</c> (the Borg, or the head flag) and <c>a2</c> (the size), jumping to a shared tail that
/// returns, so the reader runs the blocks on a few registers rather than the machine: any other instruction, a block that leaves the
/// function or doesn't end is an error. A kind with no case returns -1. Each function's range is <see cref="CodeTable"/>'s
/// <c>SpellVisual…</c>; the tables are in <see cref="CodeTable.SpellVisualJumpTables"/>.
/// </remarks>
/// <param name="Caster">Role 0 by kind: the actor at the caster.</param>
/// <param name="Target">Role 1 by kind: the actor at the target.</param>
/// <param name="TargetAtHead">The kinds whose role 1 actor stands at the height of the target's entity.</param>
/// <param name="Aura">Role 5 by kind: the scene at the target's locator 3 while the effect lasts (<see cref="VisualBorg.Memory"/> is unused).</param>
/// <param name="Potion">Role 2 by potion: the actor at whoever drinks it (or at each one a flask hurts).</param>
/// <param name="PotionAura">Role 6 by potion: its lasting scene (<see cref="VisualBorg.Memory"/> is unused).</param>
/// <param name="Refused">The Borg7 a refused cast plays at its caster.</param>
/// <param name="Resisted">The Borg7 a resisted cast plays at its target.</param>
public sealed record SpellVisualTables(
    IReadOnlyDictionary<int, VisualBorg> Caster,
    IReadOnlyDictionary<int, VisualBorg> Target,
    IReadOnlySet<int> TargetAtHead,
    IReadOnlyDictionary<int, VisualBorg> Aura,
    IReadOnlyDictionary<int, VisualBorg> Potion,
    IReadOnlyDictionary<int, VisualBorg> PotionAura,
    int Refused,
    int Resisted)
{
    /// <summary>How many kinds the spell functions' tables cover (<c>SpellList</c>'s length), and the potion functions'.</summary>
    public const int SpellKinds = 59, PotionKinds = 17;

    // What the tables hold, as the debug build's, 1.0's and 1.1's agree. Reading checks the counts, which catches a table read from the
    // wrong place.
    private const int CasterCount = 26, TargetCount = 30, AuraCount = 34, PotionCount = 12, PotionAuraCount = 4;

    public static SpellVisualTables Read(RomImage rom)
    {
        var caster = Switch(rom, CodeTable.SpellVisualCasterActor, SpellKinds, CasterCount, "caster actor");
        var target = Switch(rom, CodeTable.SpellVisualTargetActor, SpellKinds, TargetCount, "target actor");
        var aura = Switch(rom, CodeTable.SpellVisualAura, SpellKinds, AuraCount, "aura");
        var potion = Switch(rom, CodeTable.SpellVisualPotionActor, PotionKinds, PotionCount, "potion actor");
        var potionAura = Switch(rom, CodeTable.SpellVisualPotionAura, PotionKinds, PotionAuraCount, "potion aura");

        // The head flag's function returns 0 or 1 in v0 and leaves the size unset.
        var head = Switch(rom, CodeTable.SpellVisualTargetHead, SpellKinds, entries: null, "head flag", flag: true);
        if (head.Any(h => h.Value.Borg is not (0 or 1)))
            throw new InvalidDataException("The head flag function returns something besides 0 and 1.");
        var atHead = head.Where(h => h.Value.Borg == 1).Select(h => h.Key).ToHashSet();
        if (atHead.Any(k => !target.ContainsKey(k)))
            throw new InvalidDataException("A kind stands at the head, but has no target actor.");

        var immediates = rom.Table(CodeTable.SpellVisualRefusedResisted);
        return new SpellVisualTables(caster, target, atHead, aura, potion, potionAura,
            Immediate(immediates, 0, "refused"), Immediate(immediates, 8, "resisted"));
    }

    /// <summary>The immediate of <c>addiu s3, zero, imm</c> at <paramref name="at"/> (<c>AddVisual</c> loads it into the Borg's register).</summary>
    private static int Immediate(ReadOnlySpan<byte> code, int at, string what)
    {
        var word = Be.U32(code, at);
        if (word >> 16 != AddiuS3ZeroHigh)
            throw new InvalidDataException($"The {what} cast's actor isn't loaded with addiu s3, zero (0x{word:X8}).");
        return (int)(word & 0xFFFF);
    }

    private const uint AddiuS3ZeroHigh = 0x2413; // addiu (9) rs 0, rt 19

    private const int MaxBlockLength = 40;

    /// <summary>
    /// Decodes one function: every kind it has a case for and the Borg and size that case returns, or for a flag function the
    /// value (as the Borg). A kind whose case is the function's default has none.
    /// </summary>
    private static Dictionary<int, VisualBorg> Switch(
        RomImage rom, CodeTable table, int kinds, int? entries, string what, bool flag = false)
    {
        var range = Locate(rom, table);
        var tables = Locate(rom, CodeTable.SpellVisualJumpTables);
        var end = range.Offset + range.Length;

        // The function's instructions are addressed by ROM offset here, and its jump table's entries are RAM addresses.
        uint Word(int offset) => offset >= range.Offset && offset + 4 <= end
            ? Be.U32(rom.Slice(offset, 4), 0)
            : throw new InvalidDataException($"The {what} function reads 0x{offset:X}, outside its range 0x{range.Offset:X} to 0x{end:X}.");

        var at = range.Offset;
        var first = 0;
        var ins = Word(at);
        // addiu v1, a0, -first: kinds below the first have no case.
        if (ins >> 26 == 9 && Rt(ins) == 3 && Rs(ins) == 4)
        {
            first = -Imm(ins);
            at += 4;
            ins = Word(at);
        }
        if (first < 0)
            throw new InvalidDataException($"The {what} function's first kind is {first}.");

        // sltiu v0, (a0 or v1), count; beqz v0, default.
        if (ins >> 26 != 0xB)
            throw new InvalidDataException($"The {what} function doesn't start with a bound check (0x{ins:X8}).");
        var count = Imm(ins);
        if (count <= 0 || first + count > kinds)
            throw new InvalidDataException($"The {what} function covers kinds {first} to {first + count - 1}, past the {kinds}.");
        at += 4;
        ins = Word(at);
        if (ins >> 26 != 4)
            throw new InvalidDataException($"The {what} function's bound check isn't followed by a branch (0x{ins:X8}).");
        var defaultAt = at + 4 + (Imm(ins) << 2);
        at += 4;

        // lui/addiu of the jump table, within a few instructions.
        var luiAt = -1;
        for (var i = 0; i < 4 && luiAt < 0; i++)
            if (Word(at + i * 4) >> 26 == 0xF)
                luiAt = at + i * 4;
        if (luiAt < 0 || Word(luiAt + 4) >> 26 != 9)
            throw new InvalidDataException($"The {what} function doesn't load its jump table.");
        var tableAddress = unchecked((uint)(((int)(Word(luiAt) & 0xFFFF) << 16) + Imm(Word(luiAt + 4))));
        var tableAt = CodeSegment.RomOffset(tableAddress);
        if (tableAt < tables.Offset || tableAt + count * 4 > tables.Offset + tables.Length)
            throw new InvalidDataException($"The {what} function's jump table (0x{tableAddress:X8}) isn't in the jump tables' range.");

        var result = new Dictionary<int, VisualBorg>();
        var defaultValue = Run(defaultAt);
        for (var k = 0; k < count; k++)
        {
            var target = CodeSegment.RomOffset(Be.U32(rom.Slice(tableAt + k * 4, 4), 0));
            if (target == defaultAt)
                continue;
            var value = Run(target);
            if (value.Borg == -1 && !flag)
                continue;
            result.Add(first + k, value);
        }
        if (entries is { } expected && result.Count != expected)
            throw new InvalidDataException($"The {what} function has {result.Count} cases, not {expected}.");
        // A flag function's default (every kind it has no case for) is 0.
        if (flag && defaultValue.Borg != 0)
            throw new InvalidDataException($"The {what} function's default returns {defaultValue.Borg}, not 0.");
        return result;

        // Straight-line from a case block to the return, the delay slots run: (v0, a2).
        VisualBorg Run(int pc)
        {
            var regs = new uint[32];
            for (var step = 0; step < MaxBlockLength; step++)
            {
                var word = Word(pc);
                if (word >> 26 == 2)
                {
                    // j: the target's top bits are the delay slot's address's.
                    Execute(regs, Word(pc + 4));
                    var delaySlot = unchecked((uint)(pc + 4 - CodeSegment.RomBase + CodeSegment.RamBase));
                    pc = CodeSegment.RomOffset((delaySlot & 0xF0000000) | ((word & 0x3FFFFFF) << 2));
                    continue;
                }
                if (word == JrRa)
                {
                    Execute(regs, Word(pc + 4));
                    return new VisualBorg(unchecked((int)regs[2]), (int)regs[6]);
                }
                Execute(regs, word);
                pc += 4;
            }
            throw new InvalidDataException($"A case of the {what} function doesn't end.");
        }
    }

    private const uint JrRa = 0x03E00008;

    private static RomRange Locate(RomImage rom, CodeTable table) =>
        rom.Layout.CodeTables.TryGetValue(table, out var range)
            ? range
            : throw new NotSupportedException($"{rom.Version.Name} has no verified location for the {table} table.");

    private static int Rs(uint ins) => (int)(ins >> 21) & 31;

    private static int Rt(uint ins) => (int)(ins >> 16) & 31;

    private static int Imm(uint ins) => (short)(ins & 0xFFFF);

    /// <summary>One instruction of a case block: <c>addiu</c>, <c>ori</c>, <c>lui</c>, <c>addu</c> (<c>move</c>), <c>nop</c>, or the shared tail's <c>sw</c>.</summary>
    private static void Execute(uint[] regs, uint ins)
    {
        if (ins == 0)
            return;
        var op = ins >> 26;
        var rs = Rs(ins);
        var rt = Rt(ins);
        switch (op)
        {
            case 9: Set(regs, rt, regs[rs] + (uint)Imm(ins)); break;
            case 0xD: Set(regs, rt, regs[rs] | (ins & 0xFFFF)); break;
            case 0xF: Set(regs, rt, (ins & 0xFFFF) << 16); break;
            case 0 when (ins & 0x3F) == 0x21: Set(regs, (int)(ins >> 11) & 31, regs[rs] + regs[rt]); break;
            case 0x2B: break;
            default: throw new InvalidDataException($"A spell visual case has the instruction 0x{ins:X8}.");
        }
    }

    private static void Set(uint[] regs, int register, uint value)
    {
        if (register != 0)
            regs[register] = value;
    }
}
