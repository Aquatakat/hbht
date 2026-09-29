using System.Text.Json;
using Aidyn.Core.Scenes;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Scrolls textures, such as water and fire, as the original does (<see cref="TextureScroll"/>).
/// Each frame, at the port's cadence, every scene the original would draw loads its scrolling
/// textures, and each load moves the texture on. A texture is one image however many scenes use
/// it, so it moves faster the more of them are drawn, as in the original.
/// </summary>
public sealed class TextureScrolls
{
    private readonly Dictionary<int, Texture> _textures = [];
    private readonly Dictionary<RecordId, (Texture Texture, int Loads)[]> _scenes = [];

    /// <summary>
    /// Takes a scene's scrolling textures from its glTF, with the materials Godot made for them.
    /// </summary>
    public void Add(RecordId scene, JsonElement gltf, Node node)
    {
        if (!(gltf.GetProperty("scenes")[0].TryGetProperty("extras", out var extras) &&
              extras.TryGetProperty("scrollingTextures", out var list)))
            return;

        var loads = new List<(Texture, int)>();
        foreach (var entry in list.EnumerateArray())
        {
            var borg1 = entry.GetProperty("borg1").GetInt32();
            if (!_textures.TryGetValue(borg1, out var texture))
            {
                var scroll = new TextureScroll(entry.GetProperty("width").GetInt32(), entry.GetProperty("move").GetByte());
                _textures[borg1] = texture = new Texture(scroll);
            }
            loads.Add((texture, entry.GetProperty("loads").GetInt32()));
        }
        _scenes[scene] = [.. loads];

        foreach (var mesh in ChunkStreamer.FindAll<MeshInstance3D>(node))
        {
            for (var surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
            {
                if (mesh.GetSurfaceOverrideMaterial(surface) is ShaderMaterial material &&
                    material.HasMeta(N64Material.Borg1Meta) &&
                    _textures.TryGetValue(material.GetMeta(N64Material.Borg1Meta).AsInt32(), out var texture) &&
                    !texture.Materials.Contains(material))
                {
                    texture.Materials.Add(material);
                    texture.Show(material);
                }
            }
        }
    }

    /// <summary>One frame: each of <paramref name="drawn"/> loads its scrolling textures.</summary>
    public void Frame(IEnumerable<RecordId> drawn)
    {
        foreach (var scene in drawn)
        {
            if (!_scenes.TryGetValue(scene, out var loads))
                continue;
            foreach (var (texture, count) in loads)
            {
                for (var i = 0; i < count; i++)
                    texture.Scroll.Load();
            }
        }
        foreach (var texture in _textures.Values)
            texture.Update();
    }

    private sealed class Texture(TextureScroll scroll)
    {
        private int _shown;

        public TextureScroll Scroll { get; } = scroll;
        public List<ShaderMaterial> Materials { get; } = [];

        public void Update()
        {
            if (Scroll.Offset == _shown)
                return;
            _shown = Scroll.Offset;
            foreach (var material in Materials)
                Show(material);
        }

        public void Show(ShaderMaterial material) =>
            material.SetShaderParameter(N64Material.ScrollParameter, (float)Scroll.Offset / Scroll.Width);
    }
}
