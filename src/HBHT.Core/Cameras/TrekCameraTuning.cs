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
/// <param name="ArmOut">How fast the arm grows back once the wall is gone. It comes in at once.</param>
/// <param name="CloseLift">How far the pivot rises above his head with the arm at nothing, in proportion as it's shortened.</param>
/// <param name="ClosePitch">How much the pitch rises with the arm at nothing, in proportion.</param>
/// <param name="OpenTurn">The fastest the yaw trails him with room all round.</param>
/// <param name="OpenTurnAccel">How fast that turn may change.</param>
/// <param name="TightTurn">The fastest it swings round with no room to back off into: a corridor walked along at the lens.</param>
/// <param name="TightTurnAccel">How fast that turn may change.</param>
/// <param name="TurnSettle">How quickly the last of a turn closes, a second's share, so it eases onto its goal.</param>
/// <param name="PitchSpeed">The fastest the pitch moves on its own.</param>
/// <param name="PitchAccel">How fast that may change.</param>
/// <param name="MovingSpeed">How fast he must travel for the yaw to trail him: slower, it holds.</param>
/// <param name="InputWait">How long after the player last turned the camera the automatic yaw and pitch wait.</param>
/// <param name="StickTurn">Degrees a 30 fps frame the camera turns with the stick full over: the original's orbit's 3.</param>
/// <param name="StickPitch">Degrees a frame it pitches with the stick full up or down.</param>
/// <param name="LowestPitch">The lowest pitch the player can turn it to.</param>
/// <param name="HighestPitch">The highest pitch, the player's or close quarters'.</param>
/// <param name="PivotRate">The share of the way the pivot closes on the player a tick.</param>
/// <param name="PivotSpeed">The fastest it closes, units a second.</param>
/// <param name="ArrivalSwing">How long after an arrival it may swing round as quickly as in a tight space.</param>
/// <param name="ArrivalHold">How long a teleporter's own camera point is held, standing, before it swings round behind him.</param>
/// <param name="LeanBound">The most all the yaw's leans together add to its goal (slices 2 and 3: the path's bend, the nudges).</param>
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
    float CloseLift = 0.6f,
    float ClosePitch = 25,
    float OpenTurn = 40,
    float OpenTurnAccel = 90,
    float TightTurn = 270,
    float TightTurnAccel = 1080,
    float TurnSettle = 2,
    float PitchSpeed = 30,
    float PitchAccel = 120,
    float MovingSpeed = 0.5f,
    float InputWait = 2,
    float StickTurn = 3,
    float StickPitch = 1.5f,
    float LowestPitch = -10,
    float HighestPitch = 70,
    float PivotRate = 0.12f,
    float PivotSpeed = 8,
    float ArrivalSwing = 1.5f,
    float ArrivalHold = 0.5f,
    float LeanBound = 20)
{
    public static TrekCameraTuning Default { get; } = new();
}
