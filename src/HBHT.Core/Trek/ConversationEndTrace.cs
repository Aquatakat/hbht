using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.MapObjects;
using HBHT.Core.Playthrough;
using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>
/// How trek's camera turned after a conversation ended (<see cref="ConversationEndTrace"/>): the view's total turn (every
/// tick's turn summed, either way), its net turn, how many times it turned back the other way, and its fastest turn in
/// degrees a second.
/// </summary>
public sealed record ConversationEndResult(string Npc, float TotalTurn, float NetTurn, int Reversals, float PeakTurn)
{
    public override string ToString() =>
        FormattableString.Invariant($"{Npc,-28} total {TotalTurn,6:F0}° net {NetTurn,6:F0}° reversals {Reversals} peak {PeakTurn,5:F0}°/s");
}

/// <summary>
/// A talk with an npc under both enhanced cameras, ended, then trek's camera traced as he stands for 3 seconds and walks
/// off away from the npc for 3 (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): the rig for the turn after
/// a conversation's end.
/// </summary>
public static class ConversationEndTrace
{
    /// <summary>The npcs in a map set whose script is a conversation, with their chunks.</summary>
    public static IEnumerable<(ChunkRecord Chunk, NpcObject Npc)> Talkers(PluginSet plugins, int mapSet) =>
        plugins.ChunksIn(RecordId.Decimal(RecordTypes.MapSet, mapSet))
            .SelectMany(c => c.Objects.OfType<NpcObject>().Select(n => (c, n)))
            .Where(x => plugins.TryGet<ScriptRecord>(x.n.Script)?.Mode == ScriptMode.Conversation);

    /// <summary>
    /// Stands Alaron 1.5 from the npc on the side <paramref name="way"/> (eighths of a turn), talks, presses on (the last choice
    /// each time) until it ends, then traces. Null where it can't be made: no body for the npc, or a talk that doesn't end.
    /// </summary>
    /// <param name="each">Called each tick after the end, with the tick's number.</param>
    public static ConversationEndResult? Run(PluginSet plugins, int mapSet, ChunkRecord chunk, NpcObject npc, int way = 0,
        Action<TrekLoop, int>? each = null, bool talk = true)
    {
        var (s, c) = MathF.SinCos(way * MathF.PI / 4);
        var dir = new Vector2(c, s);
        var local = npc.Position + new Vector3(dir.X, 0, dir.Y) * 1.5f + new Vector3(0, 0.5f, 0);
        var walk = new CameraWalk($"{chunk.Name} #{npc.Index}", mapSet, chunk.Name, local, -dir, null, chunk.Name, []);
        var trek = CameraTrace.Start(plugins, walk, setDown: true);
        trek.CameraStyle = CameraStyle.Enhanced;
        trek.DialogueCamera = CameraStyle.Enhanced;
        var start = trek.Player.Sphere.Position.Y;
        for (var i = 0; i < 30; i++)
            trek.Tick(() => new PlayerInput(Vector2.Zero));
        if (MathF.Abs(trek.Player.Sphere.Position.Y - start) > 2)
            return null;
        if (trek.Wanderers.All.FirstOrDefault(w => w.Party == npc) is not { } wanderer)
            return null;
        if (talk)
        {
            ((IMapObjectHost)trek).Talk(wanderer.Map, npc);
            if (trek.Conversation is null)
                return null;
        }
        for (var i = 0; i < 6000 && trek.Conversation is not null; i++)
        {
            trek.Tick(() => new PlayerInput(Vector2.Zero));
            if (i % 30 == 29 && trek.Conversation is { Runner.Status: ScriptStatus.Showing })
            {
                for (var d = 0; d < 8; d++)
                    trek.Press(MenuAction.Down);
                trek.Press(MenuAction.Accept);
            }
        }
        if (trek.Conversation is not null || trek.Cued is not null)
            return null;

        var away = new Vector2(trek.Player.Sphere.Position.X - wanderer.Body.Sphere.Position.X,
            trek.Player.Sphere.Position.Z - wanderer.Body.Sphere.Position.Z);
        away = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : dir;
        var turns = new List<float>();
        var last = Angle(trek.Camera.LookXZ);
        var height = trek.Player.Sphere.Position.Y;
        for (var tick = 0; tick < 360; tick++)
        {
            // Walked off a pier into the sea: the rescue's cut isn't the camera's turn.
            if (MathF.Abs(trek.Player.Sphere.Position.Y - height) > 2)
                return null;
            var look = trek.Camera.LookXZ;
            var stick = tick < 180 ? Vector2.Zero : Pilot.StickFor(away, look);
            trek.Tick(() => new PlayerInput(stick));
            each?.Invoke(trek, tick);
            var now = Angle(trek.Camera.LookXZ);
            // A room's camera object holding the view as he walks into its reach is its authored move, not the end's turn.
            if (!trek.Camera.Held && trek.Use.CurrentCamera is null)
                turns.Add(Wrap(now - last));
            last = now;
        }
        float total = 0, net = 0, peak = 0;
        var reversals = 0;
        var sense = 0;
        // A turn back counts once it has gone 3° the other way, so a settling wobble isn't one.
        float run = 0;
        foreach (var d in turns)
        {
            total += MathF.Abs(d);
            net += d;
            peak = MathF.Max(peak, MathF.Abs(d) * 60);
            run = MathF.Sign(run) == MathF.Sign(d) ? run + d : d;
            if (MathF.Abs(run) > 3 && MathF.Sign(run) != sense)
            {
                if (sense != 0)
                    reversals++;
                sense = MathF.Sign(run);
            }
        }
        return new ConversationEndResult(walk.Name, total, net, reversals, peak);
    }

    private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X) * 180 / MathF.PI;

    private static float Wrap(float degrees)
    {
        degrees %= 360;
        return degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
    }
}
