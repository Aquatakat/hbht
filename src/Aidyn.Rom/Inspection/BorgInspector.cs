using System.Globalization;
using System.Text;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Inspection;

/// <summary>A 3D preview: a glTF binary, and the animation to play in it, if any.</summary>
/// <param name="Note">What the preview shows, when that isn't just the Borg itself.</param>
public sealed record ModelPreview(byte[] Glb, string? Animation, string? Note);

/// <summary>
/// Everything the asset inspector (roadmap 0.6) shows about a Borg, without any Godot code: the
/// decoded record, a one-line summary, its field tree, a hex dump and a preview. Previews that need
/// other Borgs, such as an animation's scene or a map chunk's placement, are resolved here.
/// </summary>
/// <remarks>
/// The inspector is a research tool, so unlike the runtime it reads the ROM directly (ADR 0001
/// covers the runtime and the importer's output).
/// </remarks>
public sealed class BorgInspector
{
    private readonly Lazy<ILookup<int, (int Index, Borg6Animation Animation)>> _animationsByScene;
    private readonly Lazy<IReadOnlyList<MapChunk>> _chunks;
    private readonly Lazy<Dictionary<ItemId, string>> _names;

    public BorgInspector(RomImage rom)
    {
        Rom = rom;
        Archive = BorgArchive.Open(rom);
        _animationsByScene = new(() => Archive.Listings.Where(l => l.Type == BorgType.Animation)
            .Select(l => (l.Index, Animation: Borg6Animation.Parse(Archive.Read(l.Index))))
            .ToLookup(a => a.Animation.Scene));
        _chunks = new(() => MapChunks.Read(rom));
        _names = new(() =>
        {
            var db = GameDb.Read(rom);
            return db.Entities.Select(e => (e.Id, e.Name)).Concat(db.DialogEntities.Select(e => (e.Id, e.Name)))
                .ToDictionary(e => e.Id, e => e.Name);
        });
    }

    public RomImage Rom { get; }
    public BorgArchive Archive { get; }

    /// <summary>The types with a decoder. Samples and sequences (roadmap 1D) are only bytes so far.</summary>
    public static bool CanDecode(BorgType type) => type is >= BorgType.Texture and <= BorgType.MapChunk or BorgType.Dialogue;

    /// <summary>The Borg parsed by its type's reader, or null for a type with none.</summary>
    public object? Decode(int index) => Decode(Archive.Listings[index].Type, Archive.Read(index));

    public static object? Decode(BorgType type, byte[] data) => type switch
    {
        BorgType.Texture => Borg1Texture.Parse(data),
        BorgType.Geometry => Borg2Geometry.Parse(data),
        BorgType.Camera => Borg3Camera.Parse(data),
        BorgType.Light => Borg4Light.Parse(data),
        BorgType.Scene => Borg5Scene.Parse(data),
        BorgType.Animation => Borg6Animation.Parse(data),
        BorgType.Actor => Borg7Actor.Parse(data),
        BorgType.UiImage => Borg8Image.Parse(data),
        BorgType.MapChunk => Borg9Map.Parse(data),
        BorgType.Dialogue => Borg13Script.Parse(data),
        _ => null,
    };

    /// <summary>A short description of a decoded Borg, such as a texture's format and size.</summary>
    public static string Summary(object? decoded) => decoded switch
    {
        Borg1Texture t => $"{t.Format} {t.Width}×{t.Height}" + (t.Lods > 0 ? $", {t.Lods} mip levels" : ""),
        Borg2Geometry g => $"{Count(g.Meshes.Count, "display list")}, {Count(g.Meshes.Sum(m => m.TriangleCount), "triangle")}" +
                           (g.Locator is { } l ? $", locator {l}" : ""),
        Borg3Camera c => Invariant($"{c.FieldOfViewY}°, near {c.Near}, far {c.Far}"),
        Borg4Light l => Invariant($"RGB ({l.Red}, {l.Green}, {l.Blue})"),
        Borg5Scene s => $"{Count(s.Models.Count, "model")}, {Count(s.Draws.Count, "draw")}, {Count(s.Lights.Count, "light")}" +
                        (s.ParticleEmitters.Count > 0 ? $", {Count(s.ParticleEmitters.Count, "emitter")}" : ""),
        Borg6Animation a => $"scene {a.Scene}, {Count(a.Length, "tick")}, {Count(a.Targets.Count, "target")}",
        Borg7Actor a => $"{Count(a.States.Count, "state")}, {Count(a.States.Sum(s => s.Branches.Length), "branch", "branches")}",
        Borg8Image i => $"{i.Format} {i.Width}×{i.Height}",
        Borg9Map m => $"{Count(m.Triangles.Count, "triangle")}, {Count(m.Objects.Count, "object")}, {m.Columns}×{m.Rows} cells",
        Borg13Script d => $"{d.StartMode}, {Count(d.Nodes.Count, "node")}",
        _ => "",
    };

    public FieldNode Fields(int index) => FieldTree.Of($"borg {index}", Decode(index));

    /// <summary>The Borg's archive entry, as a field tree.</summary>
    public FieldNode Listing(int index) => FieldTree.Of("listing", Archive.Listings[index]);

    /// <summary>The decompressed bytes as a hex dump: 16 bytes a line, with offsets and ASCII.</summary>
    public static string Hex(ReadOnlySpan<byte> data)
    {
        var s = new StringBuilder(data.Length / 16 * 78 + 78);
        for (var line = 0; line < data.Length; line += 16)
        {
            var row = data[line..Math.Min(line + 16, data.Length)];
            s.Append(CultureInfo.InvariantCulture, $"{line:X6}  ");
            for (var i = 0; i < 16; i++)
            {
                if (i == 8)
                    s.Append(' ');
                s.Append(i < row.Length ? row[i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
            }
            s.Append(' ');
            foreach (var b in row)
                s.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            s.Append('\n');
        }
        return s.ToString();
    }

    /// <summary>A texture's mip levels, largest first, or a UI image. Null for other types.</summary>
    public IReadOnlyList<RgbaImage>? Images(int index) => Decode(index) switch
    {
        Borg1Texture t => [.. Enumerable.Range(0, t.LevelCount).Select(t.Decode)],
        Borg8Image i => [i.Decode()],
        _ => null,
    };

    /// <summary>A dialogue script as text. Null for other types.</summary>
    public string? Text(int index) => Decode(index) switch
    {
        Borg13Script script => Borg13Text.Format(script, index, _names.Value.GetValueOrDefault),
        _ => null,
    };

    /// <summary>
    /// A 3D preview. A model is shown alone, untextured; a scene with all its animations; an
    /// animation or actor in the scene it animates; and a map chunk's collision in each chunk that
    /// places it. Null for other types.
    /// </summary>
    public ModelPreview? Model(int index)
    {
        switch (Decode(index))
        {
            case Borg2Geometry model:
                return new(SceneGltf.ExportModel(model, index), null, "Untextured: scenes bind the textures.");
            case Borg5Scene scene:
            {
                var animations = _animationsByScene.Value[index].ToList();
                var first = animations.FirstOrDefault(a => a.Animation.Targets.Count > 0);
                return new(SceneGltf.Export(scene, Archive.Read, index, animations),
                    first.Animation is null ? null : AnimationName(first.Index), null);
            }
            case Borg6Animation animation:
                return new(SceneGltf.Export(Borg5Scene.Parse(Archive.Read(animation.Scene)), Archive.Read, animation.Scene, [(index, animation)]),
                    animation.Targets.Count > 0 ? AnimationName(index) : null,
                    $"In scene {animation.Scene}." + (animation.Targets.Count == 0 ? " It has no targets, so it holds the scene still." : ""));
            case Borg7Actor actor:
            {
                var initial = actor.States[actor.InitialState].Animation;
                var sceneIndex = Borg6Animation.Parse(Archive.Read(initial)).Scene;
                var used = actor.States.Select(s => s.Animation).ToHashSet();
                var animations = _animationsByScene.Value[sceneIndex].Where(a => used.Contains(a.Index)).ToList();
                return new(SceneGltf.Export(Borg5Scene.Parse(Archive.Read(sceneIndex)), Archive.Read, sceneIndex, animations),
                    animations.Any(a => a.Index == initial && a.Animation.Targets.Count > 0) ? AnimationName(initial) : null,
                    $"In scene {sceneIndex}, playing its initial state's animation (borg6 {initial}).");
            }
            case Borg9Map map:
            {
                // Shown where its first placement puts it, or at the origin if nothing places it.
                var placed = _chunks.Value.Where(c => c.Collision == index).ToList();
                var chunk = placed.FirstOrDefault() ?? new MapChunk(MapSet.Overworld, 0, 0, -1, -1, index, 0);
                var note = placed.Count > 0
                    ? $"Collision, placed as {placed[0]}" + (placed.Count > 1 ? $" (also {string.Join(", ", placed.Skip(1))})." : ".")
                    : "Collision. No map set places this chunk, so it's shown at the origin.";
                return new(CollisionGltf.Export([(chunk, map)]), null, note);
            }
            default:
                return null;
        }
    }

    /// <summary>The name <see cref="SceneGltf"/> gives a Borg6's glTF animation.</summary>
    public static string AnimationName(int borg6) => $"borg6 {borg6}";

    private static string Count(int n, string one, string? many = null) =>
        string.Create(CultureInfo.InvariantCulture, $"{n:N0} {(n == 1 ? one : many ?? one + "s")}");

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
