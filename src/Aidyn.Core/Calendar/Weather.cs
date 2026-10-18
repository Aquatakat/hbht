using System.Numerics;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Calendar;

/// <summary>
/// What the weather is doing, as the roll leaves it (the world's <c>+0x4</c>), which only combat reads, as a class of
/// bad weather: fog and storm. The end of a Wind enchantment sets it to fog in the original, and loading doesn't restore it.
/// </summary>
public enum WeatherKind : byte
{
    Clear = 0,
    Fog = 1,
    /// <summary>Rain or snow, with some fog: a storm, to combat.</summary>
    Precipitation = 2,
}

/// <summary>What falls (the world's <c>+0x6</c>), which the rain flags hold.</summary>
public enum Precipitation : byte
{
    None = 0,
    Rain = 1,
    Snow = 2,
}

/// <summary>
/// The weather: what the calendar and the terrain roll (<c>Weather.Roll</c>, <c>0x8007bdb0</c> in 1.0, on the world
/// behind <c>TerrainPointer</c>), the wind that blows the clouds and the rain (<c>Weather.DriftWind</c>, <c>0x8007baa0</c>),
/// what's drawn as it sets in and dies away (<c>ProcessWeather</c>, <c>0x8001e7c0</c>, on <c>gGlobals</c>' weather at
/// <c>0x800d841c</c>), and the lightning (<c>RenderSky</c>, <c>0x8001d840</c>). See <see cref="WeatherRecord"/> for
/// the rules, and docs/systems/weather.md.
/// </summary>
/// <remarks>
/// The original checks the part of the day every frame of the clock (<c>World.Update</c>, <c>0x8007bf5c</c>), and
/// rolls when it has changed; entering a chunk of another terrain moves the part it remembers on by one first
/// (<c>World::SetTerrain</c>), so that rolls too. The wind drifts by itself, whatever the weather. The original draws
/// all of it from its one stream; the port rolls on the world's stream, and draws the wind's drift and the lightning
/// from the cosmetic one (ADR 0006).
/// <para>
/// What's drawn carries on as trek's world is built again, after a cut-scene, a backdrop or the help menu, and for a
/// cut-scene's stage, which is the user's choice: the original clears it there (<c>FreeZoneEngine</c> stops the rain loop
/// through <c>Weather.Free</c>, <c>0x8001e030</c>, and <c>InitZoneEngine</c> clears the rest through <c>Weather.Init</c>,
/// <c>0x8001e010</c>), so the sky drops to the terrain's and the rain sets in again from nothing. In the port only the
/// emitters go, with the particle pool (<see cref="Forget"/>), and the next tick makes a pair about the new view.
/// </para>
/// </remarks>
public sealed class Weather
{
    /// <summary>
    /// A new game's wind (<c>World::init</c>): (1, −0.1, 1) normalised, at 15/648 a tick, which is 0.1 of
    /// <see cref="WeatherRecord.WindMaxSpeed"/>'s 150/648.
    /// </summary>
    public static readonly Vector3 NewGameWind = Vector3.Normalize(new Vector3(1, -0.1f, 1)) * (15f / 648);

    private readonly WeatherRecord _rules;
    private readonly FrameGate _frame = new(Cadence.FrameTicks);
    private int _skyFrame;
    private bool? _skyDrawn;

    public Weather(WeatherRecord rules) => _rules = rules;

    public WeatherRecord Rules => _rules;

    // The world's.

    public WeatherKind Kind { get; set; }

    /// <summary>What falls, which the rain flags hold, and which <see cref="Falling"/> moves to.</summary>
    public Precipitation Precipitation { get; set; }

    /// <summary>How hard it falls, 0 to 1, which <see cref="FallIntensity"/> rises to.</summary>
    public float Intensity { get; set; }

    /// <summary>How thick the fog is, 0 to 1, which <see cref="FogTime"/> moves to.</summary>
    public float Fog { get; set; }

    /// <summary>
    /// How stormy the rain is, from 0 to its intensity (the world's <c>+0x10</c>), rolled with the rain, and 0 for
    /// snow and fog.
    /// </summary>
    public float Storm { get; set; }

    /// <summary>The wind, a tick's distance: the clouds blow 4 × it a frame.</summary>
    public Vector3 Wind { get; set; } = NewGameWind;

    /// <summary>
    /// The part of the day the weather last saw (the world's <c>+0x2</c>), which the original keeps for the flags too.
    /// The weather rolls when the clock's differs. Null before it's first seen, as a new game starts: it's taken then
    /// without a roll.
    /// </summary>
    public PartOfDay? PartOfDay { get; set; }

    /// <summary>The terrain the player is in (the world's <c>+0x24</c>), or null before the first.</summary>
    public int? Terrain { get; set; }

    // What's drawn: ProcessWeather's.

    /// <summary>
    /// What the world wants falling, as the last frame drawn saw it (<c>Weather.Data</c> + 0x0): the world's
    /// <see cref="Precipitation"/>, copied at the start of each <see cref="Draw"/>.
    /// </summary>
    public Precipitation Wanted { get; private set; }

    /// <summary>What's falling now: it changes to <see cref="Precipitation"/> once the last has died away.</summary>
    public Precipitation Falling { get; set; }

    /// <summary>How hard it's falling now, 0 to 1: it rises to <see cref="Intensity"/> and dies away at the rules' rate.</summary>
    public float FallIntensity { get; set; }

    /// <summary>
    /// How thick the fog is now, 0 to 1 (<c>fogTime</c>), moving toward <see cref="Fog"/>. 0 under a terrain that
    /// doesn't draw the sky. The scenery's fog, the sky, its objects and the wanderers' sight read it.
    /// </summary>
    public float FogTime { get; set; }

    /// <summary>
    /// Whether the precipitation's emitters are up (<c>Weather.Data</c> + 0x14): made with the rain sky once what
    /// falls is what the world wants, forgotten when the world wants another, and freed where the sky isn't drawn.
    /// </summary>
    public bool Emitting { get; private set; }

    /// <summary>
    /// Whether the rain sky is up for the weather (<c>Weather.SkyOverridden</c>, <c>0x800daee0</c>): set as the
    /// emitters are made, and cleared, bringing back the terrain's sky, once the world wants nothing to fall. Building
    /// trek's world again doesn't clear it, in the original or the port.
    /// </summary>
    public bool SkyOverridden { get; private set; }

    /// <summary>
    /// Whether the rain loop plays (<c>Weather.Data</c> + 0x18): it starts where the sky is drawn while rain falls,
    /// and stops once the rain has died away, or at once where the sky isn't drawn.
    /// </summary>
    public bool RainLoop { get; private set; }

    /// <summary>How many times the rain loop has started, so its player can tell a new start from one going on.</summary>
    public int RainLoopStarts { get; private set; }

    /// <summary>
    /// How loud the rain loop is before the sound volume, a byte (<c>ProcessWeather</c>):
    /// <c>min(255, trunc(FallIntensity × 1.5 × 255))</c>, in doubles, so it's full from 2/3.
    /// </summary>
    public byte RainLoopLevel => (byte)Math.Min((int)(FallIntensity * 1.5 * 255.0), 255);

    /// <summary>
    /// The weather changes the sky (<c>SetBackgroundType</c> with 600 ticks, from <c>ProcessWeather</c>): true to
    /// the rain sky as rain or snow sets in, false back to the terrain's once the world wants nothing to fall.
    /// </summary>
    public event Action<bool>? SkyChange;

    /// <summary>
    /// Whether the weather keeps the sky as the player enters a chunk under an outdoor sky from another
    /// (<c>LoadGameBorgScenes</c>, the debug build's "Different Precip types, Don't set sky"): while the world wants
    /// something to fall or something is falling, as of the last frame drawn. Fog alone doesn't.
    /// </summary>
    public bool HoldsSky => Wanted != Precipitation.None || Falling != Precipitation.None;

    /// <summary>
    /// Whether a sky chosen at once, as trek's world is built or the sky starts or stops being drawn, is the rain sky
    /// (<c>LoadGameBorgScenes</c>): rain or snow wanted, as of the last frame drawn.
    /// </summary>
    public bool WantsRainSky => Wanted != Precipitation.None;

    /// <summary>Whether lightning flashes this frame: the screen and the sky go white.</summary>
    public bool Flash { get; private set; }

    /// <summary>Which of the four thunder sounds plays this frame, as lightning flashes, or null.</summary>
    public int? Thunder { get; private set; }

    /// <summary>
    /// A new game's weather (<c>World.Init</c>, then <c>World.SetDate</c> at noon): the world starts at the first part of
    /// the day and terrain 0, so setting the clock to noon changes the part of the day, and the weather may change at
    /// once, by the season's and terrain 0's chances. The first chunk's terrain may change it again.
    /// </summary>
    public void NewGame(GameClock clock, TerrainRecord? terrain0, IRandom world, IRandom cosmetic)
    {
        PartOfDay = Calendar.PartOfDay.Morning;
        Terrain = 0;
        Frame(clock.Date.PartOfDay, clock.Date.Season, terrain0, world, cosmetic);
    }

    /// <summary>
    /// The weather rolls (<c>Weather.Roll</c>): kept on a d100 at most <see cref="WeatherRecord.KeepAtMost"/>;
    /// otherwise cleared, and then precipitation on a d100 at most the season's chance plus the terrain's: rain or
    /// snow under <see cref="WeatherRecord.FallBelow"/>, at the terrain's scale give or take the spread, with a little
    /// fog and a storm of up to its intensity for rain; or fog.
    /// </summary>
    public void Roll(int season, TerrainRecord? terrain, IRandom random)
    {
        if (random.RollD(1, 100) <= _rules.KeepAtMost)
            return;
        Kind = WeatherKind.Clear;
        Precipitation = Precipitation.None;
        Intensity = 0;
        Fog = 0;
        var seasonChance = season >= 0 && season < _rules.Seasons.Count ? _rules.Seasons[season] : 0;
        // The original adds a u8 and an s8 and compares as an s16.
        var chance = (short)(seasonChance + (terrain?.Precipitation ?? 0));
        if (random.RollD(1, 100) <= chance)
        {
            WeatherRange fog;
            if (random.RollD(1, 100) < _rules.FallBelow)
            {
                Kind = WeatherKind.Precipitation;
                Precipitation = terrain?.Snow == true ? Precipitation.Snow : Precipitation.Rain;
                var scale = terrain?.PrecipitationScale ?? 0;
                Intensity = MathF.Max(FloatRange(random, scale - _rules.IntensitySpread, scale + _rules.IntensitySpread), 0);
                fog = _rules.FallFog;
            }
            else
            {
                Kind = WeatherKind.Fog;
                fog = _rules.Fog;
            }
            Fog = FloatRange(random, fog.Min, fog.Max);
        }
        Storm = Precipitation == Precipitation.Rain ? random.Float(1) * Intensity : 0;
    }

    /// <summary>
    /// A frame of the world's clock, or a jump in it (<c>World.Update</c>): the weather rolls if the part of the day has
    /// changed, and the wind may wander.
    /// </summary>
    /// <param name="wind">Whether the wind wanders this time: once a frame, which the port takes as every
    /// <see cref="Cadence.FrameTicks"/> ticks.</param>
    public void Frame(PartOfDay partOfDay, int season, TerrainRecord? terrain, IRandom world, IRandom cosmetic, bool wind = true)
    {
        if (PartOfDay is { } last && last != partOfDay)
        {
            PartOfDay = partOfDay;
            Roll(season, terrain, world);
        }
        PartOfDay = partOfDay;
        if (wind)
            DriftWind(cosmetic);
    }

    /// <summary>A tick of the world's clock: <see cref="Frame"/>, with the wind wandering once a frame.</summary>
    public void Tick(PartOfDay partOfDay, int season, TerrainRecord? terrain, IRandom world, IRandom cosmetic)
    {
        Frame(partOfDay, season, terrain, world, cosmetic, _frame.Tick());
    }

    /// <summary>
    /// The player enters a chunk of <paramref name="terrain"/> (<c>World::SetTerrain</c>): for another terrain, the part
    /// of the day the weather saw moves on by one, so the frame that follows rolls.
    /// </summary>
    /// <returns>Whether it's another terrain.</returns>
    public bool SetTerrain(int terrain)
    {
        var other = Terrain is { } was && was != terrain;
        if (other && PartOfDay is { } part)
            PartOfDay = (PartOfDay)(((int)part + 1) % 5);
        Terrain = terrain;
        return other;
    }

    /// <summary>
    /// The wind drifts (<c>Weather.DriftWind</c>), once a frame, whatever the weather: by a chance, its direction moves a little
    /// and is normalised, and by another, its speed changes a little, from 0 to the most.
    /// </summary>
    public void DriftWind(IRandom random)
    {
        var speed = Wind.Length();
        var direction = speed > 0 ? Wind / speed : Wind;
        if (random.Float(1) <= _rules.WindChance)
        {
            direction += new Vector3(
                FloatRange(random, -_rules.WindTurn, _rules.WindTurn),
                FloatRange(random, _rules.WindRise.Min, _rules.WindRise.Max),
                FloatRange(random, -_rules.WindTurn, _rules.WindTurn));
            direction = Normalise(direction);
        }
        if (random.Float(1) <= _rules.WindChance)
        {
            var gusted = speed + FloatRange(random, -_rules.WindGust, _rules.WindGust);
            speed = gusted > _rules.WindMaxSpeed ? _rules.WindMaxSpeed : gusted < 0 ? 0 : gusted;
        }
        Wind = direction * speed;
    }

    /// <summary>
    /// What's drawn carries on from a save (the port's choice): what the world wants falling is what it wanted as the
    /// game was saved, so the first chunk chooses the rain sky at once where it rains or snows, and what's falling, how
    /// hard and the fog go on as the save has them. The original's load starts what's drawn from nothing
    /// (<c>InitZoneEngine</c>, through <c>Weather.Init</c>, <c>0x8001e010</c>), with the fog and the intensity written
    /// back after (<c>Weather.Resume</c>, <c>0x8001a4f0</c>), so the rain sky greys in again over ten seconds.
    /// </summary>
    public void ResumeDrawing() => Wanted = Precipitation;

    /// <summary>
    /// A tick of what's drawn (<c>ProcessWeather</c>): what falls dies away before another starts and rises to the
    /// weather's intensity, and the fog moves toward the weather's. Where the terrain doesn't draw the sky, there's no
    /// fog. Coming into a chunk that draws the sky from one that doesn't, or the first time, the fog is the weather's
    /// at once (<c>LoadGameBorgScenes</c>). Rain and snow bring the rain sky as they set in, where the sky is drawn,
    /// and the terrain's comes back as soon as the world wants nothing to fall (<see cref="SkyChange"/>). Rain plays
    /// its loop (<see cref="RainLoop"/>).
    /// </summary>
    /// <param name="skyDrawn">Whether the terrain draws the sky (not the original's sky type 4).</param>
    /// <param name="emitters">
    /// The rain's or snow's particles, which this makes, steers and frees as <see cref="Emitting"/> says; none where
    /// nothing draws them, as in the port's cut-scenes, when <see cref="Emitting"/> still follows the original's.
    /// </param>
    public void Draw(bool skyDrawn, int ticks = 1, IPrecipitationEmitters? emitters = null)
    {
        if (_skyDrawn != skyDrawn)
            FogTime = Fog;
        _skyDrawn = skyDrawn;
        Wanted = Precipitation;
        // The world wants another: the emitters are forgotten, and run out their lives.
        if (Falling != Wanted)
            Forget(emitters);
        if (FallIntensity <= 0)
        {
            Falling = Precipitation;
            RainLoop = false;
        }
        if (Wanted == Precipitation.None && SkyOverridden)
        {
            SkyOverridden = false;
            SkyChange?.Invoke(false);
        }
        // The rain sky is asked for before the emitters are made, so where the pool has no room for them, it's asked
        // for again every frame until there is.
        var made = false;
        if (Falling == Wanted && Falling != Precipitation.None && !Emitting && skyDrawn)
        {
            SkyOverridden = true;
            SkyChange?.Invoke(true);
            Emitting = made = emitters?.Make(Falling) ?? true;
        }
        // Steered by how hard it falls before this tick's change, on the frame they're made too. Made while it's
        // already falling, after trek rebuilds, through a door or out of a building, the air fills at once: the
        // user's choice, where the original's starts empty. Rain setting in from nothing still starts empty.
        if (Emitting)
        {
            emitters?.Steer(Falling, FallIntensity, Wind);
            if (made && FallIntensity > 0)
                emitters?.FillAir();
        }
        if (Falling == Precipitation)
        {
            if (Falling != Precipitation.None)
            {
                if (Emitting)
                    emitters?.Keep();
                FallIntensity = FallIntensity < Intensity ? FallIntensity + ticks * _rules.FallRate : Intensity;
                if (FallIntensity > 1)
                    FallIntensity = 1;
            }
        }
        else
        {
            FallIntensity -= ticks * _rules.FallRate;
            if (FallIntensity < 0)
                FallIntensity = 0;
        }
        if (skyDrawn && !RainLoop && Falling == Precipitation.Rain)
        {
            RainLoop = true;
            RainLoopStarts++;
        }
        FogTime = MathF.Abs(FogTime - Fog) < 0.01f ? Fog
            : FogTime < Fog ? FogTime + ticks * _rules.FogRate : FogTime - ticks * _rules.FogRate;
        if (!skyDrawn)
        {
            if (Emitting)
                emitters?.Free();
            Emitting = false;
            RainLoop = false;
            FogTime = 0;
        }
    }

    /// <summary>
    /// The emitters are forgotten (<c>Weather.Data</c> + 0x14 cleared), left to run out their lives: as the world wants
    /// another to fall, and as a door's teleport does before it kills every emitter still spawning (<c>TeleportPlayer</c>,
    /// <c>0x8000bb54</c>). The next frame that something falls makes a pair again, and asks for the rain sky again.
    /// The rain loop plays on.
    /// </summary>
    public void Forget(IPrecipitationEmitters? emitters = null)
    {
        if (Emitting)
            emitters?.Forget();
        Emitting = false;
    }

    /// <summary>
    /// A frame of the sky (<c>RenderSky</c>), in an outdoor sky only: while the rain is stormy, every
    /// <see cref="WeatherRecord.LightningEvery"/>th frame, lightning strikes on a share under the storm times
    /// <see cref="WeatherRecord.LightningShare"/>, and one of the four thunder sounds plays.
    /// </summary>
    public void SkyFrame(bool skyDrawn, IRandom cosmetic)
    {
        Flash = false;
        Thunder = null;
        if (!skyDrawn)
            return;
        _skyFrame = (_skyFrame + 1) % Math.Max(_rules.LightningEvery, 1);
        var share = Storm > 0 ? cosmetic.Float(1) : 0;
        if (share < Storm * _rules.LightningShare && _skyFrame == 0)
        {
            Flash = true;
            Thunder = cosmetic.Range(0, 3);
        }
    }

    /// <summary>
    /// Sets the weather at once, already set in, for debugging and screenshots: rain or snow at
    /// <paramref name="intensity"/> with <paramref name="fog"/>, a storm as hard as the rain, or fog alone, or clear.
    /// </summary>
    public void Force(WeatherKind kind, Precipitation precipitation = Precipitation.Rain, float intensity = 0.5f, float fog = 0.2f)
    {
        Kind = kind;
        Precipitation = kind == WeatherKind.Precipitation ? precipitation : Precipitation.None;
        Intensity = kind == WeatherKind.Precipitation ? intensity : 0;
        Fog = kind == WeatherKind.Clear ? 0 : fog;
        Storm = Precipitation == Precipitation.Rain ? Intensity : 0;
        Wanted = Precipitation;
        Falling = Precipitation;
        FallIntensity = Intensity;
        FogTime = Fog;
    }

    /// <summary>A float from <paramref name="a"/> to <paramref name="b"/> (<c>0x80093470</c>): equal bounds draw nothing.</summary>
    private static float FloatRange(IRandom random, float a, float b)
    {
        if (a == b)
            return a;
        var (low, high) = a <= b ? (a, b) : (b, a);
        return random.Float(1) * (high - low) + low;
    }

    private static Vector3 Normalise(Vector3 v) => v.Length() is var length and > 0 ? v / length : v;
}
