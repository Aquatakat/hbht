using Aidyn.Plugins.Assets;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// A chunk's collision as Godot physics bodies. Invisible walls block the player but not the
/// camera, as in the original, where line-of-sight tests skip them.
/// </summary>
public static class Collision
{
    /// <summary>What the player collides with: every triangle that isn't passable.</summary>
    public const uint PlayerLayer = 1;

    /// <summary>What the camera collides with: the player's triangles less the see-through ones.</summary>
    public const uint CameraLayer = 2;

    public static Node3D Build(IReadOnlyList<CollisionTriangle> triangles)
    {
        var root = new Node3D { Name = "Collision" };
        var solid = new List<Vector3>();
        var seeThrough = new List<Vector3>();
        foreach (var t in triangles)
        {
            // Toggles stay solid until a trigger opens them, which needs the flag engine (M2).
            if (t.Flags.HasFlag(CollisionFlags.Passable))
                continue;
            // Godot's front faces wind clockwise, the original's counter-clockwise.
            var list = t.Flags.HasFlag(CollisionFlags.SeeThrough) ? seeThrough : solid;
            list.Add(ToGodot(t.A));
            list.Add(ToGodot(t.C));
            list.Add(ToGodot(t.B));
        }
        Add(root, "Solid", solid, PlayerLayer | CameraLayer);
        Add(root, "SeeThrough", seeThrough, PlayerLayer);
        return root;
    }

    private static void Add(Node3D root, string name, List<Vector3> faces, uint layers)
    {
        if (faces.Count == 0)
            return;
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces([.. faces]);
        var body = new StaticBody3D { Name = name, CollisionLayer = layers, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        root.AddChild(body);
    }

    public static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
