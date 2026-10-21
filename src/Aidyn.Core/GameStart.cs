using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Core;

/// <summary>
/// Where a game starts (<see cref="GameSession.Start"/>): a save's place, where the player stood when it was made, or a
/// new game's arrival, which puts his body 10 up and sets it down on the ground (<see cref="GameSession.Arrival"/>). The
/// runtime's command line overrides any of it with <c>with</c>.
/// </summary>
/// <param name="Set">The map set to start in.</param>
/// <param name="Chunk">The chunk's name in it, such as I08.</param>
/// <param name="Local">The point in the chunk the player's body is made at.</param>
/// <param name="Heading">Which way he faces, as a look across the ground; zero faces along −Z.</param>
/// <param name="SetDown">
/// Whether he's set down from on high, on the highest ground below 1000, as the original sets a new game's body down: a
/// save puts him exactly where he stood.
/// </param>
/// <param name="Camera">The follow camera's mode, or null for the one the chunk's terrain chooses, as a teleport into it would (<see cref="FollowCamera.ModeFor"/>).</param>
public sealed record GameStart(MapSetRecord Set, string Chunk, Vector3 Local, Vector2 Heading, bool SetDown, CameraMode? Camera = null)
{
    /// <summary>The look of a yaw about the up axis, in radians: zero faces along −Z.</summary>
    public static Vector2 Facing(float yaw) => new(-MathF.Sin(yaw), -MathF.Cos(yaw));

    /// <summary>A new game's start, where its record puts the player (<see cref="GameSession.Arrival"/>), facing −Z and set down.</summary>
    public static GameStart OfNewGame(PluginSet plugins)
    {
        var (set, chunk, at) = GameSession.Arrival(plugins, GameSession.NewGame(plugins));
        return new GameStart(set, chunk, at, Facing(0), SetDown: true);
    }

    /// <summary>A save's start: where the player stood, facing as he did, not set down.</summary>
    public static GameStart OfSave(PluginSet plugins, SavePlace place) =>
        new(plugins.Get<MapSetRecord>(place.MapSet), place.Chunk, place.Position, place.Heading, SetDown: false);

    /// <summary>The game's own start: its save's place if it has one, else a new game's.</summary>
    public static GameStart For(GameState game) =>
        game.Place is { } place ? OfSave(game.Plugins, place) : OfNewGame(game.Plugins);

    /// <summary>
    /// The start in another map set: this one if it's there already, else a new game's arrival moved to it, as the runtime's
    /// <c>--set</c> leaves a save's place behind.
    /// </summary>
    public GameStart In(PluginSet plugins, MapSetRecord set) => Set.Id == set.Id ? this : OfNewGame(plugins) with { Set = set };
}
