using System.Globalization;
using System.Reflection;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Magic;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests.Formulas;

/// <summary>
/// Effects and potions against 1.0's own (combat parity's formula vectors, roadmap 2.9): <c>Character.AddEffect</c> (<c>0x800702F0</c>),
/// a round's ageing (<c>Character.AgeEffects</c>, <c>0x80071444</c>, with <c>EffectRules.Round</c> and <c>End</c>), <c>Battle.Dispel</c>,
/// <c>Entity.CurePoison</c>, <c>Character.TakeDamage</c>, <c>Combatant.DrinkPotion</c>, <c>Potions.CanDrink</c>,
/// <c>Combatant.ResolveHealer</c> and <c>Combatant.StartTroubadour</c>, run in the interpreter by
/// <c>tools/aivectors/formulas.py effects</c> (<see cref="EffectsVectors"/>), and the port's same on the same state with the same draws.
/// The tests skip without the checkpoints (and the parity logs, for the vectors on their states).
/// </summary>
public sealed class EffectsVectorTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// Vectors where the port differs from 1.0 on purpose: a fix of the original's the user chose, or a difference they keep. Each is
    /// logged rather than failed, and fails once it agrees, to come off the list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDifferences = new Dictionary<string, string>
    {
        // The Stamina spell (kind 0x20): 1.0's AddEffect jumps to the harmful stat kinds' path (0x80070560, −2L) and its End takes 2L more
        // (SubStat of +2L), so each cast lowers Stamina's base and maximum by 4L until a load. The port's raises it 2L and takes it back.
        ["cp05-add-32-on0"] = StaminaSpell,
        ["cp05-add-32-on4"] = StaminaSpell,
        ["cp05-age-end-32"] = StaminaSpell,
        ["cp05-age-end-32-alaron"] = StaminaSpell,
    }.Concat(new[] { "cp05-add-4-on0", "cp05-add-4-on4", "cp05-add-17-on0", "cp05-add-17-on4", "cp05-add-27-on0", "cp05-add-27-on4",
        "setup7-add-4-on4", "setup7-add-17-on4", "setup7-add-17-on5", "setup7-add-27-on4", "setup7-add-27-on5" }
        .Select(id => KeyValuePair.Create(id, UnallowedCharm))).ToDictionary();

    private const string StaminaSpell = "1.0's Stamina spell lowers Stamina 2L as it lands and 2L more as it ends; the port's raises it, the user's choice";

    /// <summary>
    /// A charm at one its kind doesn't allow (Control Elem at a non-elemental, Control Marquis and Control Zombies at another entity): 1.0's
    /// AddEffect adds a harmless record and toggles nothing; the port's charms whom it's given. Targeting rules such a target out first
    /// (<c>SpellKindAllowsTarget</c>, the port's <see cref="Casting.Allows"/>), which the test checks, so play never reaches it.
    /// </summary>
    private const string UnallowedCharm = "a charm at a target its kind's filter rules out, which targeting never offers";

    public static TheoryData<string> Ids => [.. EffectsVectors.Vectors.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheEffectsAreTheOriginals(string id)
    {
        var v = EffectsVectors.Vectors.Single(v => v.Id == id);
        var found = Run(v);
        output.WriteLine($"original: {v.Expect}\n  draws: {v.Draws}");
        if (KnownDifferences.TryGetValue(id, out var why))
        {
            if (why == UnallowedCharm)
            {
                var built = FormulaState.Build(Set, v.Source);
                Assert.False(Casting.Allows(Spell(built.Game, v.Args[2]).Filter, built[v.Args[1]].Sheet), $"{id}'s target is one targeting allows");
            }
            Assert.True(found.Count > 0, $"{id} is the original's now: take it off the known differences");
            output.WriteLine($"a known difference, {why}:\n  {string.Join("\n  ", found)}");
            return;
        }
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    // The operations, args[0], as effects.py numbers them.
    private const int Add = 0, Age = 1, Dispel = 2, Cure = 3, Damage = 4, Drink = 5, CanDrink = 6, Heal = 7, Perform = 8;

    /// <summary>The stream a family poke lays an effect on with (<c>effects.py</c>'s <c>POKE_SEED</c>), as Wraith Touch draws.</summary>
    private const uint PokeSeed = 0x5678;

    private static readonly string[] Skills = ["Alchemist", "Diplomat", "Healer", "Loremaster", "Mechanic", "Merchant", "Ranger", "Stealth",
        "Thief", "Troubadour", "Warrior", "Wizard", "Bite", "Breath", "Claw", "Hafted", "Missile", "Pole", "Spit", "Sting", "Sword", "Thrown",
        "Tusk", "Shield"];

    /// <summary>The vector's state and pokes laid over the port's battle, the port's function run, and what differs.</summary>
    private List<string> Run(FormulaVector v)
    {
        var built = FormulaState.Build(Set, v.Source);
        FormulaState.Poke(built, v.Pokes, Pokes(Set));
        var battle = built.Battle;
        var game = built.Game;
        var a = v.Args;
        var on = built[a[1]];
        var slots = new List<Combatant> { on };

        var draws = new FormulaState.Draws(game.Random.Combat, v.Seed, v.Script);
        var ret = 0;
        switch (a[0])
        {
            case Add:
            {
                var spell = Spell(game, a[2]);
                var record = on.Sheet.AddEffect(spell.Id, TargetEffects(spell), a[3], spell.School, a[5], Timer(spell, unchecked((uint)a[4])), on, battle,
                    game.Random.Combat);
                ret = record is null ? 0xFFFF : Array.IndexOf(SpellSlots(on.Sheet), record);
                break;
            }
            case Age:
                for (var i = 0; i < a[2]; i++)
                    battle.AgeEffects(on);
                break;
            case Dispel:
            {
                var target = new EffectTarget(on.Sheet, on, battle);
                var result = new LandingResult();
                var spell = Spell(game, a[2]);
                EffectLanding.Once(spell.Effects!.OfType<DispelEffect>().Single(), new(spell.Id, spell.School, 0, a[3], target, target, game.Random.Combat), result);
                ret = result.Dispelled;
                break;
            }
            case Cure:
            {
                var target = new EffectTarget(on.Sheet, on, battle);
                var spell = Spell(game, RemovePoison);
                EffectLanding.Once(spell.Effects!.OfType<CureEffect>().Single(), new(spell.Id, spell.School, 0, a[2], target, target, game.Random.Combat), new());
                break;
            }
            case Damage:
                on.Sheet.TakeDamage(a[2], on);
                break;
            case Drink:
            {
                // 1.0's DrinkPotion leaves the potion in the inventory (ThrowFlask's ConsumeFlask takes it); the port's takes it as it lands.
                var item = Potion(game, a[2]);
                game.Party.Inventory.Add(item.Id);
                typeof(Battle).GetMethod("DrinkPotion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(battle, [on, item]);
                break;
            }
            case CanDrink:
                ret = Potions.CanDrink(on.Sheet, Potion(game, a[2])) == DrinkResult.Drunk ? 1 : 0;
                break;
            case Heal:
            {
                var t = built[a[2]];
                on.Flags = on.Flags & ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs)
                    | (a[3] == 8 ? CombatantFlags.LayOnHands : CombatantFlags.HealWithHerbs);
                ret = battle.ResolveHealer(on, t);
                if (t != on)
                    slots.Add(t);
                break;
            }
            case Perform:
                battle.StartTroubadour(on);
                break;
            default:
                throw new InvalidDataException($"No operation {a[0]}.");
        }
        output.WriteLine($"port:     draws: {string.Join(' ', draws.Drawn)}");
        var actual = new List<(string, object)> { ("ret", ret) };
        foreach (var c in slots)
            actual.AddRange(Snapshot(game, c));
        actual.Add(("bonus", battle.TroubadourBonus));
        actual.Add(("healed", (int)slots[^1].Healed));
        actual.Add(("draws", draws.Count));
        if (a[0] == Heal)
            actual.Add(("herbs", game.Party.Inventory.Count(game.Party.Inventory.Catalog.Rules.Herb)));
        return FormulaState.Differences(v.Expect, actual);
    }

    /// <summary>Remove Poison's kind, whose cure <c>Entity.CurePoison</c> is.</summary>
    private const int RemovePoison = 2;

    /// <summary>
    /// The clock put in a part of the day, at an hour within it, the day kept (the <c>clock</c> poke's side of 1.0's <c>World+1</c>): the
    /// battle's part of the day, which Photosynthesis reads, doesn't follow it while the weather has its own.
    /// </summary>
    private static void SetPartOfDay(Core.GameState game, PartOfDay part) =>
        game.Clock.SetTimeOfDay(part switch
        {
            PartOfDay.Morning => 7,
            PartOfDay.Midday => 10,
            PartOfDay.Afternoon => 13,
            PartOfDay.Evening => 19,
            _ => 23,
        });

    private static Item Potion(Core.GameState game, int kind) => game.Items.TryGet(TestRoms.ItemId((ushort)(0x1000 + kind)))!;

    /// <summary>What a slot holds, as <c>effects.py</c>'s <c>snapshot</c> reads it from the RAM.</summary>
    private static IEnumerable<(string, object)> Snapshot(Core.GameState game, Combatant c)
    {
        var s = c.Slot;
        var sheet = c.Sheet;
        var stats = sheet.Stats;
        yield return ($"stats{s}", string.Join('/', Enum.GetValues<Stat>().Select(t => $"{(byte)stats.Current(t)}.{(byte)stats.RawBase(t)}.{(byte)stats.RawModified(t)}")));
        yield return ($"prot{s}", (int)sheet.Protection);
        yield return ($"skills{s}", string.Join('.', Skills.Select(k => sheet.Skills.RawModified(FormulaState.SkillOf(k)))));
        var positions = game.Plugins.All<SpellRecord>(RecordTypes.Spell).Where(r => r.Position is not null).ToDictionary(r => r.Id, r => r.Position!.Value);
        var fx = SpellSlots(sheet).Select((r, i) => r is null ? null
            : $"{i}:{positions[r.Source]}.{PowerByte(r)}.{int.Parse(r.School.Key)}.{(byte)r.Margin}.{(r.Harmful ? 1 : 0)}.{r.Timer}").OfType<string>();
        yield return ($"fx{s}", Join(fx));
        var potions = PotionSlots(sheet).Select((r, i) => r is null ? null : $"{i}:{Key(r.Source) - 0x1000}.{r.Power}.{r.Timer}").OfType<string>();
        yield return ($"potions{s}", Join(potions));
        yield return ($"flags{s}", (int)c.Flags);
        yield return ($"morale{s}", c.Morale is { } m ? m : "-");
        yield return ($"mirror{s}", (int)c.Mirror);
    }

    /// <summary>
    /// The record's power byte (<c>+0x01</c>) as 1.0 keeps it: the level, but for a morale spell what it took (no more than the morale
    /// had) and for Wraith Touch the amount drained, which the port keeps apart (<see cref="ActiveEffect.Amounts"/>) and gives back the same.
    /// </summary>
    private static int PowerByte(ActiveEffect record)
    {
        for (var i = 0; i < record.Effects.Count; i++)
        {
            if (record.Effects[i] is MoraleEffect)
                return Math.Abs(record.Amounts[i]);
            if (record.Effects[i] is DrainStatEffect)
                return record.Amounts[i];
        }
        return record.Power;
    }

    /// <summary>
    /// A record's timer as the port's landing gives it: the cast's, or for one that lasts the battle (Wraith Touch's) never running down,
    /// which 1.0's <c>Entity.WraithDrain</c> sets whatever it's given.
    /// </summary>
    private static uint Timer(SpellRecord spell, uint timer) => spell.Duration is { Battle: true } ? ActiveEffect.Never : timer;

    private static uint ParseTimer(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : unchecked((uint)long.Parse(s, CultureInfo.InvariantCulture));

    private static string Join(IEnumerable<string> parts) => parts.Any() ? string.Join(';', parts) : "-";

    private static int Key(RecordId id) => int.Parse(id.Key[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static ActiveEffect?[] SpellSlots(Character sheet) => Slots(sheet, "_spellEffects", sheet.Rules.Magic.Record.SpellEffects);

    private static ActiveEffect?[] PotionSlots(Character sheet) => Slots(sheet, "_potionEffects", sheet.Rules.Magic.Record.PotionEffects);

    private static ActiveEffect?[] Slots(Character sheet, string field, int count) =>
        (ActiveEffect?[]?)typeof(Character).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sheet) ?? new ActiveEffect?[count];

    private static SpellRecord Spell(Core.GameState game, int kind) =>
        game.Plugins.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Position == kind);

    /// <summary>
    /// The effects of a spell that <c>Character.AddEffect</c> lays on its target: the lasting ones, but those for an opponent alone (Wind's
    /// damage) and Tap Stamina's gain, which goes on the caster.
    /// </summary>
    private static List<Effect> TargetEffects(SpellRecord spell) =>
        [.. (spell.Effects ?? []).Where(e => EffectRules.Lasting(e) && e.For != EffectFor.Opponents && e is not StaminaEffect { ToCaster: true })];

    /// <summary>The family's own pokes, as <c>effects.py</c>'s <c>EXTRA</c>.</summary>
    private static Dictionary<string, FormulaState.PokeHandler> Pokes(PluginSet set) => new()
    {
        ["fx"] = (b, a) =>
        {
            var on = FormulaState.Slot(b, a[0]);
            var spell = Spell(b.Game, FormulaState.Int(a[1]));
            b.Game.Random.Combat.Seed = PokeSeed;
            on.Sheet.AddEffect(spell.Id, TargetEffects(spell), FormulaState.Int(a[2]), spell.School, FormulaState.Int(a[4]),
                Timer(spell, ParseTimer(a[3])), on, b.Battle, b.Game.Random.Combat);
        },
        ["potion"] = (b, a) => FormulaState.Slot(b, a[0]).Sheet.ApplyPotionEffect(TestRoms.ItemId((ushort)(0x1000 + FormulaState.Int(a[1]))),
            FormulaState.Int(a[2]), ParseTimer(a[3])),
        ["flags"] = (b, a) =>
        {
            var c = FormulaState.Slot(b, a[0]);
            c.Flags = (c.Flags | (CombatantFlags)FormulaState.Int(a[1])) & ~(CombatantFlags)FormulaState.Int(a[2]);
        },
        ["morale"] = (b, a) => FormulaState.Slot(b, a[0]).Morale = FormulaState.Int(a[1]),
        ["herbs"] = (b, a) =>
        {
            var inventory = b.Game.Party.Inventory;
            var herb = b.Game.Party.Inventory.Catalog.Rules.Herb;
            inventory.Take(herb, inventory.Count(herb));
            if (FormulaState.Int(a[0]) > 0)
                inventory.Add(herb, FormulaState.Int(a[0]));
        },
        ["clock"] = (b, a) => SetPartOfDay(b.Game, (PartOfDay)FormulaState.Int(a[0])),
    };
}
