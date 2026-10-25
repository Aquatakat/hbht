namespace HBHT.Views;

/// <summary>Between Godot's vectors and <see cref="System.Numerics"/>, which the plugins and the core use. The axes are the same.</summary>
public static class Vectors
{
    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector3 ToNumerics(this Godot.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Godot.Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static System.Numerics.Vector2 ToNumerics(this Godot.Vector2 v) => new(v.X, v.Y);
}
