using System.Globalization;

namespace HBHT.Core.Cameras;

/// <summary>
/// The enhanced trek camera's numbers (<see cref="EnhancedTrekCamera"/>), in one place to tune them. Lengths are units, angles
/// degrees, times seconds; speeds are a second's, and accelerations a second squared's.
/// </summary>
/// <param name="OutdoorLength">The boom's length outdoors: the original's 5.</param>
/// <param name="OutdoorPitch">Its pitch outdoors: the original's 15°.</param>
/// <param name="InteriorLength">Its length where the chunk under him doesn't draw the sky.</param>
/// <param name="InteriorPitch">Its pitch there.</param>
/// <param name="PlaceEase">How long the length and pitch take to ease most of the way (63%) to a new chunk's.</param>
/// <param name="Height">How far above the player's sphere's centre the pivot is: the original's aim, 1.05.</param>
/// <param name="Radius">The camera's sphere, which the spring arm sweeps.</param>
/// <param name="Margin">How far short of a wall the arm stops, past the sphere.</param>
/// <param name="ArmOut">The fastest the arm grows back once the wall is gone. It comes in at once.</param>
/// <param name="ArmAccel">How fast that may change.</param>
/// <param name="ArmIn">The fastest the arm comes in ahead of a wall it sees coming; the backstop for one it doesn't is at once.</param>
/// <param name="ArmInAccel">How fast that may change.</param>
/// <param name="ArmInTime">How long coming in takes to go 90% of the way.</param>
/// <param name="ArmLookahead">How far ahead the arm looks for the room it will need, seconds of the yaw's turn and his travel.</param>
/// <param name="ArmTurnLookahead">How far ahead, in seconds, the arm looks along the yaw's turn (his travel takes ArmLookahead).</param>
/// <param name="ArmPlansSwing">Whether, in a swing, the look ahead takes the yaw as going round at the swing's rate rather than at its present speed.</param>
/// <param name="WhiskerSpread">How far round either side of the yaw's goal its whiskers sample the room; 0 turns them off.</param>
/// <param name="WhiskerEase">How long the whiskers' push takes to ease most of the way (63%) to a new value.</param>
/// <param name="ArmEase">How long the arm's easing out takes to go 90% of the way.</param>
/// <param name="CloseLift">How far the pivot rises above his head with the arm at nothing, in proportion as it's shortened.</param>
/// <param name="ClosePitch">How much the pitch rises with the arm at nothing, in proportion, no higher than the way ahead allows.</param>
/// <param name="AheadSight">How far ahead of him, at head height, the way ahead is kept in frame: the pitch is capped for it.</param>
/// <param name="LensHeight">The runtime's lens's height in degrees.</param>
/// <param name="SightMargin">How far inside the lens's top edge the way ahead is kept.</param>
/// <param name="FadeFrom">How near the camera may come to his head with his model whole.</param>
/// <param name="FadeGone">How near, with his model gone.</param>
/// <param name="OpenTurn">The fastest the yaw trails him with room all round.</param>
/// <param name="OpenTurnAccel">How fast that turn may change.</param>
/// <param name="TightTurn">The swing's peak rate: the fastest it swings round, an unchosen view behind him or with no room to back off into.</param>
/// <param name="TightTurnAccel">How fast that turn may change: the swing's ease in.</param>
/// <param name="SettleTime">The follow's settle: how long its smoothed approach takes to go 90% of the way from rest (half in about a third of it, at rest to half a degree in about twice it).</param>
/// <param name="FollowTime">The same while he walks, trailing his travel; it eases over to SettleTime as he stops.</param>
/// <param name="SneakTime">The same for sneaking's swing to the hunter's far side.</param>
/// <param name="SwingTime">The same for the swing behind him.</param>
/// <param name="PitchSpeed">The fastest the pitch moves on its own.</param>
/// <param name="PitchAccel">How fast that may change.</param>
/// <param name="MovingSpeed">How fast he must travel for the yaw to trail him: slower, it holds.</param>
/// <param name="InputWait">How long after the player last turned the camera the automatic yaw and pitch wait.</param>
/// <param name="StickTurn">Degrees a 30 fps frame the camera turns with the stick full over: the original's orbit's 3.</param>
/// <param name="StickPitch">Degrees a frame it pitches with the stick full up or down.</param>
/// <param name="LowestPitch">The lowest pitch the player can turn it to.</param>
/// <param name="HighestPitch">The highest pitch, the player's or close quarters'.</param>
/// <param name="PivotSpeed">The fastest it closes, units a second.</param>
/// <param name="PivotTime">How long its smoothed approach takes to go 90% of the way.</param>
/// <param name="PivotAccel">How fast its speed may change, units a second².</param>
/// <param name="CloseEase">How long close quarters (the lift and the raised pitch) take to ease most of the way (63%) to the room about him.</param>
/// <param name="UnchosenSwing">How far off behind his travel a view he didn't choose must be, as he sets off, to swing behind at the tight rate.</param>
/// <param name="ArrivalShortfall">How far short of the place's length an arrival's arm may be and still count as reaching it.</param>
/// <param name="ArrivalStep">How far round from behind him each angle an arrival tries is from the last.</param>
/// <param name="ArrivalShortest">The share of the length below which no angle will do and the arrival is lifted behind him; also how short the arm along an unchosen swing's shorter way must close for it to go the other way.</param>
/// <param name="CloseFloor">The shortest arm a close arrival frame, against a wall, may have; close quarters (the lift) begin below it.</param>
/// <param name="RearBand">How far round from behind him the arrival's rear band reaches.</param>
/// <param name="SideBand">How far its side band reaches; the front band is the rest.</param>
/// <param name="LeanBound">The most all the yaw's leans together add to its goal (slices 2 and 3: the path's bend, the nudges).</param>
/// <param name="BendRays">How many level sweeps the path's bend fans ahead of his travel (<see cref="PathBendLean"/>).</param>
/// <param name="BendSpread">How far either side of his travel the fan reaches.</param>
/// <param name="BendReach">How long each sweep is.</param>
/// <param name="BendRadius">The sweeps' sphere.</param>
/// <param name="BendNear">How free straight ahead may be for the bend's lean to be whole; open to the full reach, it's none.</param>
/// <param name="BendGain">The lean's share of the open heading's angle off his travel.</param>
/// <param name="BendBound">The most the bend leans the yaw's goal.</param>
/// <param name="BendEase">How long the bend's lean takes to ease most of the way (63%) to a new value.</param>
/// <param name="BendRate">The fastest the bend's lean changes.</param>
/// <param name="SlopeShare">The share of the ground's slope ahead the pitch follows: down a little going downhill.</param>
/// <param name="SlopeAhead">How far ahead of him the ground's slope is measured.</param>
/// <param name="SlopeBound">The most the slope moves the pitch's goal.</param>
/// <param name="SlopeEase">How long that takes to ease most of the way.</param>
/// <param name="NudgeBound">The most the nudges lean the yaw's goal (<see cref="NudgeLean"/>).</param>
/// <param name="NudgeReach">How far the use button's choice is asked for, and where its nudge fades to nothing.</param>
/// <param name="HunterReach">Where a hunter's nudge fades to nothing.</param>
/// <param name="HunterShare">The share of its lean a hunter's nudge gives: the stick being the camera's, a whole one drew a walker holding forward toward it.</param>
/// <param name="NudgeEase">How long a nudge takes to ease most of the way.</param>
/// <param name="NudgeRate">The fastest a nudge changes.</param>
/// <param name="SneakLength">How much longer the boom is, sneaking past a hunter (<see cref="SneakFraming"/>).</param>
/// <param name="SneakPitch">How much higher its pitch is.</param>
/// <param name="SneakTurn">The fastest the yaw swings to the hunter's far side.</param>
/// <param name="SneakTurnAccel">How fast that swing may change.</param>
/// <param name="SneakIn">How long sneaking's framing takes to take hold.</param>
/// <param name="SneakOut">How long it takes to let go.</param>
/// <param name="SneakSwitch">The share of the held hunter's distance another must be within to be framed instead.</param>
public sealed record TrekCameraTuning(
    float OutdoorLength = 5,
    float OutdoorPitch = 15,
    float InteriorLength = 3.5f,
    float InteriorPitch = 20,
    float PlaceEase = 0.75f,
    float Height = 1.05f,
    float Radius = 0.25f,
    float Margin = 0.05f,
    float ArmOut = 1.5f,
    float ArmAccel = 3,
    float ArmEase = 1.5f,
    float ArmIn = 12,
    float ArmInAccel = 80,
    float ArmInTime = 0.4f,
    float ArmLookahead = 0.6f,
    float ArmTurnLookahead = 0.5f,
    bool ArmPlansSwing = true,
    float WhiskerSpread = 120,
    float WhiskerEase = 0.6f,
    float CloseLift = 1.6f,
    float ClosePitch = 25,
    float AheadSight = 8,
    float LensHeight = 45,
    float SightMargin = 4,
    float FadeFrom = 1.2f,
    float FadeGone = 0.5f,
    float OpenTurn = 60,
    float OpenTurnAccel = 90,
    float TightTurn = 90,
    float TightTurnAccel = 240,
    float SettleTime = 2.3f,
    float FollowTime = 1.5f,
    float SneakTime = 1,
    float SwingTime = 1.6f,
    float PitchSpeed = 30,
    float PitchAccel = 120,
    float MovingSpeed = 0.5f,
    float InputWait = 2,
    float StickTurn = 3,
    float StickPitch = 1.5f,
    float LowestPitch = -10,
    float HighestPitch = 70,
    float PivotSpeed = 8,
    float PivotTime = 0.3f,
    float PivotAccel = 30,
    float CloseEase = 0.4f,
    float UnchosenSwing = 90,
    float ArrivalShortfall = 0.15f,
    float ArrivalStep = 20,
    float ArrivalShortest = 0.35f,
    float CloseFloor = 2,
    float RearBand = 60,
    float SideBand = 110,
    float LeanBound = 40,
    float BendRays = 13,
    float BendSpread = 75,
    float BendReach = 12,
    float BendRadius = 0.2f,
    float BendNear = 4,
    float BendGain = 1,
    float BendBound = 35,
    float BendEase = 0.7f,
    float BendRate = 20,
    float SlopeShare = 0.5f,
    float SlopeAhead = 2.5f,
    float SlopeBound = 5,
    float SlopeEase = 0.6f,
    float NudgeBound = 12,
    float NudgeReach = 8,
    float HunterReach = 15,
    float HunterShare = 0.3f,
    float NudgeEase = 0.5f,
    float NudgeRate = 12,
    float SneakLength = 1,
    float SneakPitch = 5,
    float SneakTurn = 240,
    float SneakTurnAccel = 720,
    float SneakIn = 0.4f,
    float SneakOut = 0.8f,
    float SneakSwitch = 0.8f)
{
    public static TrekCameraTuning Default { get; } = new();

    /// <summary>
    /// These numbers with some set by name, <c>Name=value</c> joined by commas, as <c>--trek-camera enhanced,OpenTurn=60</c> gives
    /// them: any member of the record, found by reflection, so a new member needs no change here.
    /// </summary>
    public TrekCameraTuning Parse(string settings)
    {
        var tuning = this with { };
        foreach (var part in settings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"'{part}' isn't Name=value.");
            var (name, value) = (part[..eq].Trim(), part[(eq + 1)..].Trim());
            var property = typeof(TrekCameraTuning).GetProperties().FirstOrDefault(p => p.CanWrite && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"The trek camera has no setting '{name}'.");
            object parsed = property.PropertyType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(bool) ? bool.Parse(value)
                : property.PropertyType == typeof(float[]) ? value.Split(';').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()
                : property.PropertyType == typeof(string) ? value
                : throw new FormatException($"'{name}' can't be set.");
            property.SetValue(tuning, parsed);
        }
        return tuning;
    }
}
