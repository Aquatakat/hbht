using System.Numerics;
using HBHT.Core;
using HBHT.Core.Combat;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

public sealed partial class GameSessionTests
{
    /// <summary>The Cave Bear north of Erromon, in the Overworld's A06: a fight with no flag to mark it beaten.</summary>
    private static readonly RecordId CaveBear = RecordId.Parse("aidyn:voxel/0x0E96/0");

    [Fact]
    public void AFlaglessPartyBeatenStaysAwayUntilThePlayerHasLeftTheAreaAndComeBack()
    {
        var map = Data.Set.Get<MapRecord>(MapRecord.MapOf(CaveBear));
        var bear = Assert.IsType<MonsterPartyObject>(map.Objects.Single(o => o.Index == MapRecord.IndexOf(CaveBear)));
        Assert.Equal("Cave Bear", Data.Set.Get<EntityRecord>(bear.Entity).Name);
        Assert.Null(bear.Used);
        Assert.Null(bear.Dialogue);

        var (session, host) = Start("A06", 30, 60);
        var trek = session.Trek;
        bool BearThere() => trek.Wanderers.All.Any(w => w.Map.Record.Id == map.Id && w.Party.Index == bear.Index);
        Assert.True(BearThere());

        // The bear's battle, won: under flag 0, which can't be set and reads clear, so it's remembered (Combat.StateVictory, 0x80022450).
        var (place, terrain) = trek.Here();
        session.CueBattle(BattleSetup.WithParty(BattleSource.MonsterParty, CaveBear, bear, place, terrain, ambush: false) with { Arena = 1 });
        Assert.Equal(SessionStep.BattleCued, TickUntil(session, SessionStep.BattleCued, 200));
        session.BeginBattle();
        var battle = session.Battle!;
        Assert.Null(battle.Setup.EncounterFlag);
        var alaron = session.Game.Party.Slots[0]!;
        for (var i = 0; i < 3000 && !battle.ForceWin(); i++)
        {
            alaron.Heal(alaron.MaxHp - alaron.Hp);
            Tick(session);
        }
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal(BattleOutcome.Won, battle.Outcome);
        var beaten = new Memory(CaveBear, MemoryKind.Beaten);
        Assert.Equal(MapObjectMemory.Short, session.Game.Memory.Find(beaten)?.Lifetime);

        // Trek comes back after the post-battle windows, and again as after a cut-scene, without it.
        session.EndBattle();
        session.MenuClosed(PauseExit.Close);
        Assert.Equal(GameMode.Trek, session.Mode);
        Assert.False(BearThere());
        trek.Rebuild();
        Assert.False(BearThere());

        // Each chunk crossed is a teleport in the original (TeleportPlayer, 0x8000BB54, ages the memories first): two leave it
        // remembered, and A06 still loaded.
        var set = trek.Chunks.MapSet;
        void WalkTo(string name)
        {
            var chunk = set.Chunks.Single(c => c.Name == name);
            // The first ground on a 10-unit grid across the chunk.
            var (at, grounded) = (from x in Enumerable.Range(1, 9)
                                  from z in Enumerable.Range(1, 9)
                                  select Body.SetDown(trek.Chunks.World, trek.Chunks.Origin(chunk) + new Vector3(10 * x, 2000, 10 * z),
                                      trek.Player.Sphere.Radius, null)).FirstOrDefault(s => s.Grounded);
            Assert.True(grounded);
            trek.Player.Sphere.Position = at;
            Tick(session);
            trek.Chunks.Update(at);
            Assert.Equal(name, trek.Chunks.ChunkAt(trek.Player.Sphere.Position)?.Name);
        }
        WalkTo("B06");
        WalkTo("B07");
        Assert.Equal(1, session.Game.Memory.Find(beaten)?.Lifetime);
        Assert.False(BearThere());

        // The third forgets it, and A06 goes with B08's 3×3; back in B07, A06 loads again with the bear.
        WalkTo("B08");
        Assert.Null(session.Game.Memory.Find(beaten));
        Assert.DoesNotContain(trek.Chunks.All, c => c.Map.Record.Id == map.Id);
        WalkTo("B07");
        Assert.True(BearThere());
        Assert.Empty(host.Warnings);
    }
}
