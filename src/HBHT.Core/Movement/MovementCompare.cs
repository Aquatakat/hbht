using System.Collections.Concurrent;
using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Movement;

/// <summary>
/// Every cut-scene played headless under each movement, at the original's pace, and where the outcomes differ: the movement
/// comparison that <c>hbht movement compare</c> runs (docs/systems/movement.md#the-cut-scene-comparison). Each plays on its real
/// stage, the 3 × 3 chunks <see cref="ChunkStreamer"/> loads about its staging chunk in a new game, its actors moved by their
/// own animations (<see cref="ActorMotion"/>), each page read for <see cref="Options.ReadTicks"/> before A is pressed, and the
/// first choice taken.
/// </summary>
/// <remarks>
/// For each actor it compares where it ends, whether it gave up a walk and was put at its goal (<see cref="Body.PutAtGoal"/>),
/// and whether it ended off the ground or fell out of the world; for the script, how long each walk's wait took and how long
/// it ran to its end, or that it didn't end within <see cref="Options.Limit"/>.
/// </remarks>
public static class MovementCompare
{
    /// <summary>How the cut-scenes are played.</summary>
    /// <param name="ReadTicks">How long each page shows before A is pressed: two seconds, a player reading it.</param>
    /// <param name="Limit">The most ticks a cut-scene is given to end.</param>
    public sealed record Options(int ReadTicks = 120, int Limit = 100_000);

    /// <summary>
    /// A cut-scene the game starts: from a dialogue object, on its stage with its reference points numbered from its own, or from
    /// a talker's party in the chunk it stands in, numbered from 0. <paramref name="Sources"/> is how many map objects start it so.
    /// </summary>
    public sealed record Launch(RecordId Script, ScriptStart From, ChunkRef Stage, int Sources);

    /// <summary>A walk's wait: how long the script waited for its actors, and the walks or runs it waited for.</summary>
    public sealed record Walk(int Ticks, IReadOnlyList<MoveCue> Moves);

    /// <summary>
    /// Where an actor ended: how many walks it gave up and was put at the goal of, the furthest such a pop moved it, the furthest it
    /// moved across the ground in a tick otherwise if more than <see cref="JumpLeast"/> (<see cref="Jump"/>, 0 if never), and whether
    /// it ended with nothing within 0.1 below its sphere (<see cref="OffGround"/>), or nothing within 100 (<see cref="FellOut"/>).
    /// </summary>
    public sealed record ActorEnd(RecordId Entity, Vector3 Position, int PutAtGoal, float Pop, float Jump, bool OffGround, bool FellOut);

    /// <summary>The least move across the ground in a tick that counts as a jump: five times a run's.</summary>
    public const float JumpLeast = 0.5f;

    /// <summary>
    /// A cut-scene played under a movement: the ticks it ran, whether it ended within the limit and what it was waiting for if
    /// not, each actor's end in the order spawned, in the world's coordinates, and each walk's wait in order. <paramref name="Origin"/>
    /// is where its staging chunk's coordinates start; <paramref name="Waits"/> is how many ticks it spent waiting for each thing,
    /// and on the player otherwise.
    /// </summary>
    public sealed record Outcome(MovementMode Movement, Vector3 Origin, int Ticks, bool Ended, ScriptWait? Waiting, IReadOnlyList<ActorEnd> Actors,
        IReadOnlyList<Walk> Walks, IReadOnlyDictionary<ScriptWait, int> Waits, IReadOnlyList<string> Problems);

    /// <summary>What sort of difference is worst in a cut-scene's outcomes, the least first.</summary>
    public enum Severity
    {
        /// <summary>The same, within <see cref="PlaceTolerance"/> and <see cref="TimeTolerance"/>.</summary>
        Same,
        /// <summary>A walk's wait or the whole script took longer under one than the other.</summary>
        Timing,
        /// <summary>An actor ended in another place.</summary>
        Place,
        /// <summary>An actor ended off the ground under one only.</summary>
        Ground,
        /// <summary>An actor jumped across the ground in a tick under one only, without giving up a walk.</summary>
        Jump,
        /// <summary>An actor gave up a walk and popped to its goal under one only, or more often.</summary>
        GiveUp,
        /// <summary>An actor fell out of the world under one only.</summary>
        FellOut,
        /// <summary>The script ended under one only, or they waited for different things or spawned different actors.</summary>
        Ending,
    }

    /// <summary>How far apart an actor's ends may be and count as the same place.</summary>
    public const float PlaceTolerance = 0.5f;

    /// <summary>How many ticks a walk's wait or the script may differ by and count as the same time: a quarter of a second.</summary>
    public const int TimeTolerance = 15;

    /// <summary>An actor's ends under each movement.</summary>
    public sealed record ActorDifference(int Index, ActorEnd Original, ActorEnd Modern)
    {
        public float Distance => Vector3.Distance(Original.Position, Modern.Position);

        public Severity Severity =>
            Original.FellOut != Modern.FellOut ? Severity.FellOut
            : Original.PutAtGoal != Modern.PutAtGoal ? Severity.GiveUp
            : (Original.Jump > 0) != (Modern.Jump > 0) ? Severity.Jump
            : Original.OffGround != Modern.OffGround ? Severity.Ground
            : Distance > PlaceTolerance ? Severity.Place
            : Severity.Same;
    }

    /// <summary>A cut-scene's outcomes under each movement, and how they differ.</summary>
    public sealed record Difference(Launch Launch, Outcome Original, Outcome Modern)
    {
        /// <summary>Each actor spawned under both, in order.</summary>
        public IEnumerable<ActorDifference> Actors =>
            Original.Actors.Zip(Modern.Actors).Select((p, i) => new ActorDifference(i, p.First, p.Second));

        /// <summary>Each walk's wait under both, in order: Modern's ticks less the original's.</summary>
        public IEnumerable<(int Index, Walk Original, Walk Modern, int Delta)> Walks =>
            Original.Walks.Zip(Modern.Walks).Select((p, i) => (i, p.First, p.Second, p.Second.Ticks - p.First.Ticks));

        /// <summary>How much longer the script ran under Modern.</summary>
        public int TickDelta => Modern.Ticks - Original.Ticks;

        /// <summary>Whether the scripts went the same way: both ended or neither, the same actors spawned and the same walks waited for.</summary>
        public bool SameCourse => Original.Ended == Modern.Ended && Original.Actors.Count == Modern.Actors.Count
            && Original.Actors.Zip(Modern.Actors).All(p => p.First.Entity == p.Second.Entity) && Original.Walks.Count == Modern.Walks.Count;

        public Severity Severity
        {
            get
            {
                if (!SameCourse)
                    return Severity.Ending;
                var worst = Actors.Select(a => a.Severity).DefaultIfEmpty(Severity.Same).Max();
                if (worst == Severity.Same && (Math.Abs(TickDelta) > TimeTolerance || Walks.Any(w => Math.Abs(w.Delta) > TimeTolerance)))
                    return Severity.Timing;
                return worst;
            }
        }

        /// <summary>
        /// How much it differs within its <see cref="Severity"/>: the furthest an actor's ends are apart, or the furthest a pop
        /// moved it, or for timing, the seconds the walks' waits differ by in all.
        /// </summary>
        public float Magnitude => Severity switch
        {
            Severity.Ending => Math.Abs(TickDelta) / 60f,
            Severity.GiveUp => Actors.Select(a => MathF.Max(a.Original.Pop, a.Modern.Pop)).DefaultIfEmpty(0).Max(),
            Severity.Jump => Actors.Select(a => MathF.Max(a.Original.Jump, a.Modern.Jump)).DefaultIfEmpty(0).Max(),
            Severity.Timing => Walks.Sum(w => Math.Abs(w.Delta)) / 60f,
            _ => Actors.Select(a => a.Distance).DefaultIfEmpty(0).Max(),
        };
    }

    /// <summary>
    /// The cut-scenes the base game starts, as <see cref="GameSession"/> starts them: every dialogue object whose script is a
    /// cut-scene with a stage, and every talking party's, staged in the party's own chunk. Those that start the same script the same
    /// way on the same stage are one.
    /// </summary>
    public static IReadOnlyList<Launch> Launches(PluginSet plugins)
    {
        var chunkOf = plugins.All<MapSetRecord>(RecordTypes.MapSet)
            .SelectMany(s => s.Chunks.Select(c => (c.Map, Ref: new ChunkRef(s.Id, c.Row, c.Column))))
            .GroupBy(c => c.Map).ToDictionary(g => g.Key, g => g.First().Ref);
        var found = new List<(RecordId Script, ScriptStart From, ChunkRef Stage)>();
        foreach (var map in plugins.All<MapRecord>(RecordTypes.Map))
        {
            foreach (var o in map.Objects)
            {
                var source = MapRecord.VoxelId(map.Id, o.Index);
                if (o is DialogueObject { Stage: { } stage } d && Mode(plugins, d.Script) == DialogueMode.Cutscene)
                    found.Add((d.Script, new ScriptStart(d.RefPointBase, source), stage));
                else if (o is MonsterPartyObject { Dialogue: { } script } p && (p.Flags & PartyFlags.Silent) == 0
                    && Mode(plugins, script) == DialogueMode.Cutscene && chunkOf.TryGetValue(map.Id, out var own))
                    found.Add((script, new ScriptStart(0, source), own));
            }
        }
        return [.. found.GroupBy(f => (f.Script, f.From.RefPoint, f.Stage))
            .Select(g => new Launch(g.Key.Script, g.First().From, g.Key.Stage, g.Count()))
            .OrderBy(l => l.Script.ToString(), StringComparer.Ordinal).ThenBy(l => l.From.RefPoint)];
    }

    private static DialogueMode? Mode(PluginSet plugins, RecordId script) => plugins.TryGet<DialogueRecord>(script)?.Mode;

    /// <summary>
    /// Plays every launch under both movements, in parallel by stage, and returns each one's outcomes, those that differ worst
    /// first; <paramref name="skipped"/> gets those that couldn't be played, with why.
    /// </summary>
    public static IReadOnlyList<Difference> Run(PluginSet plugins, IEnumerable<Launch> launches, Options options, out IReadOnlyList<string> skipped)
    {
        var clips = new ConcurrentDictionary<RecordId, Lazy<ActorClips?>>();
        var results = new ConcurrentBag<Difference>();
        var missed = new ConcurrentBag<string>();
        var byStage = launches.GroupBy(l => l.Stage).ToList();
        Parallel.ForEach(byStage, group =>
        {
            if (Stage(plugins, group.Key) is not { } stage)
            {
                foreach (var launch in group)
                    missed.Add($"{launch.Script}: its stage {group.Key.MapSet} ({group.Key.Row}, {group.Key.Column}) isn't in any plugin.");
                return;
            }
            foreach (var launch in group)
            {
                if (plugins.TryGet<DialogueRecord>(launch.Script) is not { } script)
                {
                    missed.Add($"{launch.Script}: no plugin has it.");
                    continue;
                }
                var original = Play(plugins, script, launch.From, stage, MovementMode.Original, options, clips);
                var modern = Play(plugins, script, launch.From, stage, MovementMode.Modern, options, clips);
                results.Add(new Difference(launch, original, modern));
            }
        });
        skipped = [.. missed.Order(StringComparer.Ordinal)];
        return Rank(results);
    }

    /// <summary>The differences worst first: by <see cref="Difference.Severity"/>, then <see cref="Difference.Magnitude"/>, then by script.</summary>
    public static IReadOnlyList<Difference> Rank(IEnumerable<Difference> differences) =>
        [.. differences.OrderByDescending(d => d.Severity).ThenByDescending(d => d.Magnitude)
            .ThenBy(d => d.Launch.Script.ToString(), StringComparer.Ordinal).ThenBy(d => d.Launch.From.RefPoint)];

    /// <summary>A chunk as a stage, with the 3 × 3 about it loaded as <see cref="GameSession"/> loads it, in a new game.</summary>
    public static CutSceneStage? Stage(PluginSet plugins, ChunkRef chunk)
    {
        if (plugins.TryGet<MapSetRecord>(chunk.MapSet) is not { } set)
            return null;
        var world = new CollisionWorld();
        if (CutSceneStage.For(plugins, chunk, world) is not { } stage)
            return null;
        var streamer = new ChunkStreamer(plugins, GameState.NewGame(plugins), world, set);
        streamer.Update(stage.Origin + new Vector3(set.CellSize / 2, 0, set.CellSize / 2));
        return stage;
    }

    /// <summary>Plays a cut-scene on its stage under a movement, from a new game, till it ends or the limit.</summary>
    /// <param name="watch">Called after each tick with the tick and the cut-scene, for following one.</param>
    public static Outcome Play(PluginSet plugins, DialogueRecord script, ScriptStart from, CutSceneStage stage, MovementMode movement,
        Options options, ConcurrentDictionary<RecordId, Lazy<ActorClips?>>? clips = null, Action<int, CutScene>? watch = null)
    {
        clips ??= new();
        var log = new MoveLog();
        var scene = new CutScene(GameState.NewGame(plugins), script, from, stage, log) { Movement = movement };
        var motions = new Dictionary<CutSceneActor, IActorMotion>();
        IActorMotion Motion(CutSceneActor actor)
        {
            if (!motions.TryGetValue(actor, out var motion))
            {
                var record = plugins.TryGet<ActorRecord>(actor.Look.Actor);
                var actorClips = record is null ? null
                    : clips.GetOrAdd(record.Id, _ => new Lazy<ActorClips?>(() => ActorClips.For(plugins, record.Scene, record.RootTransform))).Value;
                motions[actor] = motion = record is not null && actorClips is { Animated: true }
                    ? new ActorMotion(record, actorClips, new Lcg(1))
                    : IActorMotion.Still;
            }
            return motion;
        }

        var walks = new List<Walk>();
        var waits = new SortedDictionary<ScriptWait, int>();
        var pops = new Dictionary<CutSceneActor, float>();
        var jumps = new Dictionary<CutSceneActor, float>();
        int? waitFrom = null;
        List<MoveCue> waitedFor = [];
        var (shown, since) = (-1, 0);
        scene.Start();
        var tick = 0;
        for (; !scene.Over && tick < options.Limit; tick++)
        {
            var wait = scene.Runner.Wait ?? ScriptWait.Player;
            waits[wait] = waits.GetValueOrDefault(wait) + 1;
            var waiting = wait == ScriptWait.Move;
            if (waiting && waitFrom is null)
            {
                waitFrom = tick;
                waitedFor = [.. log.Moves];
                log.Moves.Clear();
            }
            else if (!waiting && waitFrom is { } began)
            {
                walks.Add(new Walk(tick - began, waitedFor));
                waitFrom = null;
            }

            if (scene.Accepts && scene.Page is { } page)
            {
                (shown, since) = page.Line.Node == shown ? (shown, since + 1) : (page.Line.Node, 0);
                if (since >= options.ReadTicks)
                {
                    if (page.Choices.Count > 0 && scene.Runner.Wait != ScriptWait.Player)
                        scene.Press(() => scene.Runner.Choose(0));
                    else
                        scene.Press(scene.Runner.Continue);
                    shown = -1;
                }
            }

            var before = scene.Actors.Select(a => (a, a.Body.PutAtGoal, a.Body.Sphere.Position)).ToList();
            scene.Tick(Motion);
            watch?.Invoke(tick, scene);
            foreach (var (actor, count, at) in before)
            {
                var now = actor.Body.Sphere.Position;
                if (actor.Body.PutAtGoal != count)
                    pops[actor] = MathF.Max(pops.GetValueOrDefault(actor), Vector3.Distance(at, now));
                else if (new Vector2(now.X - at.X, now.Z - at.Z).Length() is var across && across > JumpLeast)
                    jumps[actor] = MathF.Max(jumps.GetValueOrDefault(actor), across);
            }
        }
        if (waitFrom is { } open)
            walks.Add(new Walk(tick - open, waitedFor));

        var ends = scene.Actors.Select(a =>
        {
            var at = a.Body.Sphere.Position;
            var radius = a.Body.Sphere.Radius;
            var fellOut = !Below(stage.World, at, radius, 100);
            var offGround = fellOut || !Below(stage.World, at, radius, 0.1f);
            return new ActorEnd(a.Entity, at, a.Body.PutAtGoal, pops.GetValueOrDefault(a), jumps.GetValueOrDefault(a), offGround, fellOut);
        }).ToList();
        return new Outcome(movement, stage.Origin, tick, scene.Over, scene.Over ? null : scene.Runner.Wait, ends, walks, waits, [.. scene.Problems.Distinct()]);
    }

    /// <summary>
    /// Whether anything, wall or ground, is within <paramref name="depth"/> below a sphere, judged alike under either movement. The
    /// cast starts <see cref="Lift"/> above it, so a sphere a little into the ground, as the original's sinks between its frames'
    /// collisions, still finds it.
    /// </summary>
    private static bool Below(CollisionWorld world, Vector3 at, float radius, float depth) =>
        SphereCast.Cast(world, at + new Vector3(0, Lift, 0), new Vector3(0, -depth - Lift, 0), radius, false, _ => true, out _);

    private const float Lift = 0.25f;

    /// <summary>The walks and runs the script asks for, in order, till a walk's wait takes them.</summary>
    private sealed class MoveLog : IScriptHost
    {
        public List<MoveCue> Moves { get; } = [];

        public void Cue(ScriptCue cue)
        {
            if (cue is MoveCue move)
                Moves.Add(move);
        }
    }
}
