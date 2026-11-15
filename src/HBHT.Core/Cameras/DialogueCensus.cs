using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Cameras;

/// <summary>
/// Which of the enhanced conversation camera's shots fit where the data places the npcs that talk: every npc map object whose
/// script is a conversation, stood where it's placed on its chunk's collision, with Alaron at talking distance from eight ways round
/// it (where he could stand), the side of the line chosen as a conversation would choose it, and each kind of shot tried alone. It
/// loads each map set's chunks with their drawn world, as the runtime does. Other bodies nearby aren't there.
/// </summary>
public static class DialogueCensus
{
    /// <summary>One placing: the npc's chunk and index, which way Alaron stands, whether the chunk is an interior, and which shots fit.</summary>
    public sealed record Placing(string MapSet, string Chunk, int Index, int Way, bool Interior, int Side, bool ShoulderAtNpc, bool ShoulderAtPlayer,
        bool SingleOfNpc, bool SingleOfPlayer, bool TwoShot, bool Wide)
    {
        /// <summary>Which over-the-shoulder placing at the npc fitted (0 roomy, 1 snug, 2 tight, 3 and 4 nearer), or −1.</summary>
        public int ShoulderPlacing { get; init; } = -1;

        /// <summary>The share of the frame's width Alaron covers over the shoulder at the npc, and in the single of the npc, where they fit.</summary>
        public float? ShoulderCover { get; init; }
        public float? SingleCover { get; init; }

        /// <summary>Whether a single of the npc, wanted, fell to the over-the-shoulder.</summary>
        public bool SingleFellToShoulder { get; init; }

        /// <summary>
        /// Every single and punch-in placed here, of either talker: its kind and <see cref="DialogueCamera.Measure"/>'s angle round from
        /// the line, the subject's side of centre toward the listener, and the listener's cover in the widest frame; and what each that
        /// didn't fit fell to.
        /// </summary>
        public List<(DialogueShot Shot, float Angle, float Toward, float Cover)> Singles { get; init; } = [];
        public List<(DialogueShot Wanted, DialogueShotKind FellTo)> Misses { get; init; } = [];

        public bool OriginalOnly => !(ShoulderAtNpc || ShoulderAtPlayer || SingleOfNpc || SingleOfPlayer || TwoShot || Wide);

        /// <summary>How many of the six fit.</summary>
        public int Fitting => new[] { ShoulderAtNpc, ShoulderAtPlayer, SingleOfNpc, SingleOfPlayer, TwoShot, Wide }.Count(f => f);
    }

    /// <summary>Alaron's sphere.</summary>
    private const float PlayerRadius = PlayerMotion.Radius;

    /// <summary>The gaps between the spheres the census stands them at: touching, as a player runs up and presses A, and the furthest A reaches (<c>Wanderers.Reaches</c>, 0.5).</summary>
    public const float Touching = 0.05f, Furthest = 0.5f;

    /// <param name="gap">How far apart their spheres are: <see cref="Touching"/> or <see cref="Furthest"/>.</param>
    public static List<Placing> Run(PluginSet plugins, DialogueTuning tuning, float gap = Furthest)
    {
        var alaron = plugins.TryGet<EntityRecord>(GameState.Alaron)?.Body;
        var placings = new List<Placing>();
        foreach (var set in plugins.All<MapSetRecord>(RecordTypes.MapSet))
        {
            var chunks = plugins.ChunksIn(set.Id).ToList();
            var talkers = chunks.SelectMany(c => c.Objects.OfType<NpcObject>().Select(o => (Chunk: c, Npc: o)))
                .Where(x => plugins.TryGet<ScriptRecord>(x.Npc.Script)?.Mode == ScriptMode.Conversation).ToList();
            if (talkers.Count == 0)
                continue;
            var game = GameState.NewGame(plugins);
            var streamer = new ChunkStreamer(plugins, game, new CollisionWorld(), set, new VisibleWorld());
            foreach (var (chunk, npc) in talkers.OrderBy(x => x.Chunk.Row).ThenBy(x => x.Chunk.Column))
            {
                var origin = streamer.Origin(chunk);
                var at = origin + npc.Position;
                streamer.Update(at);
                var world = streamer.World;
                var interior = plugins.TryGet<TerrainRecord>(chunk.Terrain) is { SkyDrawn: false };
                var look = plugins.TryGet<EntityRecord>(npc.Entity)?.Body;
                var wants = DialoguePlanner.WantsOf(plugins.Get<ScriptRecord>(npc.Script), tuning);
                var npcRadius = 0.5f * (look?.Scale ?? 1);
                if (Ground(world, at) is not { } npcGround)
                    continue;
                var npcCentre = at with { Y = npcGround + npcRadius };
                for (var way = 0; way < 8; way++)
                {
                    var (s, c) = MathF.SinCos(way * MathF.PI / 4);
                    var dir = new Vector2(c, s);
                    var reach = npcRadius + PlayerRadius + gap;
                    var stand = npcCentre + new Vector3(dir.X, 0, dir.Y) * reach;
                    if (Ground(world, stand) is not { } ground || MathF.Abs(ground - npcGround) > 1)
                        continue;
                    var playerCentre = stand with { Y = ground + PlayerRadius };
                    if (SpherePhysics.Blocked(world, npcCentre, playerCentre, 0.3f, skipSeeThrough: true)
                        || SpherePhysics.Blocked(world, playerCentre, npcCentre, 0.3f, skipSeeThrough: true)
                        || streamer.Visible!.Raycast(npcCentre, playerCentre, out _, out _, out _))
                        continue;
                    var scene = new DialogueScene(world, streamer.Visible,
                        TalkingBody.At(playerCentre, PlayerRadius, -dir, alaron), TalkingBody.At(npcCentre, npcRadius, dir, look), []);
                    // The follow camera behind Alaron, a little round: the side is chosen mostly by the room.
                    var (bs, bc) = MathF.SinCos(MathF.PI / 9);
                    var back = new Vector2(dir.X * bc - dir.Y * bs, dir.X * bs + dir.Y * bc);
                    var side = DialogueCamera.ChooseSide(scene, playerCentre + new Vector3(back.X, 0, back.Y) * 5 + Vector3.UnitY, tuning, wants);
                    bool Fits(DialogueShotKind kind, Talker who) => DialogueCamera.TryPlace(scene, side, new DialogueShot(kind, who), tuning, out _);
                    var singles = new List<(DialogueShot, float, float, float)>();
                    var misses = new List<(DialogueShot, DialogueShotKind)>();
                    foreach (var kind in new[] { DialogueShotKind.Single, DialogueShotKind.Punch })
                    foreach (var who in new[] { Talker.Npc, Talker.Player })
                    {
                        var wanted = new DialogueShot(kind, who);
                        if (DialogueCamera.TryPlace(scene, side, wanted, tuning, out var placed))
                        {
                            var (angle, toward, cover) = DialogueCamera.Measure(scene, wanted, placed, tuning);
                            singles.Add((wanted, angle, toward, cover));
                        }
                        else
                            misses.Add((wanted, DialogueCamera.Fit(scene, side, wanted, tuning).Shown.Kind));
                    }
                    placings.Add(new Placing(set.Label ?? set.Id.ToString(), chunk.Name, npc.Index, way, interior, side,
                        Fits(DialogueShotKind.OverShoulder, Talker.Npc), Fits(DialogueShotKind.OverShoulder, Talker.Player),
                        Fits(DialogueShotKind.Single, Talker.Npc), Fits(DialogueShotKind.Single, Talker.Player),
                        Fits(DialogueShotKind.TwoShot, Talker.None), Fits(DialogueShotKind.Wide, Talker.None))
                    {
                        ShoulderPlacing = DialogueCamera.TryPlace(scene, side, new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc), tuning,
                            out var shoulder, out var placing) ? placing : -1,
                        ShoulderCover = shoulder.Distance > 0 ? DialogueCamera.ListenerCover(scene, shoulder, Talker.Player, tuning) : null,
                        SingleCover = DialogueCamera.TryPlace(scene, side, new DialogueShot(DialogueShotKind.Single, Talker.Npc), tuning, out var single)
                            ? DialogueCamera.ListenerCover(scene, single, Talker.Player, tuning) : null,
                        SingleFellToShoulder = DialogueCamera.Fit(scene, side, new DialogueShot(DialogueShotKind.Single, Talker.Npc), tuning).Shown.Kind
                            == DialogueShotKind.OverShoulder,
                        Singles = singles,
                        Misses = misses,
                    });
                }
            }
        }
        return placings;
    }

    /// <summary>The floor's height under a point, looked for from 3 above it to 6 below.</summary>
    private static float? Ground(CollisionWorld world, Vector3 at) =>
        SpherePhysics.SweepNearest(world, at + new Vector3(0, 3, 0), at - new Vector3(0, 6, 0), 0.25f, skipSeeThrough: true, minUp: 0.5f,
            out var point, out _) ? point.Y : null;

    /// <summary>The shares as the census reports them, of all placings, of interiors' and of outdoors'.</summary>
    public static (double Shoulder, double Single, double TwoShot, double Wide, double OriginalOnly) Shares(IReadOnlyCollection<Placing> placings)
    {
        double n = Math.Max(placings.Count, 1);
        return (placings.Count(p => p.ShoulderAtNpc) / n, placings.Count(p => p.SingleOfNpc) / n, placings.Count(p => p.TwoShot) / n,
            placings.Count(p => p.Wide) / n, placings.Count(p => p.OriginalOnly) / n);
    }

    public static void Report(IReadOnlyList<Placing> placings, TextWriter output)
    {
        void Line(string name, IReadOnlyCollection<Placing> of)
        {
            var (shoulder, single, two, wide, original) = Shares(of);
            output.WriteLine($"{name,-9} {of.Count,5} placings of {of.Select(p => (p.MapSet, p.Chunk, p.Index)).Distinct().Count(),4} npcs: "
                + $"over the shoulder {shoulder:P0} (both ways {of.Count(p => p.ShoulderAtNpc && p.ShoulderAtPlayer) / (double)Math.Max(of.Count, 1):P0}), "
                + $"single {single:P0}, two-shot {two:P0}, wide {wide:P0}, only the original's {original:P0}");
        }
        Line("all", placings);
        Line("interior", placings.Where(p => p.Interior).ToList());
        Line("outdoor", placings.Where(p => !p.Interior).ToList());
        static string Spread(IEnumerable<float?> covers)
        {
            var c = covers.OfType<float>().Order().ToList();
            return c.Count == 0 ? "none" : $"median {c[c.Count / 2]:P0}, 90th {c[(int)(c.Count * 0.9)]:P0} ({c.Count})";
        }
        var shoulders = placings.Where(p => p.ShoulderPlacing >= 0).ToList();
        output.WriteLine($"Alaron's width in the frame: over the shoulder {Spread(placings.Select(p => p.ShoulderCover))}, "
            + $"its roomy placing alone {Spread(placings.Where(p => p.ShoulderPlacing == 0).Select(p => p.ShoulderCover))}, "
            + $"the single {Spread(placings.Select(p => p.SingleCover))}");
        output.WriteLine($"Over the shoulder placed roomy {shoulders.Count(p => p.ShoulderPlacing == 0) / (double)Math.Max(shoulders.Count, 1):P0}, "
            + $"snug {shoulders.Count(p => p.ShoulderPlacing == 1) / (double)Math.Max(shoulders.Count, 1):P0}, "
            + $"tight {shoulders.Count(p => p.ShoulderPlacing == 2) / (double)Math.Max(shoulders.Count, 1):P0}, "
            + $"nearer {shoulders.Count(p => p.ShoulderPlacing >= 3) / (double)Math.Max(shoulders.Count, 1):P0}; "
            + $"a wanted single fell to the over-the-shoulder {placings.Count(p => p.SingleFellToShoulder) / (double)Math.Max(placings.Count, 1):P0}");
        foreach (var kind in new[] { DialogueShotKind.Single, DialogueShotKind.Punch })
        {
            var placed = placings.SelectMany(p => p.Singles).Where(x => x.Shot.Kind == kind).ToList();
            var missed = placings.SelectMany(p => p.Misses).Where(x => x.Wanted.Kind == kind).ToList();
            var tried = Math.Max(placed.Count + missed.Count, 1);
            output.WriteLine($"{kind}, either talker: fits {placed.Count / (double)tried:P0} of {tried}; "
                + $"angle round from the line median {Median(placed.Select(x => x.Angle)):0}°, most {(placed.Count > 0 ? placed.Max(x => x.Angle) : 0):0}°; "
                + $"subject toward the listener's side, most {(placed.Count > 0 ? placed.Max(x => x.Toward) : 0):0.00}; "
                + $"listener's cover at the widest frame, most {(placed.Count > 0 ? placed.Max(x => x.Cover) : 0):P0}; misses fell to "
                + string.Join(", ", missed.GroupBy(m => m.FellTo).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count() / (double)tried:P0}")));
        }
        static float Median(IEnumerable<float> v)
        {
            var c = v.Order().ToList();
            return c.Count == 0 ? 0 : c[c.Count / 2];
        }
        output.WriteLine("The ten worst placed (mean shots fitting of six, over the ways Alaron could stand):");
        foreach (var g in placings.GroupBy(p => (p.MapSet, p.Chunk, p.Index)).OrderBy(g => g.Average(p => p.Fitting)).ThenBy(g => g.Key.Chunk).Take(10))
            output.WriteLine($"  {g.Key.MapSet} {g.Key.Chunk} #{g.Key.Index}: {g.Average(p => p.Fitting):0.0} over {g.Count()} ways, "
                + $"only the original's from {g.Count(p => p.OriginalOnly)}");
    }
}
