using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Actors;
using Aidyn.Core.Chunks;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Wedged against leaning plain walls (docs/systems/movement.md#wedged-against-a-leaning-plain-wall): with plain collision's
/// push down once a frame, he walks out of the crease at Overworld A22 where the user found him wedged; and where he's still
/// held falling, as in C15's slot, the port's release lets him steer out.
/// </summary>
public sealed partial class GameSessionTests
{
    /// <summary>Alaron's actor, whose run moves him.</summary>
    private static readonly RecordId AlaronActor = RecordId.Hex(RecordTypes.Actor, 0x25F9);

    /// <summary>A tick with the stick held, a journal entry's notice closed first if one is up.</summary>
    private static void Hold(GameSession session, Vector2 stick)
    {
        if (session.Trek.Use.Popup is not null)
            session.Trek.Use.Accept();
        session.Tick(SessionSettings.Original, () => new PlayerInput(stick), false, _ => null);
    }

    [Fact]
    public void AtTheFootOfA22sLeaningRockFaceAlaronWalksIntoTheCreaseAndOutAgainEveryWay()
    {
        // The user's spot, his feet at (46.7, −26.3, 40.1): the rock face aidyn:map/0x0ECB's triangle 476, plain collision
        // (surface 1) leaning back at 76°, meets the grass below it (491). Facing −Z, up on the stick walks him into it.
        for (var way = 0; way < 8; way++)
        {
            var (session, _) = StartIn(0, "A22", 46.7f, 40.1f, y: -25.8f, setDown: false);
            var player = session.Trek.Player;
            session.Trek.PlayerAnimation = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
            for (var i = 0; i < 90; i++)
                Hold(session, new Vector2(0, 1));
            var crease = player.Sphere.Position;
            // Against the face, held up by the ground rule and pressed down once a frame: never falling fast enough to stop his
            // steering, as he does in 1.0 at 30 fps, where the push isn't spread over the frame's ticks.
            Assert.True(player.Sphere.Velocity.Y > -0.048f, $"{player.Sphere.Velocity}");

            var stick = new Vector2(MathF.Sin(way * MathF.PI / 4), MathF.Cos(way * MathF.PI / 4));
            for (var i = 0; i < 120; i++)
                Hold(session, stick);
            Assert.True(Vector3.Distance(crease, player.Sphere.Position) > 1, $"way {way}: from {crease} to {player.Sphere.Position}");
            Assert.False(player.FellOut);
        }
    }

    [Fact]
    public void InC15sSlotHeldFallingAlaronSteersAgainAfterAQuarterSecondAndWalksOutEveryWay()
    {
        // The slot, about 1 wide, between C15's plain wall 335 (leaning back at 80°) and an upright plain wall facing it (1143):
        // walked into it as the bulk scan walks into every steep plain wall, he hangs between the two falling by his velocity,
        // never dropping, and without the port's release (PinnedTicks) he can't steer out any way.
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var chunk = set.Chunks.Single(c => c.Name == "C15");
        var origin = set.Origin(chunk.Row, chunk.Column);
        var wall = CollisionMesh.For(Data.Set, chunk.Map)[335];
        var (a, b, c) = (wall.A + origin, wall.B + origin, wall.C + origin);
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        Assert.Equal(1, wall.Surface);
        Assert.InRange(normal.Y, 0.16f, 0.18f);
        var world = new CollisionWorld();
        new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set).Update(a);
        var inward = Vector2.Normalize(new Vector2(-normal.X, -normal.Z));
        var point = a * 0.6f + b * 0.2f + c * 0.2f;
        var ways = new List<string>();
        for (var way = 0; way < 8; way++)
        {
            var player = new PlayerMotion(point - new Vector3(inward.X, 0, inward.Y) * 1.2f + new Vector3(0, 0.3f, 0), inward);
            var run = new SteadyMotion(0.06f);
            for (var i = 0; i < 20; i++)
                player.Step(world, new MoveInput(Vector2.Zero), inward, run);
            for (var i = 0; i < 60; i++)
                player.Step(world, new MoveInput(new Vector2(0, 1)), inward, run);
            var notch = player.Sphere.Position;
            var mostPinned = 0;
            var stick = new Vector2(MathF.Sin(way * MathF.PI / 4), MathF.Cos(way * MathF.PI / 4));
            for (var i = 0; i < 120; i++)
            {
                player.Step(world, new MoveInput(stick), inward, run);
                mostPinned = Math.Max(mostPinned, player.Pinned);
            }
            Assert.False(player.FellOut);
            if (Vector3.Distance(notch, player.Sphere.Position) > 1)
                ways.Add($"{way}, pinned {mostPinned}");
        }
        // Out every way, and only once the falling rule has let go of him.
        Assert.Equal(8, ways.Count);
        Assert.All(ways, w => Assert.True(int.Parse(w.Split("pinned ")[1]) >= PlayerMotion.PinnedTicks, w));
    }
}
