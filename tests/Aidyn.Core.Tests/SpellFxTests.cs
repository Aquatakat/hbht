using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Calendar;
using Aidyn.Core.Combat;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// The spells' visuals in a battle (<c>combatspellvisuals.cpp</c>): what a cast plays, the lasting scenes' turns, the glow, the sky's
/// dimming, Darkness's and Light's time of day and the webs' hold, from 1.0's code (docs/systems/combat.md's "What spells look like").
/// </summary>
public sealed class SpellFxTests
{
    private static readonly RecordId CasterActor = RecordId.Hex(RecordTypes.Actor, 0x1C8E);
    private static readonly RecordId TargetActor = RecordId.Hex(RecordTypes.Actor, 0x1CC9);
    private static readonly RecordId Aura = RecordId.Hex(RecordTypes.Scene, 0x1C76);
    private static readonly RecordId Refused = RecordId.Hex(RecordTypes.Actor, 0x1CAA);
    private static readonly RecordId Resisted = RecordId.Hex(RecordTypes.Actor, 0x37F1);

    private static readonly MagicRecord Magic = MagicData.Record with { Refused = Refused, Resisted = Resisted };

    private static SpellRecord Shown(SpellRecord spell, bool caster = true, bool target = true, bool aura = true) =>
        spell with { Visuals = new MagicVisuals(caster ? CasterActor : null, target ? TargetActor : null, Aura: aura ? Aura : null) };

    private static (SpellFx Fx, Combatant A, Combatant B) Battle(GameClock? clock = null)
    {
        var a = BattleFixtures.Fighter(0, 20);
        var b = BattleFixtures.Fighter(1, 20);
        a.Body.Sphere.Position = new Vector3(1, 0.7f, 2);
        b.Body.Sphere.Position = new Vector3(5, 0.7f, 2);
        return (new SpellFx(clock, Magic), a, b);
    }

    private static void Tick(SpellFx fx, int ticks, ISpellFxHost? host = null, params Combatant[] bodies)
    {
        for (var i = 0; i < ticks; i++)
        {
            foreach (var c in bodies)
                c.Tint.Step();
            fx.Tick(host);
        }
    }

    // ---- What a cast plays ----

    [Fact]
    public void ACastPlaysItsActorsAtTheCastersAndTargetsFeetAndQueuesItsLastingSceneAtTheTarget()
    {
        var (fx, a, b) = Battle();

        var cast = fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast)!;

        Assert.Equal(0, cast.Handle);
        var visuals = fx.Visuals.ToList();
        Assert.Equal([SpellFxRole.Caster, SpellFxRole.Target, SpellFxRole.Aura], visuals.Select(v => v.Role));
        Assert.Equal([CasterActor, TargetActor, Aura], visuals.Select(v => v.Id));
        // At the feet: the body's centre less its unscaled radius.
        Assert.Equal(new Vector3(1, 0, 2), visuals[0].Position);
        Assert.Equal(new Vector3(5, 0, 2), visuals[1].Position);
        // Each actor's emitters attach to the body it sits on, then the other of its cast.
        Assert.Same(a, visuals[0].Owner);
        Assert.Same(b, visuals[0].Other);
        Assert.Same(b, visuals[1].Owner);
        Assert.Same(a, visuals[1].Other);
        Assert.Equal(1, fx.AurasPending);
    }

    [Fact]
    public void AnActorShownAtTheHeadStandsTheEntitysHeightAboveTheFeet()
    {
        var (fx, a, b) = Battle();
        var spell = MagicData.Weakness with { Visuals = new MagicVisuals(Target: TargetActor, TargetAtHead: true) };

        fx.Start(a, b, spell, SpellVisualKind.Cast);

        Assert.Equal(new Vector3(5, b.Look.Height * b.Look.Scale, 2), fx.Visuals.Single().Position);
    }

    [Fact]
    public void ARefusedCastPlaysTheRefusedActorAtTheCasterAloneAndAResistedOneTheResistedActorAtTheTarget()
    {
        var (fx, a, b) = Battle();

        fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Failed);
        fx.Start(a, b, Shown(MagicData.Weakness), SpellVisualKind.Resisted);

        var visuals = fx.Visuals.ToList();
        Assert.Equal([(SpellFxRole.Refused, Refused, a), (SpellFxRole.Resisted, Resisted, b)], visuals.Select(v => (v.Role, v.Id, v.Owner)));
        // Neither glows a body or dims the sky.
        Assert.Equal(0, a.Tint.Rate);
        Assert.Equal(1, fx.Sky.Goal);
    }

    [Fact]
    public void AnActorPlaysItsCastUntilItsAnimationHasRunThenWaitsForItsParticles()
    {
        var (fx, a, b) = Battle();
        var host = new Host(length: 10);
        fx.Start(a, b, Shown(MagicData.Endurance, target: false, aura: false), SpellVisualKind.Cast);
        var actor = fx.Visuals.Single();

        Tick(fx, 1, host);
        Assert.Equal([5], host.Requests.Distinct());

        Tick(fx, 20, host);
        // Stepped to its length and no further: the original stops ticking it there.
        Assert.Equal(10, host.Motion.Frame);
        Assert.True(actor.Played);
        Assert.Contains(actor, fx.Visuals);

        host.Emitting = false;
        Tick(fx, 1, host);
        Assert.Empty(fx.Visuals);
        Assert.Empty(fx.Casts);
    }

    [Fact]
    public void HeadlessActorsEndAtOnceAndTheLastingSceneStays()
    {
        var (fx, a, b) = Battle();
        fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast);

        Tick(fx, 1);

        Assert.Equal([SpellFxRole.Aura], fx.Visuals.Select(v => v.Role));
    }

    // ---- The lasting scenes' turns ----

    [Fact]
    public void NoLastingSceneShowsForThreeHundredTicksAfterACastThenItsTurnIsAHumpOf125Ticks()
    {
        var (fx, a, b) = Battle();
        fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast);
        var aura = fx.Visuals.Single(v => !v.IsActor);

        var alphas = new List<byte>();
        for (var i = 0; i < 600; i++)
        {
            fx.Tick(null);
            alphas.Add(aura.Showing ? aura.Alpha : (byte)0);
        }

        var first = alphas.FindIndex(x => x > 0);
        Assert.InRange(first, SpellFx.CastWindowTicks, SpellFx.CastWindowTicks + 2);
        var peak = alphas.Max();
        Assert.InRange(peak, 250, 255);
        // sin(t / 125 × 3.14): it rises and falls back within its 125 ticks, then the same scene, alone, takes its turn again.
        var end = alphas.FindIndex(first, x => x == 0);
        Assert.InRange(end - first, 120, 125);
        Assert.Contains(alphas.Skip(end + 1), x => x > 0);
    }

    [Fact]
    public void TwoBodiesLastingScenesTakeTurnsOneAtATimeAcrossTheBattle()
    {
        var (fx, a, b) = Battle();
        fx.Start(a, a, Shown(MagicData.Endurance), SpellVisualKind.Cast);
        fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast);
        var auras = fx.Visuals.Where(v => !v.IsActor).ToList();

        var order = new List<Combatant>();
        for (var i = 0; i < 1200; i++)
        {
            fx.Tick(null);
            Assert.True(auras.Count(v => v.Showing && v.Alpha > 0) <= 1);
            if (auras.FirstOrDefault(v => v.Showing && v.Alpha > 0) is { } shown && order.LastOrDefault() != shown.Owner)
                order.Add(shown.Owner);
        }

        Assert.Equal([a, b, a, b, a], order.Take(5));
    }

    [Fact]
    public void AnEffectsEndEndsItsLastingSceneOnceItsTurnIsOverAndLeavesTheActors()
    {
        var (fx, a, b) = Battle();
        var host = new Host(length: 1000) { Emitting = true };
        var cast = fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast)!;
        Tick(fx, SpellFx.CastWindowTicks + 30, host);
        var aura = fx.Visuals.Single(v => !v.IsActor);
        Assert.True(aura.Showing);

        ((Magic.IEffectVisual)cast).Ended();
        Tick(fx, 10, host);
        Assert.Contains(aura, fx.Visuals);
        Tick(fx, SpellFx.AuraTicks, host);

        Assert.DoesNotContain(aura, fx.Visuals);
        Assert.Equal(2, fx.Visuals.Count(v => v.IsActor));
        Assert.Equal(0, fx.AurasPending);
    }

    [Fact]
    public void RemovingTheEffectEndsWhatTheBattleShowsOfIt()
    {
        var (fx, a, b) = Battle();
        var spell = Shown(MagicData.Endurance, caster: false, target: false);
        var record = b.Sheet.AddEffect(spell.Id, spell.Effects!, 2, spell.School, 3, 1000, null, null, null)!;
        record.Visual = fx.Start(a, b, spell, SpellVisualKind.Cast);
        Tick(fx, 1);

        b.Sheet.RemoveEffect(record);
        Assert.Null(record.Visual);
        Assert.Single(fx.Visuals);

        // Its turn had begun, held at the start of its hump by the cast's window, so it goes once that hump is over.
        Tick(fx, SpellFx.CastWindowTicks + SpellFx.AuraTicks + 2);
        Assert.Empty(fx.Visuals);
        Assert.Empty(fx.Casts);
    }

    // ---- The bodies, the sky and the clock ----

    [Fact]
    public void ACastGlowsBothBodiesWhiteOver30TicksFor150AndDimsTheSkyToHalf()
    {
        var (fx, a, b) = Battle();
        fx.Start(a, b, Shown(MagicData.Endurance), SpellVisualKind.Cast);

        Tick(fx, 30, null, a, b);
        Assert.Equal(1, a.Tint.Level, 4);
        Assert.Equal(1, b.Tint.Level, 4);
        Assert.Equal(Vector3.One, a.Tint.Colour);
        Assert.True(a.Unfogged);
        Assert.Equal(0.5f, fx.Sky.Value, 4);

        Tick(fx, SpellFx.GlowTicks + 31, null, a, b);
        Assert.Equal(0, a.Tint.Level);
        Assert.False(a.Unfogged);
        Assert.Equal(1, fx.Sky.Value, 4);
    }

    [Fact]
    public void LightHoldsTheSkysTimeAtNoonWhileItLastsAndEasesItBackAfter()
    {
        var clock = new GameClock();
        clock.SetTimeOfDay(20);
        var (fx, a, b) = Battle(clock);
        var light = Shown(MagicData.Endurance) with { Effects = [new PartOfDayEffect(Night: false)] };
        var cast = fx.Start(a, b, light, SpellVisualKind.Cast)!;

        Tick(fx, 60);
        Assert.Equal(0.5f, clock.ShownTimeOfDay, 4);
        // The rules' time of day is the clock's.
        Assert.Equal(20 / 24f, clock.TimeOfDay, 4);

        ((Magic.IEffectVisual)cast).Ended();
        Tick(fx, 60);
        Assert.Equal(20 / 24f, clock.ShownTimeOfDay, 4);
    }

    [Fact]
    public void AWebHoldsItsTargetsPoseUntilItEnds()
    {
        var (fx, a, b) = Battle();
        var cast = fx.Start(a, b, Shown(MagicData.WebOfStarlight), SpellVisualKind.Cast)!;
        Assert.True(b.PoseHeld);

        ((Magic.IEffectVisual)cast).Ended();

        Assert.False(b.PoseHeld);
    }

    [Fact]
    public void ThePotionsVisualPlaysOnTheOneWhoDrinksIt()
    {
        var (fx, a, _) = Battle();
        var potion = new ItemRecord(RecordId.Hex(RecordTypes.Item, 0x1009), "Strength Potion", default, 0, ItemKind.Potion,
            Visuals: new MagicVisuals(Target: CasterActor, Aura: Aura));

        fx.StartPotion(a, potion);

        Assert.Equal([(SpellFxRole.Potion, a, a), (SpellFxRole.PotionAura, a, a)], fx.Visuals.Select(v => (v.Role, v.Owner, v.Other)));
        Assert.Equal(0, a.Tint.Rate);
    }

    [Fact]
    public void TheBattlesEndLetsTheGlowsPosesAndTimeGo()
    {
        var clock = new GameClock();
        var (fx, a, b) = Battle(clock);
        fx.Start(a, b, Shown(MagicData.WebOfStarlight) with { Effects = [new PartOfDayEffect(true), new ImmobiliseEffect()] }, SpellVisualKind.Cast);
        Tick(fx, 10);

        fx.Free();

        Assert.Empty(fx.Visuals);
        Assert.False(b.PoseHeld);
        Assert.Equal(-1 / SpellFx.FadeTicks, a.Tint.Rate, 5);
        Assert.Equal(0, clock.ShownOffset);
    }

    // ---- The body's tint ----

    [Fact]
    public void ATintMovesTheAmbientFromHalfTheSkyTowardItsColourByItsLevelAndTheFade()
    {
        var tint = new BodyTint();
        var sky = new Vector3(0.4f);
        Assert.Equal(sky, tint.Ambient(sky, 0.5f));

        tint.Set(4, Vector3.One);
        tint.Step(2);

        Assert.Equal(0.5f, tint.Level);
        Assert.Equal(0.35f, tint.Ambient(sky, 0.5f).X, 5);
        tint.Step(10);
        Assert.Equal((1f, 0f), (tint.Level, tint.Rate));
        tint.Clear(0);
        tint.Step();
        Assert.Equal((0f, 0f), (tint.Level, tint.Rate));
    }

    private sealed class Host(int length) : ISpellFxHost
    {
        public Motion Motion { get; } = new(length);

        public List<int> Requests => Motion.Requests;

        public bool Emitting { get; set; } = true;

        IActorMotion? ISpellFxHost.Motion(SpellFxVisual visual) => Motion;

        bool ISpellFxHost.Emitting(SpellFxVisual visual) => Emitting;
    }

    private sealed class Motion(int length) : IActorMotion
    {
        public List<int> Requests { get; } = [];

        public int StateLength => length;

        public int Frame { get; private set; } = 1;

        public bool Entered => false;

        public float Tick(int request)
        {
            Requests.Add(request);
            Frame++;
            return 0;
        }
    }
}
