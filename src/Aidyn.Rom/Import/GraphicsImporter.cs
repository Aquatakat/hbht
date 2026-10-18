using System.Collections.Concurrent;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Import;

/// <summary>
/// The archive's graphics: actors, every scene as glTF with its particle emitters, and the textures and UI images
/// the engine draws with.
/// </summary>
internal static class GraphicsImporter
{
    /// <summary>
    /// Alaron's actor (<c>BORG7_Alaron</c>) and the head scene the game draws on its locator 3
    /// (<c>some_borg5</c>, <c>Actor::ChangeAppearance</c>). Engine code holds both as literals; see
    /// the hardcoded-content audit.
    /// </summary>
    private const int AlaronActor = 0x25F9, AlaronHead = 0x25AA;

    public static void Import(ImportContext ctx)
    {
        var archive = ctx.Archive;

        ctx.Report("Actors");
        var actorRecords = new List<ActorRecord>();
        foreach (var index in archive.Listings.Where(l => l.Type == BorgType.Actor).Select(l => l.Index).Order())
        {
            var actor = Borg7Actor.Parse(archive.Read(index));
            var scene = Borg6Animation.Parse(archive.Read(actor.States[0].Animation)).Scene;
            actorRecords.Add(new ActorRecord(RecordId.Hex(RecordTypes.Actor, index), RecordId.Hex(RecordTypes.Scene, scene),
                actor.RootTransform, actor.InitialState,
                [.. actor.States.Select(s => new ActorState(RecordId.Hex(RecordTypes.Animation, s.Animation),
                    [.. s.Branches.Select(b => new ActorBranch(b.Request, b.Chance, b.Interrupts, b.State))]))],
                index == AlaronActor ? new ActorHead(RecordId.Hex(RecordTypes.Scene, AlaronHead), 3) : null));
        }
        ctx.WriteRecords("actors.json", actorRecords);

        ctx.Report("Scenes");
        var animations = archive.Listings.Where(l => l.Type == BorgType.Animation)
            .Select(l => (l.Index, Animation: Borg6Animation.Parse(archive.Read(l.Index))))
            .ToLookup(a => a.Animation.Scene);
        var scenes = archive.Listings.Where(l => l.Type == BorgType.Scene).Select(l => l.Index).ToList();
        var done = 0;
        var particleTextures = ctx.ParticleSlots;
        var emitters = new ConcurrentBag<EmittersRecord>();
        Parallel.ForEach(scenes, index =>
        {
            var scene = Borg5Scene.Parse(archive.Read(index));
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Scene, index), "glb",
                SceneGltf.Export(scene, archive.Read, index, animations[index].ToList()));
            if (scene.ParticleEmitters.Count > 0)
                emitters.Add(new EmittersRecord(RecordId.Hex(RecordTypes.Emitters, index), [.. scene.ParticleEmitters.Select(e => Emitter(e, particleTextures))]));
            if (Interlocked.Increment(ref done) % 200 == 0)
                ctx.Report($"Scenes {done}/{scenes.Count}");
        });

        ctx.WriteRecords("emitters.json", emitters.OrderBy(e => e.Id.Key, StringComparer.Ordinal).ToList());

        // Scenes carry their own textures. These are for what the engine draws with a texture
        // alone, such as bodies' shadows and particles. Only the full-size level is kept.
        ctx.Report("Textures");
        var textures = archive.Listings.Where(l => l.Type == BorgType.Texture).Select(l => l.Index).ToList();
        Parallel.ForEach(textures, index =>
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Texture, index), "png", Png.Encode(Borg1Texture.Parse(archive.Read(index)).Decode())));

        // UI images, and the sky gradients the scenery's colours come from.
        ctx.Report("Images");
        var images = archive.Listings.Where(l => l.Type == BorgType.UiImage).Select(l => l.Index).ToList();
        Parallel.ForEach(images, index =>
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Image, index), "png", Png.Encode(Borg8Image.Parse(archive.Read(index)).Decode())));
    }

    /// <summary>
    /// An emitter as the plugin holds it, with its texture slot looked up in the engine's particle list
    /// (<c>partTextInds</c>). An empty slot leaves it without a texture, which the original would refuse to draw.
    /// </summary>
    internal static ParticleEmitter Emitter(Borg5ParticleEmitter e, IReadOnlyList<int?> textures)
    {
        static Aidyn.Plugins.Records.Rgba Rgba(System.Numerics.Vector4 c) =>
            new((byte)MathF.Round(c.X * 255), (byte)MathF.Round(c.Y * 255), (byte)MathF.Round(c.Z * 255), (byte)MathF.Round(c.W * 255));
        static System.Numerics.Vector3 Xyz(System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z);
        var texture = e.Texture >= 0 && e.Texture < textures.Count && textures[e.Texture] is { } borg1 ? RecordId.Hex(RecordTypes.Texture, borg1) : (RecordId?)null;
        return new ParticleEmitter(
            new ParticleLocator(e.Locator.Index, e.Locator.Scene), new ParticleLocator(e.Target.Index, e.Target.Scene),
            e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest) ? e.Request : null, e.Flags,
            e.Lifetime, e.ParticleLifetime, e.Delay, e.Interval, e.Burst, texture, e.Texture, e.EmitterSpeed,
            e.Position, e.SpawnJitter, e.TargetPosition, e.TargetJitter, e.VelocityJitter,
            Xyz(e.Acceleration), e.Acceleration.W, Xyz(e.AccelerationChange), e.AccelerationChange.W,
            Xyz(e.Acceleration2), e.Acceleration2.W, Xyz(e.Acceleration2Change), e.Acceleration2Change.W,
            e.Direction, e.Speed, e.MinScale, e.MaxScale, e.EndScale, Rgba(e.MinColor), Rgba(e.MaxColor), Rgba(e.FadeColor), e.Unknown40);
    }
}
