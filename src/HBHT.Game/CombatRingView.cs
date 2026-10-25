using HBHT.Core.Combat;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws the ring of teardrops round the current combatant's field (<see cref="CombatMarkers"/>, <c>CombatMarkers.Draw</c>):
/// the pool's 20 copies of the teardrop, each with its own scrolling texture as every scene the original loads has, the
/// ring's first <see cref="CombatMarkers.Count"/> shown where the core set them and turned to the camera, all hidden while
/// an action plays. The scene's own look does the rest, a blended billboard, lit and fogged as the arena's scenery is
/// (<c>Zones.SetSceneColors</c>).
/// </summary>
public partial class CombatRingView : Node3D
{
    private readonly List<(Node3D Node, ScrollingScene? Scrolling, List<Node3D> Facings)> _teardrops = [];

    public CombatRingView(Models models)
    {
        Name = "Ring";
        for (var i = 0; i < CombatMarkers.Pool; i++)
        {
            if (models.Instantiate(CombatMarkers.Scene) is not { } node)
                break;
            AddChild(node);
            node.Visible = false;
            _teardrops.Add((node, models.Scrolls.Attach(CombatMarkers.Scene, node), Billboards.Facings(node)));
        }
        // It's laid out as a turn begins, and moves with the physics tick as the bodies do.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
    }

    public CombatRingView()
    {
    }

    /// <summary>Puts the teardrops where the battle's ring has them, shown unless an action plays.</summary>
    public void Sync(Battle battle)
    {
        var points = battle.Ring.Points;
        var shown = !battle.ActionActive;
        for (var i = 0; i < _teardrops.Count; i++)
        {
            var node = _teardrops[i].Node;
            node.Visible = shown && i < points.Length;
            if (node.Visible)
                node.Position = points[i].ToGodot();
        }
    }

    /// <summary>Turns the shown teardrops to face the camera.</summary>
    public void Face(Vector3 camera)
    {
        foreach (var (node, _, facings) in _teardrops)
        {
            if (node.Visible)
                Billboards.Face(facings, camera);
        }
    }

    /// <summary>A frame of the shown teardrops, which moves their scrolling textures on (<c>Scene.Draw</c>).</summary>
    public void Draw()
    {
        foreach (var (node, scrolling, _) in _teardrops)
        {
            if (node.Visible)
                scrolling?.Draw();
        }
    }
}
