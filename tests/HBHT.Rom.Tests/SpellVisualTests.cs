using HBHT.Core.Actors;
using HBHT.Core.Combat;
using HBHT.Core.Particles;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Tests;

/// <summary>
/// What each spell and potion shows in a battle (<see cref="SpellVisualTables"/>, importer 46): the five switch functions of 1.0's
/// <c>combatspellvisuals.cpp</c> and the two immediates, decoded from every ROM and compared with 1.0's own functions run in the interpreter
/// (session 10's research, <c>kind:Borg:memory</c> below), and what the import makes of them.
/// </summary>
public sealed class SpellVisualTests(Na10Import fixture)
{
    // The cases of each function, as kind:Borg:memory in hex, kinds in decimal.
    private const string Caster =
        "2:1D7A:11C00 3:1AD9:4C00 10:1AB9:11C00 12:1E50:3800 13:1B51:11C00 15:1B37:4C00 19:1C8E:11C00 20:1DA1:8C00 21:1DBD:3400 23:1B68:4000 " +
        "25:1AF8:C400 26:1EF7:1800 29:1BD8:17400 30:1CF3:5800 32:1DFD:7C00 35:1DE7:5800 37:1D43:C800 38:1E65:9000 39:1E76:6800 40:1E89:6800 " +
        "41:1E9D:3400 46:1DB6:4800 47:1B29:22800 48:1E0E:4800 51:1A48:11C00 54:1D31:C400";

    private const string Target =
        "0:1D0D:6000 1:1C9F:4800 4:1B87:5000 5:1BF3:2C00 6:1C55:4C00 7:1C66:8000 8:1CC9:5000 9:1D21:8000 11:1EEC:A400 14:1D52:2800 " +
        "15:1C9F:4800 16:1B79:4000 17:1B95:5000 21:1DC4:1C00 22:1ECD:2800 24:1AC6:5000 27:1BA3:5000 28:1BBC:C400 31:1DD6:5000 33:1E42:5400 " +
        "34:1EC0:C800 42:1C07:1FC00 43:1C18:6000 44:1C30:1CC00 45:1C44:F800 52:1CB9:2400 53:1CDB:7800 56:1E32:7800 57:1E1C:3800 58:1EDF:8400";

    private const string Aura =
        "0:1D0F:2000 3:1ACC:2000 4:1BA4:2400 5:1BE7:2800 10:1E33:3C00 13:1B47:3C00 14:1D46:2800 16:1B6B:2400 17:1BA5:2400 19:1C76:3C00 " +
        "22:1EC3:2800 23:1B56:2400 24:1AC7:2000 25:1ADD:2C00 26:1EFC:2800 27:1BA6:2400 30:1CE3:3000 31:1DC7:3800 32:1DEC:2400 34:1EA9:6400 " +
        "35:1DDB:2000 37:1D34:2000 38:1E55:3400 39:1E69:3C00 40:1E7A:3C00 41:1E8D:2400 46:1DA6:2000 47:1B07:5C00 48:1E02:2000 51:1BF4:3C00 " +
        "52:1CAD:2800 53:1CCF:3400 56:1E21:2800 57:1E02:2000";

    private const string Potion =
        "0:1A56:3000 1:1A00:6400 2:1A83:3C00 3:19CB:3C00 4:1A65:4000 5:1A91:4800 6:1A10:6400 7:19DB:4400 8:1A74:5000 9:1AB9:12000 " +
        "10:1A48:12000 15:1A20:5400";

    private const string PotionAura = "3:19C0:2000 9:1AA1:3C00 10:1A30:3C00 15:1A14:2000";

    private static readonly int[] Heads = [4, 5, 14, 16, 17, 22, 27, 28, 31, 52];

    private static Dictionary<int, VisualBorg> Parse(string table) =>
        table.Split(' ').Select(e => e.Split(':')).ToDictionary(
            p => int.Parse(p[0]), p => new VisualBorg(Convert.ToInt32(p[1], 16), Convert.ToInt32(p[2], 16)));

    private static RecordId Spell(int id) => RecordId.Hex(RecordTypes.Spell, id);

    private static RecordId PotionItem(int n) => RecordId.Hex(RecordTypes.Item, 0x1000 + n);

    private static RecordId Actor(int borg) => RecordId.Hex(RecordTypes.Actor, borg);

    private static RecordId Scene(int borg) => RecordId.Hex(RecordTypes.Scene, borg);

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheTablesReadTheSameInEveryVersion(RomVersionId id)
    {
        var tables = SpellVisualTables.Read(TestRoms.Get(id));
        Assert.Equal(Parse(Caster), tables.Caster);
        Assert.Equal(Parse(Target), tables.Target);
        Assert.Equal(Parse(Aura), tables.Aura);
        Assert.Equal(Parse(Potion), tables.Potion);
        Assert.Equal(Parse(PotionAura), tables.PotionAura);
        Assert.Equal(Heads.Order(), tables.TargetAtHead.Order());
        Assert.Equal((0x1CAA, 0x37F1), (tables.Refused, tables.Resisted));
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheSpotValuesHold(RomVersionId id)
    {
        var tables = SpellVisualTables.Read(TestRoms.Get(id));
        Assert.Equal(new VisualBorg(0x1C8E, 0x11C00), tables.Caster[19]);
        Assert.Equal(0x1CC9, tables.Target[8].Borg);
        Assert.Equal(0x1C76, tables.Aura[19].Borg);
        Assert.Equal(0x1AB9, tables.Potion[9].Borg);
        Assert.Equal(0x1A14, tables.PotionAura[15].Borg);
        // Detecting Traps, Poison, Detect Moon Phase, Detect Sun Phase and Stealth show nothing at all.
        foreach (var kind in new[] { 18, 36, 49, 50, 55 })
        {
            Assert.DoesNotContain(kind, tables.Caster.Keys);
            Assert.DoesNotContain(kind, tables.Target.Keys);
            Assert.DoesNotContain(kind, tables.Aura.Keys);
        }
        Assert.Equal((26, 30, 34, 12, 4), (tables.Caster.Count, tables.Target.Count, tables.Aura.Count, tables.Potion.Count, tables.PotionAura.Count));
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void EveryBorgTheTablesNameIsOfItsType(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = SpellVisualTables.Read(rom);
        var archive = BorgArchive.Open(rom);
        BorgType Type(int borg) => archive.Listings[borg].Type;
        Assert.All(tables.Caster.Values.Concat(tables.Target.Values).Concat(tables.Potion.Values), v => Assert.Equal(BorgType.Actor, Type(v.Borg)));
        Assert.All(tables.Aura.Values.Concat(tables.PotionAura.Values), v => Assert.Equal(BorgType.Scene, Type(v.Borg)));
        Assert.Equal(BorgType.Actor, Type(tables.Refused));
        Assert.Equal(BorgType.Actor, Type(tables.Resisted));
    }

    [Fact]
    public void ASpellHasItsVisuals()
    {
        var set = fixture.Data.Set;
        // Endurance (kind 19) shows an actor at the caster and a lasting aura on the target.
        Assert.Equal(new MagicPresentation(Actor(0x1C8E), null, false, Scene(0x1C76)), set.Get<SpellRecord>(Spell(0x0310)).Presentation! with { Sound = null });
        // Fireball (kind 8) an actor at the target; Banishing (15) both, sharing Escape's; Crushing Death (28) lifts its actor to the head.
        Assert.Equal(new MagicPresentation(null, Actor(0x1CC9), false, null), set.Get<SpellRecord>(Spell(0x0305)).Presentation! with { Sound = null });
        Assert.Equal(new MagicPresentation(Actor(0x1B37), Actor(0x1C9F), false, null), set.Get<SpellRecord>(Spell(0x030A)).Presentation! with { Sound = null });
        Assert.Equal(new MagicPresentation(null, Actor(0x1BBC), true, null), set.Get<SpellRecord>(Spell(0x0315)).Presentation! with { Sound = null });
    }

    [Fact]
    public void TheSpellsThatShowNothingHaveASoundAlone()
    {
        var spells = fixture.Data.Set.All<SpellRecord>(RecordTypes.Spell);
        static bool Shows(SpellRecord s) => s.Presentation is { } p && (p.Caster ?? p.Target ?? p.Aura) is not null;
        Assert.Equal(59 - 5, spells.Count(Shows));
        Assert.Equal([18, 36, 49, 50, 55], spells.Where(s => !Shows(s)).Select(s => s.Position!.Value).Order());
        // Those five still play their cast's sound.
        Assert.All(spells.Where(s => !Shows(s)), s => Assert.NotNull(s.Presentation?.Sound));
        // Every spell's visuals are the tables' by its place in the list.
        var tables = SpellVisualTables.Read(TestRoms.Get(RomVersionId.Na10));
        Assert.All(spells.Where(Shows), s =>
        {
            var kind = s.Position!.Value;
            var v = s.Presentation!;
            Assert.Equal(tables.Caster.TryGetValue(kind, out var c) ? Actor(c.Borg) : (RecordId?)null, v.Caster);
            Assert.Equal(tables.Target.TryGetValue(kind, out var t) ? Actor(t.Borg) : (RecordId?)null, v.Target);
            Assert.Equal(tables.Aura.TryGetValue(kind, out var a) ? Scene(a.Borg) : (RecordId?)null, v.Aura);
            Assert.Equal(tables.TargetAtHead.Contains(kind), v.TargetAtHead);
        });
    }

    [Fact]
    public void APotionHasItsVisuals()
    {
        var set = fixture.Data.Set;
        // The Strength Potion's actor is the Strength spell's, with its own aura; the Healing Potion has an actor alone.
        Assert.Equal(new MagicPresentation(null, Actor(0x1AB9), false, Scene(0x1AA1)), set.Get<ItemRecord>(PotionItem(9)).Presentation);
        Assert.Equal(new MagicPresentation(null, Actor(0x1A65), false, null), set.Get<ItemRecord>(PotionItem(4)).Presentation);
        Assert.Equal(Scene(0x1A14), set.Get<ItemRecord>(PotionItem(15)).Presentation!.Aura);
        // The two placeholders, Clarity, Charisma and Stealth show nothing.
        Assert.All(new[] { 11, 12, 13, 14, 16 }, n => Assert.Null(set.Get<ItemRecord>(PotionItem(n)).Presentation));
        Assert.Equal(12, Enumerable.Range(0, 17).Count(n => set.Get<ItemRecord>(PotionItem(n)).Presentation is not null));
        // Nothing else has visuals.
        Assert.All(set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Kind != ItemKind.Potion), i => Assert.Null(i.Presentation));
    }

    [Fact]
    public void TheMagicRecordNamesTheRefusedAndResistedActors()
    {
        var magic = fixture.Data.Set.Singleton<MagicRecord>();
        Assert.Equal(Actor(0x1CAA), magic.Refused);
        Assert.Equal(Actor(0x37F1), magic.Resisted);
    }

    [Fact]
    public void SevenOfTheVisualsActorsAreParticlesAloneWhoseAnimationsHoldTheirScenesStill()
    {
        // The refused and resisted casts' actors, Dragon Flames', Banishing's and Escape's targets', vs. Star's and Wraith Touch's
        // casters' and the Fire Flask's have no models, and every Borg6 of theirs targets nothing: the importer lists them as still
        // animations, so Godot gives their scenes no animation player. Their states still run, and request 5 starts their emitters,
        // which are all they show (the runtime's ActorAnimator runs them without a player).
        var set = fixture.Data.Set;
        var magic = set.Singleton<MagicRecord>();
        var spells = set.All<SpellRecord>(RecordTypes.Spell).ToList();
        var actors = new[]
        {
            magic.Refused!.Value, magic.Resisted!.Value,
            spells.Single(s => s.Name == "Dragon Flames").Presentation!.Target!.Value,
            spells.Single(s => s.Name == "Banishing").Presentation!.Target!.Value,
            spells.Single(s => s.Name == "vs. Star").Presentation!.Caster!.Value,
            spells.Single(s => s.Name == "Wraith Touch").Presentation!.Caster!.Value,
            set.Get<ItemRecord>(PotionItem(0)).Presentation!.Target!.Value,
        };
        Assert.Equal([Actor(0x1CAA), Actor(0x37F1), Actor(0x1C55), Actor(0x1C9F), Actor(0x1E9D), Actor(0x1EF7), Actor(0x1A56)], actors);
        Assert.All(actors, id =>
        {
            var actor = set.Get<ActorRecord>(id);
            var clips = Plugins.Assets.ActorClips.For(set, actor.Scene, actor.RootTransform)!;
            Assert.False(clips.Animated);
            Assert.All(actor.States, s => Assert.True(clips.Clip(s.Animation)?.Length > 0));
            var emitters = set.Get<EmittersRecord>(RecordId.Hex(RecordTypes.Emitters, Convert.ToInt32(actor.Scene.Key, 16))).Emitters;
            Assert.Contains(emitters, e => e.Request == Requests.Id(5) && e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest));
        });
    }

    [Fact]
    public void FourOfDragonFlamesFlamesAtOnceNeedMoreEmittersThanTheOriginalsPoolHolds()
    {
        // Start Early draws a spell's casts at its targets at once. Dragon Flames' flame is 8 emitters, so two fill the original's
        // 16 and a third and fourth start none (the user's, Nov 11: four bandits burned and two showed it). The pool the runtime
        // draws from has room for more on top of the original's (BattleDrawing.ParticleRoom), handed out while a battle is drawn under Start Early.
        var set = fixture.Data.Set;
        var actor = set.Get<ActorRecord>(set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Name == "Dragon Flames").Presentation!.Target!.Value);
        var emitters = EmittersOf(set, actor);
        Assert.Equal(8, emitters.Emitters.Count(e => e.Request == Requests.Id(5) && e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest)));

        int Flames(ParticleRoom room)
        {
            var head = RuntimeHead();
            head.Room = room;
            var lit = 0;
            for (var target = 0; target < 4; target++)
            {
                var flame = new ActorParticles(head, emitters, new Still());
                var before = head.EmittersInUse;
                flame.Entered(5);
                lit += head.EmittersInUse - before == 8 ? 1 : 0;
            }
            return lit;
        }

        Assert.Equal(2, Flames(default));
        Assert.Equal(4, Flames(BattleDrawing.ParticleRoom));
    }

    [Fact]
    public void StartEarlysRoomIsTwiceWhatEightFireElementalsAndDispelNamingAtEightTargetsAtOnceNeed()
    {
        // The worst the user set out (Nov 11): eight Fire Elementals on the field, idling, each starting its fire on every loop as the
        // original's actors do; then two casts of Dispel Naming, which shows only at its target, land on eight targets together. The
        // pool, with all the room anyone could want, counts what it holds at most, and Start Early's room is twice that.
        var set = fixture.Data.Set;
        var elemental = set.Get<ActorRecord>(set.All<EntityRecord>(RecordTypes.Entity).First(e => e.Name == "Fire Elemental" && e.Body is not null).Body!.Actor);
        var dispel = set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Name == "Dispel Naming").Presentation!;
        Assert.Null(dispel.Caster);
        Assert.Null(dispel.Aura);
        var target = set.Get<ActorRecord>(dispel.Target!.Value);

        var head = new ParticleHead(new Lcg(), maxParticles: 0x4000, capacity: 0x4000, emitterCapacity: 1000) { Room = new ParticleRoom(1000, 0) };
        var walkers = Enumerable.Range(0, 8).Select(i =>
        {
            var motion = ActorMotion.For(set, elemental, new Lcg((uint)i + 1))!;
            motion.Particles = new ActorParticles(head, EmittersOf(set, elemental), new Still());
            return motion;
        }).ToList();
        // Staggered, and long enough that their fires have settled.
        for (var t = 0; t < 3000; t++)
        {
            for (var i = 0; i < walkers.Count; i++)
            {
                if (t >= i * 37)
                    walkers[i].Tick(0);
            }
            head.Step();
        }
        Assert.InRange(head.EmittersInUse, 1, 16);

        // Each as SpellFx steps a visual: its emitters as it's made, its cast until its state has run its length, then nothing.
        var visuals = Enumerable.Range(0, 8).Select(_ =>
        {
            var motion = ActorMotion.AsDrawn(set, target, new Lcg(0))!;
            var particles = new ActorParticles(head, EmittersOf(set, target), new Still());
            particles.Entered(motion.Request);
            return (motion, particles);
        }).ToList();
        var (emittersAtMost, particlesAtMost) = (0, 0);
        for (var t = 0; t < 1200; t++)
        {
            foreach (var w in walkers)
                w.Tick(0);
            foreach (var (motion, particles) in visuals)
            {
                if (motion.Frame < motion.StateLength)
                {
                    motion.Tick(Battle.CastRequest);
                    if (motion.Entered)
                        particles.Entered(Battle.CastRequest);
                }
            }
            head.Step();
            (emittersAtMost, particlesAtMost) = (Math.Max(emittersAtMost, head.EmittersInUse), Math.Max(particlesAtMost, head.ParticlesInUse));
        }
        Assert.Equal((58, 1660), (emittersAtMost, particlesAtMost));
        Assert.Equal(new ParticleRoom(emittersAtMost * 2, particlesAtMost * 2), BattleDrawing.ParticleRoom);
    }

    /// <summary>The runtime's head, as <c>Game.BuildWorld</c> makes it: room for the densest rain and for Start Early's battles on top.</summary>
    private static ParticleHead RuntimeHead() =>
        new(new Lcg(), capacity: WeatherParticles.PoolCapacity + BattleDrawing.ParticleRoom.Particles,
            emitterCapacity: ParticleHead.MaxEmitters + BattleDrawing.ParticleRoom.Emitters);

    private static EmittersRecord EmittersOf(PluginSet set, ActorRecord actor) =>
        set.Get<EmittersRecord>(RecordId.Hex(RecordTypes.Emitters, Convert.ToInt32(actor.Scene.Key, 16)));

    /// <summary>An anchor with every locator at the origin.</summary>
    private sealed class Still : IParticleAnchor
    {
        public bool Alive => true;

        public bool TryLocator(ParticleLocator locator, out System.Numerics.Matrix4x4 world)
        {
            world = System.Numerics.Matrix4x4.Identity;
            return true;
        }
    }
}
