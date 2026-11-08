using System.Numerics;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// A battle stepped from a checkpoint with no view, as Start Early's stage 1 has it (Start Early): the core's own actors for the
/// combatants and the spells' visuals, so a missile leaves the hand's locator and a spell lands at its visual's strike, as in the
/// runtime, where headless they left the body's middle and landed at once.
/// </summary>
public sealed class HeadlessBattleTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    [Fact]
    public void EveryCombatantStepsAnActorOfItsOwnThatTheCoreMade()
    {
        var built = At(Set, 5);
        var zone = Zone(built);
        Assert.All(built.Battle.Combatants, c =>
        {
            Assert.NotNull(c.Actor);
            Assert.Same(c.Actor, c.Motion);
        });
        // The party's hands hold what they throw and shoot; the rats have none.
        Assert.All(built.Battle.Combatants.Where(c => !c.Hostile), c => Assert.True(c.Actor!.HasLocator(c.HandLocator)));
        // The bodies' rolls on the shared stream move it on as they step, as the runtime's did.
        var seed = built.Game.Random.Animation.Seed;
        for (var i = 0; i < 600; i++)
            built.Battle.Tick(zone);
        Assert.NotEqual(seed, built.Game.Random.Animation.Seed);
    }

    [Fact]
    public void AlaronsFlaskLeavesHisHandsLocatorAsTheCorePosesItAtTheThrowsStrike()
    {
        // Checkpoint 05, Alaron's turn: a Fire flask at rat 4 (Combat.StartCastCamera: from the hand's locator, +0x88).
        var built = At(Set, 5);
        var battle = built.Battle;
        var alaron = built[0];
        Poke(battle, nameof(Battle.State), CombatState.Turn);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        built.Game.Party.Inventory.Add(PotionId(BasePotion.FireFlask), 2);
        alaron.HoldPotion(battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], battle);
        Assert.True(battle.Attack());
        Assert.True(battle.Confirm());
        built.Game.Random.Combat.Seed = 1;

        var zone = Zone(built);
        MissileFlight? flight = null;
        var ticks = 0;
        for (; ticks < 2000 && flight is null; ticks++)
        {
            battle.Tick(zone);
            flight = battle.Flight;
        }
        Assert.NotNull(flight);
        // Thrown at or after the throw's strike, its frame (u16)(int)(length × 0.4), while the throw plays on.
        var throwing = alaron.Actor!;
        Assert.InRange(throwing.Frame, (int)(throwing.StateLength * Battle.StrikeShare(alaron.Look, Battle.ThrowRequest)), throwing.StateLength);

        // From his hand, as the core poses his actor's scene at the strike, which the runtime's posed scene agrees with: not his middle.
        var hand = alaron.LocatorAt(alaron.HandLocator)!.Value.Translation;
        Assert.Equal(hand, flight!.From);
        var middle = alaron.Body.Sphere.Position with
        {
            Y = alaron.Body.Sphere.Position.Y + alaron.Look.Height * alaron.Look.Scale * 0.5f - alaron.Body.Sphere.Radius,
        };
        Assert.True(Vector3.Distance(hand, middle) > 0.1f);
        var feet = alaron.Body.Sphere.Position.Y - alaron.Body.Sphere.Radius;
        Assert.InRange(hand.Y - feet, 0, alaron.Look.Height * alaron.Look.Scale);
    }

    [Fact]
    public void RhedasEnduranceLandsTheTickAfterHerActorReachesItsStrike()
    {
        // Checkpoint 07, Rheda's Endurance at herself: her caster's actor (0x1C8E) times the strike, the core's own, played headless as
        // the runtime plays it; the battle lands the cast on the tick after the actor reaches it (Battle.SwingEnded reads it before the
        // visuals step), once SwingTicks have passed.
        var built = At(Set, 7);
        built.Game.Random.Combat.Seed = built.Cp.ReadWord(RandomState);
        var battle = built.Battle;
        var rheda = built[1];
        rheda.Spell = new SpellChoice(SpellSourceKind.Known, rheda.Sheet.Spells.Select((s, i) => (s, i)).Single(p => p.s.Spell.Name == "Endurance").i);
        Poke(battle, nameof(Battle.State), CombatState.TargetSpell);
        battle.TargetsB.BeginSpell(battle, rheda);
        Assert.True(battle.Confirm());

        var zone = Zone(built);
        var strike = built.Game.Rules.Magic!.Spell(RecordId.Hex(RecordTypes.Spell, 0x0310))!.Strike;
        SpellFxVisual? caster = null;
        int started = -1, reached = -1, struck = -1, landed = -1;
        for (var tick = 0; tick < 2000 && landed < 0; tick++)
        {
            var before = battle.State;
            battle.Tick(zone);
            if (caster is null && battle.Fx.Visuals.FirstOrDefault(v => v.Role == SpellFxRole.Caster) is { } v)
                (caster, started) = (v, tick);
            if (reached < 0 && caster?.Actor is { } a && a.Frame >= (int)(a.StateLength * strike))
                reached = tick;
            if (struck < 0 && battle.Fx.Casts.FirstOrDefault() is { Struck: true })
                struck = tick;
            if (before == CombatState.Action && battle.State != CombatState.Action)
                landed = tick;
        }
        Assert.NotNull(caster);
        Assert.InRange(caster!.Actor!.StateLength, 100, 300);
        // The cast struck as its actor reached its strike, well after it started, not at once as with no view before.
        Assert.Equal(reached, struck);
        Assert.True(struck > started + Battle.SwingTicks);
        Assert.Equal(struck + 1, landed);
    }
}
