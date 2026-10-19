using Aidyn.Core.Combat;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the numbers over heads (<see cref="CombatNumbers"/>, <c>CombatStatIndicator.Frame</c>, <c>0x8008A630</c>): each a quad
/// facing the camera, its texture the number's digits put together from the ten digit textures into 64 × 16, centred, tinted
/// yellowish white for damage and blue for healing, and rising, growing and fading as the core says.
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

    /// <summary>Draws the numbers showing, seen from a camera at <paramref name="eye"/>.</summary>
    public void Sync(CombatNumbers numbers, System.Numerics.Vector3 eye)
    {
        foreach (var gone in _shown.Keys.Except(numbers.Shown).ToList())
        {
            _shown[gone].Mesh.QueueFree();
            _shown.Remove(gone);
        }
        foreach (var number in numbers.Shown)
        {
            if (!_shown.TryGetValue(number, out var drawn))
                _shown[number] = drawn = Make(number);
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
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        var mesh = new MeshInstance3D { Mesh = new QuadMesh(), MaterialOverride = material };
        AddChild(mesh);
        return (mesh, material);
    }
}

/// <summary>Draws an arrow or thrown weapon in flight (<see cref="MissileFlight"/>), its scene at its place on its arc.</summary>
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

    /// <summary>Draws <paramref name="flight"/>, or nothing when none is flying.</summary>
    public void Sync(MissileFlight? flight)
    {
        if (flight != _flight)
        {
            _scene?.QueueFree();
            _scene = flight is null ? null : _models.Instantiate(flight.Scene);
            if (_scene is not null)
            {
                _scene.Scale = Vector3.One * flight!.Scale;
                _scene.Rotation = new Vector3(0, Mathf.DegToRad(flight.Turn), 0);
                AddChild(_scene);
            }
            _flight = flight;
        }
        if (_scene is not null && flight is not null)
            _scene.Position = flight.Drawn.ToGodot();
    }
}
