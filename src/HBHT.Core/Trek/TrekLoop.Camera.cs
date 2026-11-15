using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

public sealed partial class TrekLoop
{
    /// <summary>Whether the player held Sneak this tick, which the enhanced camera's sneaking and nudges read.</summary>
    private bool _sneaking;

    /// <summary>The enhanced trek camera's lean toward the path's bend.</summary>
    public PathBendLean? BendLean { get; private set; }

    /// <summary>Its nudges, toward what A would use if it reached further and toward a hunter.</summary>
    public NudgeLean? Nudges { get; private set; }

    /// <summary>
    /// What the enhanced trek camera asks of trek: the walls for the path's bend; for the nudges, A's choice at the longer
    /// reach (<see cref="MapObjects.MapObjectUse.Reach"/> with its <c>farther</c>, an npc's body weighed as A weighs the one it
    /// reaches) and the nearest hunter; for sneaking, the hunters and the Sneak button.
    /// </summary>
    private void WireEnhancedCamera()
    {
        var camera = EnhancedCamera;
        BendLean = new PathBendLean(camera, () => Chunks.World);
        Nudges = new NudgeLean(camera) { Usable = Usable, Hunter = NearestHunter, Sneaking = () => _sneaking };
        camera.Leans.Add(BendLean);
        camera.Leans.Add(Nudges);
        camera.Sneak.Sneaking = () => _sneaking;
        camera.Sneak.Hunters = _ => Hunters().Select(w => (w.Body.Sphere.Position, (float)w.Perception));
    }

    private IEnumerable<Wanderer> Hunters() => Wanderers.All.Where(w => w.Hunts && !w.Calmed);

    private Vector3? NearestHunter(Vector3 player)
    {
        var reach = EnhancedCamera.Tuning.HunterReach;
        return Hunters().Select(w => w.Body.Sphere.Position).Where(p => Flat(p - player) < reach)
            .OrderBy(p => Flat(p - player)).Cast<Vector3?>().FirstOrDefault();
    }

    /// <summary>What A would use if it reached <see cref="TrekCameraTuning.NudgeReach"/>, where it is, or null.</summary>
    private Vector3? Usable(Vector3 player, Vector2 travel)
    {
        if (Use.Popup is not null || Chunks.MapAt(player) is not { } map || travel.LengthSquared() < 1e-8f)
            return null;
        var reach = EnhancedCamera.Tuning.NudgeReach;
        var npc = Wanderers.All
            .Where(w => w.Map == map && !w.Hunts && (w.Party.Flags & PartyFlags.Silent) == 0)
            .Select(w => (Wanderer: w, Distance: Flat(w.Body.Sphere.Position - player)))
            .Where(n => n.Distance < reach)
            .OrderBy(n => n.Distance)
            .Select(n => ((Wanderer, float)?)n)
            .FirstOrDefault();
        (PartyObject, Vector3, float)? body = npc is var (w, d) ? (w.Party, w.Body.Sphere.Position - map.Origin, d) : null;
        return Use.Reach(map, player - map.Origin, Vector2.Normalize(travel), Player.Sphere.Radius, body, reach) switch
        {
            null => null,
            PartyObject when npc is var (talker, _) => talker.Body.Sphere.Position,
            var o => map.Origin + o.Position,
        };
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
}
