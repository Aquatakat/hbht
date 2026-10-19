using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

using static Aidyn.Rom.Tests.CombatCheckpoints;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Spells, potions and skills in a turn (M3 session 9) against the original's own results: checkpoint 07 (Rheda aiming her Endurance at
/// the party, state 10) laid over the battle the port sets up from checkpoint 01, and the held menus' rows, target set B and the cast
/// compared with what 1.0's own functions gave when they were run on 07's RAM (<c>CombatMenu.BuildSpellsMenu</c>,
/// <c>BuildItemsMenu</c>, <c>TargetSet.BeginSpell</c>, <c>Combat.ConfirmSpellTarget</c> and <c>Combatant.ExecuteAction</c>; the
/// session's research), and with checkpoint 08, just after the cast. The tests skip without the ROM and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SpellCheckpointTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static readonly RecordId Endurance = RecordId.Hex(RecordTypes.Spell, 0x0310);

    private Built At07()
    {
        var built = At(Set, 7);
        built.Game.Random.Combat.Seed = built.Cp.ReadWord(RandomState);
        return built;
    }

    private static SpellChoice Known(Combatant c, string name) =>
        new(SpellSourceKind.Known, c.Sheet.Spells.Select((s, i) => (s, i)).Single(p => p.s.Spell.Name == name).i);

    [Fact]
    public void CDownListsTheSkillsThenTheSpellsByNameAsTheOriginalsMenu()
    {
        var built = At07();
        var battle = built.Battle;
        string[] Rows(int slot) => [.. battle.SpellRows(built[slot]).Select(r => r.Text)];

        // CombatMenu.BuildSpellsMenu run on 07: Perform, then the spells in strcmp's order, capitals before "vs.".
        Assert.Equal(["Perform", "Air Shield", "Spirit Shield", "Wind", "vs. Elemental", "vs. Naming", "vs. Necromancy", "vs. Star"], Rows(0));
        Assert.Equal(["Endurance", "Sense Aura", "Weakness", "vs. Necromancy"], Rows(1));
        Assert.Empty(Rows(2));
        Assert.Equal(["Lay On Hands", "Heal With Herbs", "Dispel Naming", "Dispel Star", "Solar Wrath", "Stellar Gravity", "Web Of Starlight"], Rows(3));

        // The skills blue, the spells red with their schools' icons.
        var rheda = battle.SpellRows(built[1]);
        Assert.All(rheda, r => Assert.Equal(CombatMenu.SpellColor, r.Color));
        Assert.Equal(CombatMenu.SchoolIcon(MagicSchool.Naming), rheda[0].Icon);
        Assert.Null(rheda[3].Icon);
        Assert.Equal(CombatMenu.SkillColor, battle.SpellRows(built[0])[0].Color);
    }

    [Fact]
    public void CUpListsThePotionsThatWouldDoSomethingThenTheWornItemsSpells()
    {
        var built = At07();
        var battle = built.Battle;
        string[] Rows(int slot) => [.. battle.ItemRows(built[slot]).Select(r => r.Text)];

        // CombatMenu.BuildItemsMenu run on 07: Alaron, hurt and tired, has the Healing, Stamina, Curing and Strength potions and his
        // sword's Whitefire; Rheda and Brenna, at full health and Stamina, only Strength; Sholeh the four.
        var alaron = battle.ItemRows(built[0]);
        Assert.Equal([Potion.Healing, Potion.Stamina, Potion.Curing, Potion.Strength], alaron.Where(r => r.Potion is not null).Select(r => r.Potion!.Value));
        var swords = alaron.Single(r => r.Choice == CombatMenuChoice.ItemSpell);
        Assert.Equal(("Whitefire", SpellSourceKind.Weapon), (swords.Text, swords.Spell.Source));
        Assert.Same(swords, alaron[^1]);
        Assert.Single(Rows(1));
        Assert.Equal(battle.ItemRows(built[1])[0].Text, battle.ItemRows(built[2])[0].Text);
        Assert.Equal(Potion.Strength, battle.ItemRows(built[1])[0].Potion);
        Assert.Equal(4, Rows(3).Length);
    }

    [Fact]
    public void RhedasEnduranceTakesTheTwoNearestHerAsCheckpoint07sSetB()
    {
        var built = At07();
        var battle = built.Battle;
        var rheda = built[1];
        rheda.Spell = Known(rheda, "Endurance");

        // 07's set B (Combat+0x4F0C): the four party members as candidates, two may be chosen, slots 0 and 1 selected.
        var s = built.Struct + 0x4F0C;
        int[] Flags(uint at) => [.. Enumerable.Range(0, Battle.SlotCount).Where(i => built.Cp.ReadWord(at + (uint)i * 4) != 0)];
        Assert.Equal([0, 1, 2, 3], Flags(s));
        Assert.Equal([0, 1], Flags(s + 0x48));
        Assert.Equal((4, 2, 2), (built.Cp.ReadByte(s + 0xA3), built.Cp.ReadByte(s + 0xA4), built.Cp.ReadByte(s + 0xA5)));

        Assert.True(battle.TargetsB.BeginSpell(battle, rheda));
        Assert.Equal([0, 1, 2, 3], Enumerable.Range(0, Battle.SlotCount).Where(battle.TargetsB.IsCandidate));
        Assert.Equal([0, 1], battle.TargetsB.SelectedSlots);
        Assert.Equal(2, battle.TargetsB.Most);

        // Rebuild, as every turn's start does for the red tints, selects in slot order: the same two here.
        battle.TargetsB.Rebuild(battle, rheda);
        Assert.Equal([0, 1], battle.TargetsB.SelectedSlots);
    }

    [Theory]
    // TargetSet.BeginSpell run on 07 for each member's spells: the candidates, and those selected.
    [InlineData(0, "Wind", new[] { 0, 1, 2, 3, 4, 5 }, 1)]
    [InlineData(0, "vs. Elemental", new[] { 0, 1, 2, 3 }, 4)]
    [InlineData(1, "Sense Aura", new[] { 4, 5 }, 1)]
    [InlineData(1, "Weakness", new[] { 4, 5 }, 2)]
    [InlineData(3, "Solar Wrath", new[] { 4, 5 }, 2)]
    [InlineData(3, "Web Of Starlight", new int[0], 0)]
    public void EachSpellsCandidatesAreTheOriginals(int slot, string spell, int[] candidates, int selected)
    {
        var built = At07();
        var battle = built.Battle;
        var c = built[slot];
        c.Spell = Known(c, spell);
        battle.TargetsB.BeginSpell(battle, c);
        Assert.Equal(candidates, Enumerable.Range(0, Battle.SlotCount).Where(battle.TargetsB.IsCandidate));
        Assert.Equal(selected, battle.TargetsB.Selected);
    }

    [Fact]
    public void SenseAurasOneTargetIsTheFirstOfTheAngleOrder()
    {
        var built = At07();
        var battle = built.Battle;
        var rheda = built[1];
        rheda.Spell = Known(rheda, "Sense Aura");
        rheda.LastTarget = -1;
        battle.TargetsB.BeginSpell(battle, rheda);
        Assert.Equal([5, 4], battle.TargetsB.Order);
        Assert.Equal([5], battle.TargetsB.SelectedSlots);
    }

    [Fact]
    public void ConfirmingCastsAtRhedaThenAlaronWithOneRollAsCheckpoint08()
    {
        var built = At07();
        var battle = built.Battle;
        var (alaron, rheda) = (built[0], built[1]);
        var zone = Zone(built);
        rheda.Spell = Known(rheda, "Endurance");
        Poke(battle, nameof(Battle.State), CombatState.TargetSpell);
        battle.TargetsB.BeginSpell(battle, rheda);
        var seed = built.Game.Random.Combat.Seed;
        var spice = built.Game.Party.Inventory.Count(RecordId.Hex(RecordTypes.Item, 0x0111));

        // A (Combat.ConfirmSpellTarget): the highest selected first, the caster herself: state 6, then the cast.
        Assert.True(battle.Confirm());
        Assert.Equal((CombatState.Facing, 1, 0), (battle.State, battle.Target, rheda.CastMargin));
        battle.Tick(zone, false, _ => null);
        // 08: state 2, +0x14 = 1, the margin 4, the acted flag, the lines, two draws.
        var c08 = Combat(8);
        Assert.Equal(c08.ReadByte(0x800D9454), (byte)battle.State);
        Assert.Equal(CombatState.Action, battle.State);
        Assert.Equal(4, rheda.CastMargin);
        Assert.True(rheda.Flags.HasFlag(CombatantFlags.Acted));
        Assert.Equal(("Rheda casts Endurance", "Endurance cast on Rheda"), (battle.Lines.Shown, battle.Lines.Queued));
        var expected = new Lcg(seed);
        expected.Next();
        expected.Next();
        Assert.Equal(expected.Seed, built.Game.Random.Combat.Seed);
        Assert.Equal(Battle.CastRequest, rheda.Body.Request);
        Assert.Single(rheda.Sheet.Effects);
        Assert.Empty(alaron.Sheet.Effects);
        Assert.Equal(spice - 1, built.Game.Party.Inventory.Count(RecordId.Hex(RecordTypes.Item, 0x0111)));

        // Through states 2 and 4 to the next target, slot 0, cast with no draw, and then the turn's end.
        var states = new List<CombatState>();
        for (var i = 0; i < 1200 && battle.Current == rheda; i++)
        {
            battle.Tick(zone, false, _ => null);
            if (states.Count == 0 || states[^1] != battle.State)
                states.Add(battle.State);
            if (battle.State == CombatState.Action && battle.Target == 0 && alaron.Sheet.Effects.Count == 1)
                Assert.Equal("Endurance cast on Alaron", battle.Lines.Queued);
        }
        Assert.Equal([CombatState.Action, CombatState.Result, CombatState.Facing, CombatState.Action, CombatState.Result], states.Take(5));
        Assert.Single(alaron.Sheet.Effects);
        Assert.Equal(-1, rheda.CastMargin);
        Assert.NotSame(rheda, battle.Current);
    }
}
