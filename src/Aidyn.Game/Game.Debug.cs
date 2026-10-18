using System.Globalization;
using Aidyn.Core.Items;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// What the command line and the debug keys drive (<see cref="LaunchOptions"/>): the actions the options start
/// once the game is built, the screenshot run, and the F3 overlay. None of it is the game's, and a run without options
/// doesn't use it.
/// </summary>
public partial class Game
{
    private readonly Label _overlay = new() { Position = new Vector2(8, 8) };

    /// <summary>The player's last second of positions, for the overlay's speed.</summary>
    private readonly Queue<System.Numerics.Vector3> _track = new();

    /// <summary>Gives the overlay its outline, so it reads over any scene.</summary>
    private void StyleOverlay()
    {
        _overlay.AddThemeColorOverride("font_outline_color", Colors.Black);
        _overlay.AddThemeConstantOverride("outline_size", 4);
    }

    /// <summary>
    /// Carries out the options that drive the run, in the order they apply: the menu, the free camera, the input held
    /// and pressed, the cut-scene and cinematic to play, and the screenshot, which waits for what's before it.
    /// </summary>
    private void RunLaunchActions(LaunchOptions options)
    {
        switch (options.Menu)
        {
            case "1":
                _menu!.Open();
                break;
            case "loot":
                _menu!.OpenLoot(new ItemPile(_game!.Party.Inventory.Catalog), 0);
                break;
            case { } tab when Enum.TryParse<PauseTab>(tab, ignoreCase: true, out var opening):
                _menu!.Open(opening);
                break;
        }

        if (options.Fly is { } p)
        {
            _camera!.ToggleFly();
            _camera.Current.GlobalPosition = new Vector3(p[0], p[1], p[2]);
            _camera.Current.Rotation = new Vector3(_camera.Pitch, _camera.Yaw, 0);
        }
        var hold = Math.Max(Hold(options.Hold), Press(options.Press));
        if (options.CutScene is { } cutscene)
            CueFromCommandLine(cutscene);
        if (options.Cinematic is { } cinematic)
            CinematicFromCommandLine(cinematic);
        if (options.Screenshot is { } png)
            _ = Screenshot(png, hold, options.Frames);
    }

    /// <summary>Holds actions (<c>--hold move_forward+walk,4</c>) for so many seconds. Returns how long, or 0 if it's badly written.</summary>
    private double Hold(string? held)
    {
        if (held?.Split(',') is not [var action, var seconds])
            return 0;
        var hold = double.Parse(seconds, CultureInfo.InvariantCulture);
        var actions = action.Split('+');
        foreach (var a in actions)
            Input.ActionPress(a);
        GetTree().CreateTimer(hold).Timeout += () =>
        {
            foreach (var a in actions)
                Input.ActionRelease(a);
        };
        return hold;
    }

    /// <summary>
    /// Each action pressed and let go in turn, a fifth of a second apart, as input events. Returns how long that takes
    /// after the half second before the first, or 0 with none.
    /// </summary>
    private double Press(string? presses)
    {
        if (presses is null)
            return 0;
        var pressed = presses.Split(',');
        for (var i = 0; i < pressed.Length; i++)
        {
            var name = pressed[i];
            GetTree().CreateTimer(0.5 + 0.2 * i).Timeout += () =>
            {
                Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = false });
            };
        }
        return 0.5 + 0.2 * pressed.Length;
    }

    /// <summary>
    /// Plays a cut-scene from the command line (<c>--cutscene aidyn:dialogue/0x0365</c>), as the first dialogue object
    /// that starts it would, or else the first talker, in its own chunk.
    /// </summary>
    private void CueFromCommandLine(RecordId script)
    {
        if (_plugins!.TryGet<DialogueRecord>(script) is not { Mode: DialogueMode.Cutscene } record)
        {
            Say($"{script} isn't a cut-scene.");
            return;
        }
        var maps = _plugins.All<MapRecord>(RecordTypes.Map).ToList();
        foreach (var map in maps)
        {
            if (map.Objects.OfType<DialogueObject>().FirstOrDefault(d => d.Script == script && d.Stage is not null) is { } dialogue)
            {
                _session!.Cue(record, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
                return;
            }
        }
        var placements = MapPlacements.For(_plugins);
        foreach (var map in maps)
        {
            if (map.Objects.OfType<MonsterPartyObject>().FirstOrDefault(p => p.Dialogue == script) is { } party && placements.First(map.Id) is { } place)
            {
                _session!.Cue(record, new ScriptStart(0, MapRecord.VoxelId(map.Id, party.Index)), new ChunkRef(place.Set.Id, place.Chunk.Row, place.Chunk.Column));
                return;
            }
        }
        Say($"Nothing starts {script}.");
    }

    /// <summary>
    /// Plays a cinematic from the command line (<c>--cinematic 0</c>, or <c>--cinematic 0,5,300</c> to start at shot 5's
    /// time 300, and <c>--cinematic 0,5,300,hold</c> to stand still there), as from the Theatre but without the menu after.
    /// </summary>
    private void CinematicFromCommandLine(string arg)
    {
        var parts = arg.Split(',');
        if (_plugins!.TryGet<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, int.Parse(parts[0], CultureInfo.InvariantCulture))) is not { } cinematic)
        {
            Say($"There's no cinematic {parts[0]}.");
            return;
        }
        _session!.PlayCinematic(cinematic, fromTheatre: false);
        if (parts.Length > 1)
            _cinematic!.SkipTo(int.Parse(parts[1], CultureInfo.InvariantCulture),
                parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0);
        _cinematic!.Held = parts.Length > 3 && parts[3] == "hold";
    }

    /// <summary>
    /// Saves the window <paramref name="after"/> seconds on, once the frame has settled, and quits; or with
    /// <paramref name="frames"/>, the next frames as rendered. Quits with an error if it takes more than a minute.
    /// </summary>
    private async Task Screenshot(string path, double after, int frames = 0)
    {
        CommandLine.Watchdog(GetTree(), 60 + after);
        // Time for the frame to settle.
        await ToSignal(GetTree().CreateTimer(1.5 + after), SceneTreeTimer.SignalName.Timeout);
        await CommandLine.Frames(this, 3);
        if (frames > 0)
        {
            // Every frame as rendered, at the rate it renders, kept until the last, for flicker between ticks.
            var images = new List<Image>(frames);
            for (var i = 0; i < frames; i++)
            {
                images.Add(GetViewport().GetTexture().GetImage());
                await CommandLine.Frames(this, 1);
            }
            var stem = Path.ChangeExtension(path, null);
            for (var i = 0; i < images.Count; i++)
                images[i].SavePng($"{stem}-{i:000}.png");
            GD.Print($"Saved {images.Count} frames to {stem}-000.png on");
            GetTree().Quit(0);
            return;
        }
        var error = CommandLine.Save(GetViewport().GetTexture().GetImage(), path);
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    // The F3 overlay.

    /// <summary>Notes where the player is this tick, for the overlay's speed.</summary>
    private void TrackPlayer(System.Numerics.Vector3 position)
    {
        _track.Enqueue(position);
        if (_track.Count > Aidyn.Core.Calendar.GameClock.TicksPerSecond)
            _track.Dequeue();
    }

    /// <summary>The overlay's text for the frame: where the view is, what the player stands on, and what's playing and drawn.</summary>
    private void UpdateOverlay(ChunkStreamerView chunks, Vector3 focus)
    {
        var chunk = chunks.Chunks.ChunkAt(focus.ToNumerics());
        var local = chunk is null ? focus : focus - chunks.Origin(chunk);
        var motion = _player!.Motion;
        var sphere = motion.Sphere;
        _overlay.Text = (Scene is { } scene ? $"cut-scene {scene.Runner.Script.Id}, waiting for {scene.Runner.Wait?.ToString() ?? "nothing"}, {scene.Actors.Count} actors, {scene.Cameras.Count} camera moves\n" : "") +
            $"{chunks.Chunks.MapSet.Label} {chunk?.Name ?? "(no chunk)"}  " +
            $"({local.X:F1}, {local.Y:F1}, {local.Z:F1})  {Engine.GetFramesPerSecond()} fps" +
            (_camera!.Flying ? "  [free camera]" : "") + "\n" +
            (sphere.Touching ? $"on {SurfaceNames.Of(sphere.Surface)}" : "in the air") +
            $"  speed {Speed():F2}/s  request {motion.Request}  pace {motion.Pace:0.##} (F5)" +
            $"  camera {_follow!.Mode.ToString().ToLowerInvariant()}{(Trek!.Use.CurrentCamera is { } held ? $" (object {held.Index})" : "")}" +
            $"  {Clock(_sky!.Clock.TimeOfDay)}  sky {_sky.Placement.ToString().ToLowerInvariant()} (F6)" +
            $"  UI {_menu!.Screen.Lines} lines (F7)  wanderers {Trek.Wanderers.All.Count}\n" +
            $"music {_audio!.MusicPlaying?.Key ?? "none"}  sounds {Trek.Zone.Sounds.Playing} of the map's, {_audio.SoundsPlaying} one-shots" +
            $"  particles {_particles!.Head.ParticlesInUse} of {_particles.Head.MaxParticles}{Precipitation()}";
    }

    /// <summary>For the overlay: how many raindrops or snowflakes and splashes the last frame drew, and how many of them are on screen.</summary>
    private string Precipitation()
    {
        if ((Scene is not null ? Stage?.Precipitation : Trek?.Zone.Precipitation) is not { Drops: { } drops, Splashes: { } splashes } ||
            GetViewport().GetCamera3D() is not { } camera)
            return "";
        int fall = 0, fallSeen = 0, splash = 0, splashSeen = 0;
        foreach (var s in _particles!.Head.Sprites)
        {
            var seen = camera.IsPositionInFrustum(s.Position.ToGodot()) ? 1 : 0;
            if (s.Emitter == drops.Slot)
                (fall, fallSeen) = (fall + 1, fallSeen + seen);
            else if (s.Emitter == splashes.Slot)
                (splash, splashSeen) = (splash + 1, splashSeen + seen);
        }
        return $"  drops {fall} ({fallSeen} on screen), splashes {splash} ({splashSeen})";
    }

    private static string Clock(float timeOfDay)
    {
        var minutes = (int)(timeOfDay * 24 * 60);
        return $"{minutes / 60:D2}:{minutes % 60:D2}";
    }

    /// <summary>How far the player went in X and Z over the last second, per second.</summary>
    private float Speed()
    {
        if (_track.Count < 2)
            return 0;
        var span = _track.Last() - _track.Peek();
        return new System.Numerics.Vector2(span.X, span.Z).Length() * Aidyn.Core.Calendar.GameClock.TicksPerSecond / (_track.Count - 1);
    }
}
