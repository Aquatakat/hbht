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
        battle.Tick(zone, false, _ => null, fx: new Playing());
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
        // CombatSpellFx.Start (session 10): 08's pool A[0] is Rheda's cast at herself, whose handle her record keeps (+0x14 = 0); B[0] the
        // caster's actor 0x1C8E at her feet, B[1] the lasting scene 0x1C76, waiting its turn; her body glows and the sky dims.
        var cast = Assert.Single(battle.Fx.Casts);
        Assert.Equal(0, cast.Handle);
        Assert.Same(cast, rheda.Sheet.Effects[0].Visual);
        Assert.Equal([(SpellFxRole.Caster, RecordId.Hex(RecordTypes.Actor, 0x1C8E)), (SpellFxRole.Aura, RecordId.Hex(RecordTypes.Scene, 0x1C76))],
            battle.Fx.Visuals.Select(v => (v.Role, v.Id)));
        Assert.Equal(rheda.Body.Sphere.Position - new System.Numerics.Vector3(0, rheda.Body.Sphere.Radius, 0), battle.Fx.Visuals.First().Position);
        Assert.Equal(1 / 30f, rheda.Tint.Rate, 5);
        Assert.Equal(0.5f, battle.Fx.Sky.Goal);

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
        // 09's records: Alaron's keeps handle 1, Rheda's 0.
        Assert.Equal(1, Assert.IsType<SpellFxCast>(alaron.Sheet.Effects[0].Visual).Handle);
        Assert.Equal(0, Assert.IsType<SpellFxCast>(rheda.Sheet.Effects[0].Visual).Handle);
        Assert.Equal(-1, rheda.CastMargin);
        Assert.NotSame(rheda, battle.Current);
    }

    // 08's bodies all have tint level 0 and rate 0 (+0x75C, +0x760), 165 ticks after the cast: the glow on Rheda (SetTint over 30) is
    // undone on the frame it starts, as the target marks let her and Alaron go (ClearTint over 0) after the state's step. Alaron's own
    // glow comes with his cast, his mark long gone.
    [Fact]
    public void TheMarksLettingGoUndoTheCastersGlowAsCheckpoint08sTintsShow()
    {
        var built = At07();
        var battle = built.Battle;
        var (alaron, rheda) = (built[0], built[1]);
        var zone = Zone(built);
        rheda.Spell = Known(rheda, "Endurance");
        Poke(battle, nameof(Battle.State), CombatState.TargetSpell);
        battle.TargetsB.BeginSpell(battle, rheda);
        battle.Tick(zone, false, _ => null);
        Assert.NotNull(rheda.Mark);
        Assert.Equal(1, rheda.Tint.Level);

        Assert.True(battle.Confirm());
        battle.Tick(zone, false, _ => null);

        var c08 = Combat(8);
        foreach (var c in new[] { alaron, rheda })
        {
            var body = c08.ReadWord(BodyPointers + 4 * (uint)c.Slot);
            Assert.Equal(BitConverter.UInt32BitsToSingle(c08.ReadWord(body + 0x75C)), c.Tint.Level);
            Assert.Equal(BitConverter.UInt32BitsToSingle(c08.ReadWord(body + 0x760)), c.Tint.Rate);
        }
        for (var i = 0; i < 1200 && alaron.Sheet.Effects.Count == 0; i++)
            battle.Tick(zone, false, _ => null);
        Assert.Equal(1 / 30f, alaron.Tint.Rate, 5);
    }

    /// <summary>The visuals' actors, each still playing its cast.</summary>
    private sealed class Playing : ISpellFxHost
    {
        public Aidyn.Core.Actors.IActorMotion? Motion(SpellFxVisual visual) => new Still();

        public bool Emitting(SpellFxVisual visual) => true;

        private sealed class Still : Aidyn.Core.Actors.IActorMotion
        {
            public int StateLength => 300;
            public int Frame => 1;
            public bool Entered => false;
            public float Tick(int request) => 0;
        }
    }
}

/// <summary>
/// A flask's blast (<c>Combatant.FlaskExplosion</c>, <c>0x800652F8</c>) at checkpoint 05, against 1.0's own function run on 05's RAM: Alaron's
/// Fire flask landing on rat 4's cell (66, 56).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class FlaskCheckpointTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    [Fact]
    public void TheTargetTakesTheBlastAndThenTheBlowAsTheOriginalDoes()
    {
        var built = At(Set, 5);
        var battle = built.Battle;
        var (alaron, rat) = (built[0], built[4]);
        alaron.HoldPotion(Potion.FireFlask, battle);
        battle.BlastCell = (66, 56);
        Assert.Equal(31, rat.Sheet.Hp);

        // The original: 31 -> 21 in the blast, its 10 kept on the rat, then 21 -> 11 as EndCast's ApplyHit lands it again.
        battle.FlaskExplosion(alaron, rat, 10);
        Assert.Equal((21, 10), (rat.Sheet.Hp, rat.Damage));
        Poke(battle, nameof(Battle.Target), rat.Slot);
        battle.Land(rat);
        Assert.Equal(11, rat.Sheet.Hp);
    }

    [Fact]
    public void ABlastOfFortyKillsItsTargetAndReachesThePartyThroughTheBytesWrap()
    {
        var built = At(Set, 5);
        var battle = built.Battle;
        var (alaron, rat) = (built[0], built[4]);
        alaron.HoldPotion(Potion.FireFlask, battle);
        battle.BlastCell = (66, 56);
        var before = battle.Combatants.ToDictionary(c => c, c => c.Sheet.Hp);
        battle.FlaskExplosion(alaron, rat, 40);
        Assert.True(rat.Sheet.IsDead);

        // A rat 20 units squared away loses 2; party members whose squared distance wraps past 256 to 32 or less lose 1.
        int Squared(Combatant c) => (ArenaGrid.CellOf(c.Cell.X) - 66) * (ArenaGrid.CellOf(c.Cell.X) - 66) + (ArenaGrid.CellOf(c.Cell.Y) - 56) * (ArenaGrid.CellOf(c.Cell.Y) - 56);
        var near = battle.Combatants.Single(c => c != rat && Squared(c) == 20);
        Assert.Equal(2, before[near] - near.Sheet.Hp);
        var wrapped = battle.Combatants.Where(c => c.Party && Squared(c) > 255 && (byte)Squared(c) is > 0 and <= 40).ToList();
        Assert.NotEmpty(wrapped);
        Assert.All(wrapped, c => Assert.Equal(40 / Math.Max(1, (int)(byte)Squared(c)), before[c] - c.Sheet.Hp));
    }
}
