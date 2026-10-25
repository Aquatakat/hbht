using HBHT.Core;
using HBHT.Core.Particles;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Draws the engine's particles (<see cref="ParticleHead"/>), as <c>ParticleHead.Draw</c> (<c>0x800a92fc</c>) does: each
/// a square facing the camera, its texture once across it, times its vertex colour in RGB and alpha
/// (<c>G_CC_MODULATERGBA</c>), blended normally over what's drawn, depth tested and not written
/// (<c>G_RM_ZB_XLU_SURF</c>, <c>0x504A50</c>), filtered bilinearly and clamped, unlit and unfogged, after everything else in
/// the frame. Each emitter slot is a multimesh drawn in slot order, its particles newest first, as the original draws
/// them, unsorted.
/// </summary>
/// <remarks>
/// Its owner steps <see cref="Head"/> by the tick with <see cref="Step"/>. A frame of particles runs every
/// <see cref="Cadence.FrameTicks"/> ticks, and between frames the view moves each sprite on from where the last frame drew
/// it, so they glide where the original's step. The editor draws with it too.
/// </remarks>
public partial class ParticlesView : Node3D
{
    /// <summary>The render priority of slot 0's particles, which puts them after every other translucent draw.</summary>
    private const int Priority = 100;

    /// <summary>Bodies' visual layer (<c>ShadowDecal.BodyLayer</c>), which the shadow's decal leaves out.</summary>
    private const int BodyLayer = 2;

    private static readonly Shader Shader = new()
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, skip_vertex_transform;

            // Raw texels, which are on-screen values: no source_color.
            uniform sampler2D particle_texture : filter_linear, repeat_disable;
            uniform bool on_screen = false;

            varying vec4 shade;

            vec3 to_linear(vec3 c) {
            	return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
            }

            void vertex() {
            	// Turned to face along the camera's look, upright (guAlign with the look), and sized by the instance's
            	// scale; or, flagged by the instance's red, turned by the instance's own transform, as a particle with its
            	// own matrix is.
            	vec3 world;
            	if (COLOR.r > 0.5) {
            		world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            	} else {
            		vec3 centre = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
            		vec2 size = vec2(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz));
            		world = centre + INV_VIEW_MATRIX[0].xyz * VERTEX.x * size.x + INV_VIEW_MATRIX[1].xyz * VERTEX.y * size.y;
            	}
            	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
            	shade = INSTANCE_CUSTOM;
            }

            void fragment() {
            	// The quad's top takes the texture's last row (t = H there), so the image shows upside down.
            	vec4 texel = texture(particle_texture, vec2(UV.x, 1.0 - UV.y));
            	vec3 color = texel.rgb * shade.rgb;
            	ALBEDO = on_screen ? color : to_linear(color);
            	ALPHA = texel.a * shade.a;
            }
            """,
    };

    private readonly PluginSet _plugins;
    private readonly Dictionary<RecordId, Texture2D?> _textures = [];
    private readonly MultiMeshInstance3D[] _slots = new MultiMeshInstance3D[ParticleHead.MaxEmitters];
    private readonly RecordId?[] _slotTextures = new RecordId?[ParticleHead.MaxEmitters];

    public ParticlesView(PluginSet plugins, ParticleHead? head = null)
    {
        _plugins = plugins;
        Head = head ?? new ParticleHead();
        Name = "Particles";
        var quad = new QuadMesh { Size = Vector2.One };
        for (var i = 0; i < _slots.Length; i++)
        {
            var material = new ShaderMaterial { Shader = Shader, RenderPriority = Priority + i };
            material.SetShaderParameter("on_screen", N64Material.OnScreenFramebuffer);
            _slots[i] = new MultiMeshInstance3D
            {
                Name = $"Emitter {i}",
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = quad },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // Bodies' layer, which the shadow's decal leaves out.
                Layers = 1u << (BodyLayer - 1),
                CustomAabb = new Aabb(new Vector3(-1e5f, -1e5f, -1e5f), new Vector3(2e5f, 2e5f, 2e5f)),
                // The view moves the sprites on itself, every frame drawn. With Godot's physics interpolation, a
                // multimesh blends each instance from where the last physics tick left that index, and an index is a
                // different particle as soon as one is born or dies, so between ticks splashes showed for a frame
                // where others had been and vanished where they were.
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(_slots[i]);
        }
    }

    public ParticlesView() : this(PluginSet.Load([]))
    {
    }

    public ParticleHead Head { get; }

    /// <summary>
    /// How far into the tick the frame is drawn, from 0 to 1, for interpolating. Null takes Godot's physics
    /// interpolation, as the runtime steps on the physics tick.
    /// </summary>
    public Func<double>? TickFraction { get; set; }

    /// <summary>A tick of the particles.</summary>
    public void Step() => Head.Step();

    public override void _Process(double delta)
    {
        var sprites = Head.Sprites;
        var fraction = (float)((Head.TicksSinceFrame + (TickFraction?.Invoke() ?? Engine.GetPhysicsInterpolationFraction())) / Cadence.FrameTicks);
        fraction = Mathf.Clamp(fraction, 0, 1);
        var start = 0;
        for (var slot = 0; slot < _slots.Length; slot++)
        {
            var end = start;
            while (end < sprites.Count && sprites[end].Emitter == slot)
                end++;
            var multimesh = _slots[slot].Multimesh;
            if (end > start && sprites[start].Texture is { } texture && Texture(texture) is { } image)
            {
                if (_slotTextures[slot] != texture)
                {
                    _slotTextures[slot] = texture;
                    ((ShaderMaterial)_slots[slot].MaterialOverride).SetShaderParameter("particle_texture", image);
                }
                if (multimesh.InstanceCount < end - start)
                    multimesh.InstanceCount = Math.Max(end - start, multimesh.InstanceCount * 2);
                multimesh.VisibleInstanceCount = end - start;
                for (var i = start; i < end; i++)
                {
                    var s = sprites[i];
                    var at = s.Previous.ToGodot().Lerp(s.Position.ToGodot(), fraction);
                    var scale = Basis.FromScale(new Vector3(s.Size.X, s.Size.Y, 1));
                    var turned = s.Orientation.HasValue;
                    var q = s.Orientation.GetValueOrDefault();
                    var basis = turned ? new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * scale : scale;
                    multimesh.SetInstanceTransform(i - start, new Transform3D(basis, at));
                    multimesh.SetInstanceColor(i - start, turned ? Colors.White : Colors.Black);
                    multimesh.SetInstanceCustomData(i - start, new Color(s.Color.R / 255f, s.Color.G / 255f, s.Color.B / 255f, s.Color.A / 255f));
                }
            }
            else
            {
                multimesh.VisibleInstanceCount = 0;
            }
            start = end;
        }
    }

    /// <summary>
    /// An actor's emitters, if its scene has any, attached to its instance's locators, and to <paramref name="owner"/>'s and
    /// <paramref name="other"/>'s for those that name an attached scene (locator bits 7 and 8: a spell's visual's).
    /// </summary>
    public ActorParticles? For(ActorRecord actor, Node3D instance, IParticleAnchor? owner = null, IParticleAnchor? other = null) =>
        _plugins.TryGet<EmittersRecord>(RecordId.Hex(RecordTypes.Emitters, Convert.ToInt32(actor.Scene.Key, 16))) is { } emitters
            ? new ActorParticles(Head, emitters, new NodeAnchor(instance, owner, other))
            : null;

    /// <summary>An actor instance's locators, which another actor's emitters can be attached to.</summary>
    public static IParticleAnchor Anchor(Node3D instance) => new NodeAnchor(instance);

    private Texture2D? Texture(RecordId id)
    {
        if (_textures.TryGetValue(id, out var texture))
            return texture;
        if (_plugins.FindAsset(id, "png") is { } png)
            texture = Png.Texture(png);
        else
        {
            GD.PushWarning($"No texture {id} for particles.");
        }
        return _textures[id] = texture;
    }

    /// <summary>
    /// An actor's instance, whose locator nodes (<see cref="SceneNames.Locator"/>, which the importer puts on each locator's transform)
    /// place its emitters as it's drawn; and the scenes attached to it at run time, a spell's visual's owner (1, the scene's
    /// <c>+0x2A4</c>) and the other body of its cast (2, <c>+0x2A8</c>), whose own locators of the same number place it
    /// (<c>Particles.AttachedScene</c>, <c>0x800A9828</c>). A locator a scene doesn't have, or a body that has gone, ends the emitter.
    /// </summary>
    private sealed class NodeAnchor(Node3D root, IParticleAnchor? owner = null, IParticleAnchor? other = null) : IParticleAnchor
    {
        private readonly Node3D?[] _locators = Locators(root);

        public bool Alive => IsInstanceValid(root) && root.IsInsideTree();

        public bool TryLocator(ParticleLocator locator, out System.Numerics.Matrix4x4 world)
        {
            world = default;
            if (locator.Scene is 1 or 2)
            {
                var attached = locator.Scene == 1 ? owner : other;
                return attached is { Alive: true } && attached.TryLocator(locator with { Scene = 0 }, out world);
            }
            if (locator.Scene != 0 || locator.Index is < 0 or > 7 || _locators[locator.Index] is not { } node || !IsInstanceValid(node))
                return false;
            var t = node.GlobalTransform;
            world = new System.Numerics.Matrix4x4(
                t.Basis.Column0.X, t.Basis.Column0.Y, t.Basis.Column0.Z, 0,
                t.Basis.Column1.X, t.Basis.Column1.Y, t.Basis.Column1.Z, 0,
                t.Basis.Column2.X, t.Basis.Column2.Y, t.Basis.Column2.Z, 0,
                t.Origin.X, t.Origin.Y, t.Origin.Z, 1);
            return true;
        }

        private static Node3D?[] Locators(Node3D root)
        {
            var locators = new Node3D?[8];
            foreach (var node in Nodes.FindAll<Node3D>(root))
            {
                if (SceneNames.TryLocator(node.Name.ToString(), out var i) && i is >= 0 and < 8)
                    locators[i] ??= node;
            }
            return locators;
        }
    }
}
