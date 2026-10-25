using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Plugins.Records;

namespace HBHT.Core.MapObjects;

/// <summary>
/// Camera objects taking the camera. Those the player's grid cell lists work while he's within their
/// radius (<c>ProcessVoxelObjects</c>), before the camera steps. Those trigger triangles name, which
/// sit at the chunk's origin with a radius of 200 and no cell lists, work for a second after he last
/// touches one (<c>set_camera_voxel_pointer</c>, then <c>ProcessScriptCamera</c>), after he moves.
/// </summary>
public sealed partial class MapObjectUse
{
    /// <summary>How long a trigger triangle's camera keeps the camera after the player's last on it, in ticks (<c>camera_dat</c>).</summary>
    public const int TreadCameraTicks = 60;

    /// <summary>The trigger triangle's camera last trodden on (<c>cameraVoxelPointer</c>).</summary>
    private (LoadedMap Map, CameraObject Camera)? _trodden;

    private int _treadTicks;

    /// <summary>The camera that camera objects move. Without one, they do nothing.</summary>
    public FollowCamera? Camera { get; set; }

    /// <summary>The player's sphere's radius, which a camera object without a reference point allows for.</summary>
    public float PlayerRadius { get; set; } = 0.5f;

    private (CameraObject Camera, long Tick)? _stepped;

    /// <summary>The camera object that last had the camera, if one did this tick or the last.</summary>
    public CameraObject? CurrentCamera => _stepped is var (camera, tick) && tick + 1 >= game.Ticker ? camera : null;

    /// <summary>
    /// A trigger triangle's camera after the player moves (<c>ProcessScriptCamera</c>): it takes the
    /// camera until a second after he last touched one of its triangles. <paramref name="at"/> is in
    /// the world.
    /// </summary>
    public void StepCameras(Vector3 at)
    {
        if (_treadTicks == 0)
            return;
        _treadTicks--;
        if (_trodden is var (map, camera))
            Step(map, camera, at - map.Origin);
    }

    /// <summary>Lets go of a trigger triangle's camera, as going through a teleporter does (<c>clear_camera_voxel_pointer</c>).</summary>
    public void ClearCameras()
    {
        _trodden = null;
        _treadTicks = 0;
    }

    /// <summary>
    /// Touching a trigger triangle that names object <paramref name="index"/>, for its camera alone: as
    /// <see cref="Tread"/> does, but leaving other objects be, as through a teleport's fade.
    /// </summary>
    public void TreadCamera(LoadedMap map, int index)
    {
        if (map.IsActive(index) && map[index] is CameraObject camera)
            TreadCamera(map, camera);
    }

    private void TreadCamera(LoadedMap map, CameraObject camera)
    {
        _trodden = (map, camera);
        _treadTicks = TreadCameraTicks;
    }

    /// <summary>
    /// The cameras the player's grid cell lists, at <paramref name="at"/> in the chunk's coordinates,
    /// which <see cref="Touch"/> runs first. Alone, as through a teleport's fade, it leaves other objects be.
    /// </summary>
    public void TouchCameras(LoadedMap map, Vector3 at)
    {
        if (Camera is null || map.Record.Grid is not { } grid)
            return;
        foreach (var index in grid.At(at))
        {
            if (map[index] is CameraObject camera)
                Step(map, camera, at);
        }
    }

    private void Step(LoadedMap map, CameraObject camera, Vector3 at)
    {
        if (Camera is null || !map.IsActive(camera.Index) || Vector3.Distance(at, camera.Position) > camera.Radius)
            return;
        map.CameraMove(camera).Step(Camera, map.Origin + at, PlayerRadius, game.Ticker);
        _stepped = (camera, game.Ticker);
    }
}
