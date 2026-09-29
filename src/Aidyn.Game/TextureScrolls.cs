using System.Text.Json;
using Aidyn.Core.Scenes;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Scrolls textures, such as water and fire, as the original does (<see cref="TextureScroll"/>).
/// </summary>
/// <remarks>
/// The original loads every scene it places as a copy of its own (<c>BorgAnimLoadScene</c> sets
/// <c>borgFlag</c>), and a texture that scrolls leaves the Borg cache as it's loaded
/// (<c>InitBorgTexture</c>, for flag 0x100), so each copy of a scene has its own copy of each of its
/// scrolling textures. That copy moves each time its scene is drawn, and only then. So each copy
/// of a scene here gets its own materials for them, which <see cref="ScrollingScene.Draw"/> moves.
/// </remarks>
public sealed class TextureScrolls
{
    private readonly Dictionary<RecordId, Spec[]> _scenes = [];

    /// <summary>Takes a scene's scrolling textures from its glTF.</summary>
    public void Add(RecordId scene, JsonElement gltf)
    {
        if (gltf.GetProperty("scenes")[0].TryGetProperty("extras", out var extras) &&
            extras.TryGetProperty("scrollingTextures", out var list))
        {
            _scenes[scene] = [.. list.EnumerateArray().Select(e => new Spec(e.GetProperty("borg1").GetInt32(),
                e.GetProperty("width").GetInt32(), e.GetProperty("move").GetByte(), e.GetProperty("loads").GetInt32()))];
        }
    }

    /// <summary>
    /// Gives a copy of a scene its own scrolling textures, as the original gives each scene it
    /// loads. Null if the scene has none.
    /// </summary>
    public ScrollingScene? Attach(RecordId scene, Node instance)
    {
        if (!_scenes.TryGetValue(scene, out var specs))
            return null;
        var textures = specs.Select(s => new Texture(new TextureScroll(s.Width, s.Move), s.Loads)).ToArray();
        var copies = new Dictionary<Material, ShaderMaterial>();
        foreach (var mesh in ChunkStreamer.FindAll<MeshInstance3D>(instance))
        {
            for (var surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
            {
                if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial material || !material.HasMeta(N64Material.Borg1Meta))
                    continue;
                var borg1 = material.GetMeta(N64Material.Borg1Meta).AsInt32();
                var i = Array.FindIndex(specs, s => s.Borg1 == borg1);
                if (i < 0)
                    continue;
                if (!copies.TryGetValue(material, out var copy))
                {
                    copies[material] = copy = (ShaderMaterial)material.Duplicate();
                    textures[i].Materials.Add(copy);
                }
                mesh.SetSurfaceOverrideMaterial(surface, copy);
            }
        }
        return new ScrollingScene(textures);
    }

    private readonly record struct Spec(int Borg1, int Width, byte Move, int Loads);

    internal sealed class Texture(TextureScroll scroll, int loads)
    {
        private int _shown;

        public TextureScroll Scroll { get; } = scroll;
        public int Loads { get; } = loads;
        public List<ShaderMaterial> Materials { get; } = [];

        public void Update()
        {
            if (Scroll.Offset == _shown)
                return;
            _shown = Scroll.Offset;
            foreach (var material in Materials)
                material.SetShaderParameter(N64Material.ScrollParameter, (float)Scroll.Offset / Scroll.Width);
        }
    }
}

/// <summary>A copy of a scene with its own scrolling textures.</summary>
public sealed class ScrollingScene
{
    private readonly TextureScrolls.Texture[] _textures;

    internal ScrollingScene(TextureScrolls.Texture[] textures) => _textures = textures;

    /// <summary>One draw of the scene, which loads its scrolling textures and so moves them on.</summary>
    public void Draw()
    {
        foreach (var texture in _textures)
        {
            for (var i = 0; i < texture.Loads; i++)
                texture.Scroll.Load();
            texture.Update();
        }
    }
}
