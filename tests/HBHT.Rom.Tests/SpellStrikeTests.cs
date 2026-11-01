using HBHT.Core.Actors;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Where each spell lands in its effect animation (<see cref="SpellRecord.Strike"/>), which the importer works out from the Borgs and
/// the runtime's actor machine reports again from the imported plugin.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SpellStrikeTests(Na10Import fixture)
{
    /// <summary>The mean length of the party casters' cast animations (91, 123, 129, 131, 161, 201 and 239), the importer's reference.</summary>
    private const int Reference = 153;

    private PluginSet Plugins => fixture.Data.Set;

    private SpellRecord Get(int id) => Plugins.Get<SpellRecord>(RecordId.Hex(RecordTypes.Spell, id));

    [Fact]
    public void EarthSmiteStrikesAtItsShareOfTheReferenceCast()
    {
        Assert.Equal(153f / 241, Get(0x0304).Strike, 0.001f);
        Assert.Equal(0.635f, Get(0x0304).Strike);
    }

    [Fact]
    public void ASpellWhoseAnimationIsShortOrWhichHasNoActorStrikesAtItsEnd()
    {
        Assert.Equal(1f, Get(0x0312).Strike);
        Assert.Equal(1f, Get(0x0332).Strike);
        Assert.All(Plugins.All<SpellRecord>(RecordTypes.Spell).Where(s => s.Visuals?.Target is null && s.Visuals?.Caster is null),
            s => Assert.Equal(1f, s.Strike));
    }

    /// <summary>
    /// The tick it lands at is the reference's (or the animation's end, if that is sooner), within a tick or two, where the
    /// length is the actor machine's: the state request 5 leads to from the initial state, once entered on that request.
    /// </summary>
    [Fact]
    public void EverySpellLandsAtTheReferenceTickOfItsVisualsCast()
    {
        var checkedSpells = 0;
        foreach (var spell in Plugins.All<SpellRecord>(RecordTypes.Spell))
        {
            if ((spell.Visuals?.Target ?? spell.Visuals?.Caster) is not { } actorId)
                continue;
            var length = CastLength(Plugins.Get<ActorRecord>(actorId));
            var lands = (int)(spell.Strike * length);
            Assert.InRange(lands, Math.Min(Reference, length), Math.Min(Reference, length) + 2);
            checkedSpells++;
        }
        Assert.True(checkedSpells >= 30);
    }

    /// <summary>The length of the state a visual's cast leads to, stepping the actor on request 5 as <c>SpellFx</c> does.</summary>
    private int CastLength(ActorRecord actor)
    {
        var clips = ActorClips.For(Plugins, actor.Scene, actor.RootTransform) ?? throw new InvalidOperationException($"{actor.Id} has no scene.");
        var motion = new ActorMotion(actor, clips, new Lcg(0));
        motion.Request = 5;
        for (var tick = 0; tick < 1000; tick++)
        {
            if (motion.Step())
                return motion.StateLength;
        }
        throw new InvalidOperationException($"{actor.Id} never leaves its initial state on request 5.");
    }
}
