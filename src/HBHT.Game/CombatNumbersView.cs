using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws the numbers over heads (<see cref="CombatNumbers"/>, <c>CombatStatIndicator.Frame</c>, <c>0x8008A630</c>): each a quad
/// facing the camera, its texture the number's digits put together from the ten digit textures into 64 × 16, centred, tinted
/// yellowish white for damage and blue for healing, and rising, growing and fading as the core says. Filtered bilinearly, as
/// the original's are (docs/systems/combat.md, the numbers over heads).
/// </summary>
public partial class CombatNumbersView : Node3D
{
    private readonly Image?[] _digits = new Image?[10];
    private readonly Dictionary<CombatNumber, (MeshInstance3D Mesh, StandardMaterial3D Material)> _shown = [];

    public CombatNumbersView(PluginSet plugins)
    {
        Name = "Numbers";
        for (var d = 0; d < 10; d++)
        {
            if (plugins.FindAsset(RecordId.Hex(RecordTypes.Texture, CombatNumbers.DigitTextures[d]), "png") is { } path && Png.Load(path) is { } image)
            {
                // The digit glyphs are upside down (docs/formats/textures.md).
                image.FlipY();
                image.Convert(Image.Format.Rgba8);
                _digits[d] = image;
            }
        }
    }

    /// <summary>A number starts showing (<see cref="NumberShown"/>).</summary>
    public void Show(CombatNumber number) => _shown.TryAdd(number, Make(number));

    /// <summary>A number has gone (<see cref="NumberGone"/>).</summary>
    public void Remove(CombatNumber number)
    {
        if (_shown.Remove(number, out var drawn))
            drawn.Mesh.QueueFree();
    }

    /// <summary>Draws the numbers showing, seen from a camera at <paramref name="eye"/>.</summary>
    public void Sync(System.Numerics.Vector3 eye)
    {
        foreach (var (number, drawn) in _shown)
        {
            var (y, width, height) = number.Place(number.Scale(eye));
            drawn.Mesh.Position = new System.Numerics.Vector3(number.At.X, y, number.At.Z).ToGodot();
            ((QuadMesh)drawn.Mesh.Mesh).Size = new Vector2(Math.Max(width, 0.01f), Math.Max(height, 0.01f));
            var c = number.Color;
            drawn.Material.AlbedoColor = Color.Color8(c.R, c.G, c.B, c.A);
        }
    }

    private (MeshInstance3D, StandardMaterial3D) Make(CombatNumber number)
    {
        var image = Image.CreateEmpty(64, 16, false, Image.Format.Rgba8);
        var digits = number.Digits;
        for (var k = 0; k < digits.Length; k++)
        {
            if (_digits[digits[k] - '0'] is { } digit)
                image.BlitRect(digit, new Rect2I(0, 0, 16, 16), new Vector2I(32 - 8 * digits.Length + 16 * k, 0));
        }
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            // Bilinear with no mip levels and clamped, as Plane.SetDrawState's state list (0x800DFCC0, G_TF_BILERP) and
            // Scene.SetTextureTiles (G_TL_TILE, T clamped; S wraps, but the texture's first and last columns are always clear).
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            TextureRepeat = false,
            // G_RM_ZB_XLU_SURF: tested against what's drawn, not written, and drawn after the world and its bodies.
            RenderPriority = 10,
        };
        var mesh = new MeshInstance3D { Mesh = new QuadMesh(), MaterialOverride = material };
        AddChild(mesh);
        return (mesh, material);
    }
}

/// <summary>Draws an arrow, thrown weapon or flask in flight (<see cref="MissileFlight"/>), its scene at its place on its arc, turned as it is.</summary>
public partial class MissileView : Node3D
{
    private readonly Models _models;
    private MissileFlight? _flight;
    private Node3D? _scene;

    public MissileView(Models models)
    {
        _models = models;
        Name = "Missile";
    }

    /// <summary>An arrow, thrown weapon or flask leaves (<see cref="MissileLaunched"/>): its scene is made.</summary>
    public void Launch(MissileFlight flight)
    {
        Land();
        _flight = flight;
        _scene = _models.Instantiate(flight.Scene);
        if (_scene is null)
            return;
        AddChild(_scene);
        Sync();
    }

    /// <summary>It has landed (<see cref="MissileLanded"/>): its scene goes.</summary>
    public void Land()
    {
        _scene?.QueueFree();
        _scene = null;
        _flight = null;
    }

    /// <summary>Draws the flight where it is on its arc, if one is flying.</summary>
    public void Sync()
    {
        if (_scene is null || _flight is not { } flight)
            return;
        // The rotation's rows are where the scene's axes go, Godot's basis's columns.
        var r = flight.Rotation;
        var basis = new Basis(new Vector3(r.M11, r.M12, r.M13), new Vector3(r.M21, r.M22, r.M23), new Vector3(r.M31, r.M32, r.M33));
        _scene.Transform = new Transform3D(basis.Scaled(Vector3.One * flight.Scale), flight.Drawn.ToGodot());
    }
}

/// <summary>
/// The marker on the ground while a spell, its area or a flask is aimed (<c>Combat.DrawTargetCounts</c>, <c>0x80021448</c>): the
/// translucent cylinder of scene <c>0x0797</c> at the aim (<c>Battle.Aim</c>), set on the floor, as wide as the aim's radius and 2 tall, unturned.
/// Its scene's own tint (<c>Zones.SetSceneColors(scene, 0x40, 0, colour)</c>, then <c>Scene.SetTint</c>) is red for a spell and green
/// for a flask, scaled by the screen's fade, and alpha 64: the prim colour of its draws in place of the scenery's tint, and prim alpha
/// 64, so its texels' alpha × 64 / 255, blended, depth tested and not written (<c>G_RM_AA_ZB_XLU_SURF2</c>).
/// </summary>
public partial class AimMarkerView : Node3D
{
    /// <summary>The cylinder (<c>CombatSpellFx.DrawNumber</c>, <c>0x8008ABD8</c>).</summary>
    public static readonly HBHT.Plugins.RecordId Scene = HBHT.Plugins.RecordId.Hex(HBHT.Plugins.RecordTypes.Scene, 0x0797);

    /// <summary>The scene's alpha (<c>Combat.DrawTargetCounts</c>'s 0x40).</summary>
    public const byte Alpha = 0x40;

    private readonly Node3D? _cylinder;
    private readonly Level? _level;
    private readonly ScrollingScene? _scrolling;
    private float _floor;

    public AimMarkerView(Models models)
    {
        Name = "Aim";
        _cylinder = models.Instantiate(Scene);
        if (_cylinder is not null)
        {
            AddChild(_cylinder);
            _level = new Level(_cylinder, null);
            _scrolling = models.Scrolls.Attach(Scene, _cylinder);
        }
        Visible = false;
    }

    public void Sync(Battle battle)
    {
        if (battle.Aim is not { } aim || _cylinder is null)
        {
            Visible = false;
            return;
        }
        var at = battle.Origin + new System.Numerics.Vector3(aim.Centre.X, 0, aim.Centre.Y);
        // Its floor: a sphere of 1 swept from 50 down to -10, or the height it last found.
        if (Core.Physics.SpherePhysics.Sweep(battle.World, at with { Y = battle.Origin.Y + 50 }, at with { Y = battle.Origin.Y - 10 }, 1f,
                skipSeeThrough: false, out var floor, out _))
            _floor = floor.Y;
        Position = (at with { Y = _floor }).ToGodot();
        Scale = new Vector3(aim.Radius + 0.2f, 2.0f, aim.Radius + 0.2f);
        // PointLight.Scale(colour, ScreenFade.Alpha): each channel × the fade, truncated to a byte.
        var on = (int)(255 * battle.Fade.Brightness) / 255f;
        N64Material.SetSceneTint(_cylinder, aim.Spell ? new Color(on, 0, 0) : new Color(0, on, 0));
        _level!.Show(Alpha);
        Visible = true;
    }

    /// <summary>Moves its scrolling texture on, as each draw of it does (<c>Scene.Draw</c>), at the textures' frame.</summary>
    public void Draw()
    {
        if (Visible)
            _scrolling?.Draw();
    }
}
