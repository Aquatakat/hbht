using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// One of the formula vectors <c>tools/aivectors/formulas.py</c> writes (combat parity, roadmap 2.9): 1.0's own function run in the
/// interpreter on a state's RAM (<paramref name="Source"/>) with changes made (<paramref name="Pokes"/>), the main stream at
/// <paramref name="Seed"/> and some draws fixed (<paramref name="Script"/>), and what came of it (<paramref name="Expect"/>,
/// "name=value ..."). <paramref name="Args"/> are the family's (slots and the like); <paramref name="Draws"/> names the function of
/// each of the original's draws, for the reader.
/// </summary>
public sealed record FormulaVector(string Id, string Source, string Pokes, int[] Args, uint Seed, string Script, string Expect, string Draws)
{
    public override string ToString() => Id;
}

/// <summary>
/// A formula vector's state in the port, as <c>tools/aivectors/state.py</c> makes it in the interpreter: the battle a source's RAM is
/// laid over (<see cref="Build"/>), the pokes (<see cref="Poke"/>, the same text), and the combat stream's draws (<see cref="Draws"/>).
/// </summary>
internal static class FormulaState
{
    /// <summary>
    /// The battle with a source's RAM laid over it: <c>cpNN</c>, a Combat checkpoint over checkpoint 01's battle
    /// (<see cref="CombatCheckpoints.At"/>); <c>log:&lt;session&gt;/&lt;battle&gt;:&lt;dump&gt;</c>, a parity log's dump over the battle its
    /// setup begins (<see cref="CombatCheckpoints.FromLog"/>), skipped without the logs.
    /// </summary>
    public static Built Build(PluginSet set, string source)
    {
        if (source.StartsWith("cp", StringComparison.Ordinal))
            return At(set, int.Parse(source[2..], CultureInfo.InvariantCulture));
        if (!source.StartsWith("log:", StringComparison.Ordinal))
            throw new ArgumentException($"A source {source}.");
        var (folder, dump) = (source[4..source.LastIndexOf(':')], source[(source.LastIndexOf(':') + 1)..]);
        var path = Path.Combine(LogFolder, folder);
        if (!File.Exists(Path.Combine(path, dump)))
            Assert.Skip($"No {dump} in {path}.");
        return FromLog(set, ParityLog.Read(path), dump);
    }

    public static string LogFolder => Environment.GetEnvironmentVariable("HBHT_PARITY") ?? Path.Combine(TestRoms.RepoRoot(), "out", "parity");

    /// <summary>A family's own pokes, by name, which <see cref="Poke"/> tries before its own.</summary>
    public delegate void PokeHandler(Built built, string[] args);

    /// <summary>
    /// Makes a vector's changes to the port's battle, as <c>state.py</c>'s pokes make them to the RAM: "name:arg,...", joined by "|".
    /// </summary>
    public static void Poke(Built built, string pokes, IReadOnlyDictionary<string, PokeHandler>? extra = null)
    {
        foreach (var item in pokes.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = item.IndexOf(':');
            var (name, args) = colon < 0 ? (item, Array.Empty<string>()) : (item[..colon], item[(colon + 1)..].Split(','));
            if (extra?.TryGetValue(name, out var handler) == true)
                handler(built, args);
            else if (Pokes.TryGetValue(name, out var own))
                own(built, args);
            else
                throw new InvalidDataException($"No poke {name}.");
        }
    }

    private static readonly Dictionary<string, PokeHandler> Pokes = new()
    {
        ["mod"] = (b, a) => SetPart(Slot(b, a[0]).Sheet.Stats, StatOf(a[1]), modified: Int(a[2])),
        ["base"] = (b, a) => SetPart(Slot(b, a[0]).Sheet.Stats, StatOf(a[1]), @base: Int(a[2])),
        ["cur"] = (b, a) => SetPart(Slot(b, a[0]).Sheet.Stats, StatOf(a[1]), current: Int(a[2])),
        ["skill"] = (b, a) => SetSkill(Slot(b, a[0]).Sheet.Skills, SkillOf(a[1]), Int(a[2])),
        ["place"] = (b, a) => Place(b.Battle, Slot(b, a[0]), Float(a[1]), Float(a[2])),
        ["face"] = (b, a) => Face(Slot(b, a[0]), Int(a[1])),
        ["height"] = (b, a) => SetHeight(b.Battle.Grid, Int(a[0]), Int(a[1]), Int(a[2])),
        ["daypart"] = (b, a) => b.Game.Weather!.PartOfDay = (PartOfDay)Int(a[0]),
        ["troubadour"] = (b, a) => CombatCheckpoints.Poke(b.Battle, nameof(Battle.TroubadourBonus), Int(a[0])),
        // Put in hand whoever may wield it, as the RAM's pointer is (the port's Equipment.EquipWeapon refuses another's item).
        ["weapon"] = (b, a) => KeepingStats(Slot(b, a[0]).Sheet, s =>
        {
            s.Equipment.RemoveWeapon();
            var weapon = new ItemInstance(b.Game.Items.TryGet(TestRoms.ItemId((ushort)Int(a[1])))!);
            if (s.Equipment.EquipWeapon(weapon) != EquipRefusal.None)
                typeof(Equipment).GetProperty(nameof(Equipment.Weapon))!.SetValue(s.Equipment, weapon);
        }),
        ["noshield"] = (b, a) => KeepingStats(Slot(b, a[0]).Sheet, s => s.Equipment.RemoveShield()),
        ["resist"] = (b, a) =>
        {
            var sheet = Slot(b, a[0]).Sheet;
            var list = sheet.Record.CharacterSheet.Resistances.ToList();
            var i = Int(a[1]);
            var none = RecordIds.Element(0);
            while (list.Count <= i)
                list.Add(new Resistance(none, 1));
            list[i] = new Resistance(RecordIds.Element(Int(a[2])), Float(a[3]));
            SetRecord(sheet, sheet.Record with { Sheet = sheet.Record.CharacterSheet with { Resistances = [.. list.Where(r => r.Element != none)] } });
        },
        ["aspect"] = (b, a) => SetRecord(Slot(b, a[0]).Sheet, Slot(b, a[0]).Sheet.Record with { Sheet = Slot(b, a[0]).Sheet.Record.CharacterSheet with { Aspect = (Aspect)Int(a[1]) } }),
        ["acted"] = (b, a) =>
        {
            var c = Slot(b, a[0]);
            c.Flags |= CombatantFlags.Acted;
            c.CastMargin = (sbyte)Int(a[1]);
        },
        ["effect"] = (b, a) =>
        {
            var on = Slot(b, a[0]);
            var spell = b.Game.Plugins.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Position == Int(a[1]));
            on.Sheet.AddEffect(spell.Id, spell.Effects ?? [], Int(a[2]), spell.School, Int(a[4]), (uint)Int(a[3]), on, b.Battle, null);
        },
    };

    public static Combatant Slot(Built built, string slot) => built.Battle.Slots[Int(slot)]!;

    public static int Int(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : int.Parse(s, CultureInfo.InvariantCulture);

    public static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static Stat StatOf(string name) => Enum.Parse<Stat>(name);

    /// <summary>A skill by the port's name: a <see cref="Skill"/>, a <see cref="WeaponClass"/>, or Shield.</summary>
    public static SkillId SkillOf(string name) =>
        Enum.TryParse<Skill>(name, out var skill) ? skill : Enum.TryParse<HBHT.Rom.Db.WeaponClass>(name, out var weapon) ? SkillId.WeaponSkill((int)weapon)
        : name == "Shield" ? SkillId.Shield : throw new InvalidDataException($"No skill {name}.");

    private static void SetPart(CharacterStats stats, Stat stat, int? current = null, int? @base = null, int? modified = null) =>
        SetStat(stats, stat, stats.Trained(stat), current ?? stats.Current(stat), @base ?? stats.RawBase(stat), modified ?? stats.RawModified(stat));

    /// <summary>A skill's base and modified levels as raw bytes, as the RAM's are poked (training caps a base).</summary>
    private static void SetSkill(CharacterSkills skills, SkillId skill, int level)
    {
        foreach (var field in new[] { "_base", "_modified" })
            ((sbyte[])typeof(CharacterSkills).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(skills)!)[skill.Slot] = (sbyte)level;
    }

    /// <summary>A combatant and its body to a place, its turn's start with it, the grid following; its radius as it was.</summary>
    private static void Place(Battle battle, Combatant c, float x, float z)
    {
        battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Cell = c.TurnStart = new Vector2(x, z);
        c.Body.Sphere.Position = c.Body.Sphere.Position with { X = battle.Origin.X + x, Z = battle.Origin.Z + z };
        c.ReadFrom = c.Body.Sphere.Position;
        battle.Grid.Occupy(ArenaGrid.CellOf(x), ArenaGrid.CellOf(z), c.Footprint);
    }

    private static readonly int[] DX = [0, 1, 1, 1, 0, -1, -1, -1], DZ = [-1, -1, 0, 1, 1, 1, 0, -1];

    /// <summary>A combatant's direction byte and facing, its body's heading with it, to one of the eight directions.</summary>
    private static void Face(Combatant c, int direction)
    {
        c.Direction = direction;
        c.Facing = c.Body.Heading = Vector2.Normalize(new Vector2(DX[direction], DZ[direction]));
    }

    private static void SetHeight(ArenaGrid grid, int x, int z, int height)
    {
        var cells = (byte[])typeof(ArenaGrid).GetField("_cells", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(grid)!;
        var i = x * ArenaGrid.Side + z;
        cells[i] = (byte)(cells[i] & ArenaGrid.Taken | height & 0x7F);
    }

    /// <summary>Replaces a character's record, and its sheet, which the character keeps from creating it.</summary>
    private static void SetRecord(Character sheet, EntityRecord record)
    {
        typeof(Character).GetField("<Record>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sheet, record);
        typeof(Character).GetField("<Sheet>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sheet, record.CharacterSheet);
    }

    /// <summary>A change to the gear with the stats and skills put back after: the RAM's poke changes the pointer alone.</summary>
    private static void KeepingStats(Character sheet, Action<Character> change)
    {
        var stats = Enum.GetValues<Stat>().Select(s => (s, sheet.Stats.Trained(s), sheet.Stats.Current(s), sheet.Stats.RawBase(s), sheet.Stats.RawModified(s))).ToList();
        var skills = SkillId.All.Select(s => (s, sheet.Skills.RawBase(s), sheet.Skills.RawModified(s))).ToList();
        var protection = sheet.Protection;
        change(sheet);
        foreach (var (s, trained, current, @base, modified) in stats)
            SetStat(sheet.Stats, s, trained, current, @base, modified);
        foreach (var field in new[] { "_base", "_modified" })
        {
            var bytes = (sbyte[])typeof(CharacterSkills).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sheet.Skills)!;
            foreach (var (s, b, m) in skills)
                bytes[s.Slot] = (sbyte)(field == "_base" ? b : m);
        }
        sheet.Protection = protection;
    }

    /// <summary>
    /// The combat stream's draws as the interpreter's (<c>draws.py</c>): the seed set, each scripted draw coming up its raw value (the
    /// stream set to the seed before it), and every draw kept with the port's method that made it.
    /// </summary>
    public sealed class Draws
    {
        private readonly Dictionary<int, uint> _script;

        public Draws(Lcg stream, uint seed, string script)
        {
            _script = script.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('='))
                .ToDictionary(p => int.Parse(p[0], CultureInfo.InvariantCulture), p => Convert.ToUInt32(p[1], 16));
            stream.Seed = seed;
            stream.BeforeDraw = Before;
        }

        /// <summary>The port's draws, each by the first of its methods outside the generator.</summary>
        public List<string> Drawn { get; } = [];

        public int Count => Drawn.Count;

        private void Before(Lcg stream)
        {
            if (_script.TryGetValue(Drawn.Count, out var value))
                stream.Seed = SeedBefore(value);
            var frame = new StackTrace(2, false).GetFrames().Select(f => f.GetMethod()).OfType<MethodBase>()
                .FirstOrDefault(m => m.DeclaringType is { } t && t != typeof(Lcg) && t != typeof(RandomExtensions) && t != typeof(Draws));
            Drawn.Add(frame is null ? "?" : $"{frame.DeclaringType!.Name}.{frame.Name}");
        }

        private const uint A = 0x19660D, C = 0x3C6EF35F;

        /// <summary>The seed whose next draw is <paramref name="value"/>: the inverse of A modulo 2³², by Newton's iteration.</summary>
        public static uint SeedBefore(uint value)
        {
            unchecked
            {
                var inverse = A;
                for (var i = 0; i < 5; i++)
                    inverse *= 2 - A * inverse;
                return (value - C) * inverse;
            }
        }
    }

    /// <summary>
    /// What the port came to against what the vector expects, name by name: an empty list if they agree, else each name that differs
    /// (or is missing on either side).
    /// </summary>
    public static List<string> Differences(string expect, IEnumerable<(string Name, object Value)> actual)
    {
        var want = expect.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var got = actual.ToDictionary(p => p.Name, p => Convert.ToString(p.Value, CultureInfo.InvariantCulture)!);
        return [.. want.Keys.Union(got.Keys).Where(k => want.GetValueOrDefault(k) != got.GetValueOrDefault(k))
            .Select(k => $"{k}: original {want.GetValueOrDefault(k) ?? "(none)"}, port {got.GetValueOrDefault(k) ?? "(none)"}")];
    }
}
