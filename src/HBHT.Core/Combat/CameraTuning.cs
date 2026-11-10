using System.Globalization;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// Every number the enhanced battle camera works by (<see cref="EnhancedCamera"/>), each settable by name from the command line
/// (<c>--battle-camera enhanced,Name=value,…</c>, <see cref="Parse"/>). Angles are in degrees, distances in units, accelerations a second
/// squared, times in ticks.
/// </summary>
public sealed record CameraTuning
{
    public static CameraTuning Default { get; } = new();

    // ---- The lens

    /// <summary>The vertical field of view a shot is judged by: the runtime camera's.</summary>
    public float FieldOfView { get; init; } = 45;

    /// <summary>The narrowest screen judged for, its width over its height.</summary>
    public float Aspect { get; init; } = 4f / 3;

    /// <summary>The share of the half-width and half-height a subject must sit within.</summary>
    public float FrameMargin { get; init; } = 0.85f;

    /// <summary>
    /// The share of the half-height below the middle a subject must sit within: above the conversation box, whose top is two thirds of the
    /// way down, so no one is framed under it.
    /// </summary>
    public float FrameFloor { get; init; } = 0.55f;

    // ---- Motion

    /// <summary>The cap on the aim's acceleration, and its deceleration (units a second squared). Speed isn't capped.</summary>
    public float AimAcceleration { get; init; } = 12;

    /// <summary>
    /// The cap on how fast the view's turn and pitch speed up and slow down (degrees a second squared): quick enough that a new shot's
    /// angle comes in with its place, rather than the view orbiting on once it's there.
    /// </summary>
    public float TurnAcceleration { get; init; } = 200;

    /// <summary>
    /// The cap on how fast its distance from the aim speeds up and slows down, by proportion: its logarithm's acceleration, a second squared.
    /// 1.5 takes a zoom from 7 out to 30 (or back) in about two seconds, at the same pace close in as far out.
    /// </summary>
    public float ZoomAcceleration { get; init; } = 1.5f;

    /// <summary>How quickly the last of a move closes, a second's share: a gentle landing rather than a wobble round the goal.</summary>
    public float Settle { get; init; } = 2.5f;

    /// <summary>The same for the turn and pitch, quicker, so the last of a turn doesn't trail in after the aim has landed.</summary>
    public float TurnSettle { get; init; } = 4;

    /// <summary>
    /// The same for the distance, gentler: how fast it closes on its goal by proportion, near it. It bounds how far a zoom carries on once its
    /// goal turns round, as when the next turn asks for a wide shot while the camera is still closing in.
    /// </summary>
    public float ZoomSettle { get; init; } = 1.5f;

    /// <summary>How far a goal may move in a tick and be followed at its own speed; further, it jumped.</summary>
    public float GoalJump { get; init; } = 1;

    /// <summary>
    /// The most a shot over the shoulder orbits a second, turned straight away along with the way from the character to the target as
    /// they go round it, degrees: as the shot is carried with the character, so the view doesn't trail them. Faster, it catches up after.
    /// </summary>
    public float CarryTurn { get; init; } = 90;

    /// <summary>
    /// How long the player's turn's goal takes to glide onto a shot found anew (a new framing, target or distance), seconds: 95% of the way
    /// (<see cref="Cameras.ShotGlide"/>). Every change of framing in their turn takes about this long, easing in and out, and the camera rides
    /// the glide.
    /// </summary>
    public float GlideTime { get; init; } = 1;

    /// <summary>
    /// What the caps are times while the camera rides the glide in the player's turn: the glide's own course is what keeps the motion
    /// smooth, and the caps only keep a jump far across it from whipping.
    /// </summary>
    public float GlidePace { get; init; } = 3;

    /// <summary>
    /// While gliding, how quickly the turn the shot's carried by (the orbit's and a new framing's easing) follows them, seconds: its speed
    /// follows theirs as a critically damped spring this long, so the view starts and stops turning without a kick.
    /// </summary>
    public float TurnSmoothing { get; init; } = 0.12f;

    /// <summary>
    /// How slowly the shot over the shoulder comes in to stand nearer, seconds: the time over which what's left of the way in falls by e. It
    /// draws back at once to hold what it must, so the view doesn't pump in and out as the bodies shift about.
    /// </summary>
    public float ZoomInTime { get; init; } = 1;

    /// <summary>
    /// How smoothly the camera follows whom it's carried with, seconds: the time of a critically damped spring drawing the point it
    /// follows after their body (<see cref="Cameras.FollowPoint"/>), so their stride, a step a frame, and a root stopping and pushing off,
    /// doesn't jolt the view. It trails a walker by twice this times their speed and settles as they stop, never past them. 0 follows the
    /// body exactly.
    /// </summary>
    public float FollowSmoothing { get; init; } = 0.15f;

    /// <summary>Further than this from the body it follows, the point is put on it, not drawn: a body put somewhere.</summary>
    public float FollowReach { get; init; } = 6;

    /// <summary>
    /// The furthest the shot over the shoulder turns by itself from straight behind a character who walks, degrees, the way they head:
    /// its orbit and a new framing bring the camera round to their side at most, never in front of them. Walking toward it themselves,
    /// it's left where it is, as a third person's camera is.
    /// </summary>
    public float MostAcross { get; init; } = 90;

    /// <summary>
    /// A move into an aim, about one, or back from one is a pan, not a cut: <see cref="PanTicks"/> ticks, and
    /// <see cref="PanTicksPerUnit"/> more for each unit the aim travels, no more than <see cref="PanMostTicks"/>, eased in and out. The
    /// player sees where on the field they're aiming.
    /// </summary>
    public int PanTicks { get; init; } = 20;

    public float PanTicksPerUnit { get; init; } = 0.5f;

    public int PanMostTicks { get; init; } = 40;

    /// <summary>
    /// An AI's walk, or its flight, is shown from near where it ends: the eye moves with the walker by this share of the way they walk,
    /// no more than <see cref="WalkPanMost"/> units, and turns after them, so the view turns to face them and whom they act on as they
    /// come rather than tracking alongside.
    /// </summary>
    public float WalkPan { get; init; } = 0.3f;

    /// <inheritdoc cref="WalkPan"/>
    public float WalkPanMost { get; init; } = 8;

    /// <summary>
    /// The yaws an AI's walk's end is tried from, degrees turned from straight ahead of the walker (they come at the eye); its pitches are
    /// <see cref="PlannedPitches"/>.
    /// </summary>
    public float[] WalkYaws { get; init; } = [40, -40, 60, -60, 80, -80, 100, -100];

    /// <summary>
    /// How far either side along an AI's walk its rail's points are averaged over, units, rounding its corners: the way its shot rides
    /// (<see cref="EnhancedCamera"/>).
    /// </summary>
    public float WalkRound { get; init; } = 1.5f;

    /// <summary>
    /// How smoothly an AI's walk's shot goes along its rail after the walker, seconds: how far along they are is tracked, place and speed,
    /// as a critically damped pair this long, which doesn't trail a steady walker, so their stride's unevenness doesn't shake it.
    /// </summary>
    public float WalkSmoothing { get; init; } = 0.3f;

    /// <summary>
    /// The caps times this while the player has the camera in their turn, so the view follows their own hand closely and still swoops,
    /// quicker, from a wide shot.
    /// </summary>
    public float HandAcceleration { get; init; } = 10;

    /// <summary>The aim's cap times this while the player aims, so the reticle stays near the middle of the screen as it moves.</summary>
    public float AimingAcceleration { get; init; } = 6;

    /// <summary>
    /// The most of a moving goal's own speed followed at while the player has the camera in their hand, units a second: a running body's.
    /// The director's own shots follow none of it: they trail a moving goal, by as much as it takes to stop on it, so the view never
    /// overshoots a goal that stops and swings back.
    /// </summary>
    public float FollowSpeed { get; init; } = 6;

    /// <summary>The same for the turn, degrees a second.</summary>
    public float FollowTurn { get; init; } = 90;

    /// <summary>
    /// While its aim has far to go, the camera stands back at least this times the way left, so a move across the field is a swoop that
    /// keeps both ends in view rather than a close pan over the ground between; it comes down as it arrives. 0 for none, the default: a
    /// long move is cut instead (<see cref="CutDistance"/>, <see cref="CutTurn"/>).
    /// </summary>
    public float TravelZoom { get; init; }

    /// <summary>The nearest the eye comes to what it aims at.</summary>
    public float NearestDistance { get; init; } = 3;

    /// <summary>A goal whose aim is further than this from the camera's is cut to rather than moved to: a move across the field.</summary>
    public float CutDistance { get; init; } = 30;

    /// <summary>A goal turned further than this from the camera's yaw, degrees, is cut to as well: a swing round to the other side.</summary>
    public float CutTurn { get; init; } = 120;

    /// <summary>
    /// A goal pitched further than this from the camera's, degrees, is cut to: into or out of an aim's or an area's view from above, which
    /// moved to under the caps spends seconds looking at the ground between.
    /// </summary>
    public float CutPitch { get; init; } = 35;

    /// <summary>
    /// The player's turn begins with a cut to its shot unless that's nearer than this to the shot shown, and turned less than
    /// <see cref="EnterCutTurn"/> degrees, or the shot shown doesn't hold the character: the turn begins with them in view, and a move
    /// a long way from the last turn's shot swept the eye past them.
    /// </summary>
    public float EnterCutDistance { get; init; } = 8;

    /// <inheritdoc cref="EnterCutDistance"/>
    public float EnterCutTurn { get; init; } = 30;

    /// <summary>How far beyond the battlefield's grid a director's shot may put its eye: the walls and cliffs round it are scenery without collision.</summary>
    public float FieldMargin { get; init; } = 1;

    /// <summary>How far above the ground the eye is kept.</summary>
    public float FloorClearance { get; init; } = 1;

    // ---- Sight lines

    /// <summary>The sphere a sight line is swept with against the arena's collision.</summary>
    public float SightRadius { get; init; } = 0.1f;

    /// <summary>A body across a sight line hides what's behind it, taken as this times its radius; 0 for bodies hiding nothing.</summary>
    public float BodyOcclusion { get; init; } = 1.2f;

    /// <summary>
    /// Taken off a shot's score, times a subject's weight, for each subject it holds but can't see. A body is seen while its upper half is:
    /// the sight lines to its middle and its top are both clear (<see cref="SightLines.Hidden"/>), its legs hidden or not.
    /// </summary>
    public float OcclusionPenalty { get; init; } = 2;

    // ---- Events: the original's shots, kept clear

    /// <summary>Ticks between looks at whether the shot's subjects are hidden, and at a better framing.</summary>
    public int DecideEvery { get; init; } = 15;

    /// <summary>Turns from the original's shot tried when it hides a subject, degrees.</summary>
    public float[] EventYaws { get; init; } = [25, -25, 50, -50, 80, -80, 120, -120, 180];

    /// <summary>Raises of its pitch tried with each, degrees.</summary>
    public float[] EventPitches { get; init; } = [0, 15, 30];

    /// <summary>Times its distance tried with each.</summary>
    public float[] EventScales { get; init; } = [1, 1.3f];

    /// <summary>Looks in a row the original's shot must be clear before the camera goes back to it.</summary>
    public int ClearChecks { get; init; } = 3;

    // ---- An AI's turn: one shot, planned

    /// <summary>The step between the yaws tried for an AI's turn, all the way round, degrees.</summary>
    public float PlannedYawStep { get; init; } = 15;

    /// <summary>Pitches tried for an AI's turn, degrees.</summary>
    public float[] PlannedPitches { get; init; } = [12, 20, 30, 40];

    /// <summary>The pitch liked best for an AI's turn, degrees.</summary>
    public float PlannedPitch { get; init; } = 20;

    /// <summary>
    /// Taken off for each radian an AI's turn's shot is turned from the next player's turn's likely shot (over that character's shoulder at
    /// its nearest enemy), beside <see cref="TurnCost"/> for the turn from the shot now: the way that disrupts both least.
    /// </summary>
    public float NextTurnCost { get; init; } = 0.4f;

    // ---- The player's targets

    /// <summary>Half the cone, about the way the character points (or walks), an enemy must be in to be a target.</summary>
    public float ConeAngle { get; init; } = 20;

    /// <summary>Half the cone the target held stays in, wider, so the stick's wobble doesn't drop it.</summary>
    public float ConeHold { get; init; } = 45;

    /// <summary>
    /// The farthest across the ground an enemy may be to be a target. Farther, no shot over the shoulder holds both within
    /// <see cref="MaxDistance"/>, and the camera follows behind the character as the original's does.
    /// </summary>
    public float TargetRange { get; init; } = 30;

    /// <summary>The farthest the target held may be and stay it, further, so walking at the edge of the range doesn't flick it.</summary>
    public float TargetRangeHold { get; init; } = 38;

    /// <summary>
    /// Ticks the target held stays it once it has left its cone or its range, unless the player turns back to it: a stick held across
    /// the edge doesn't flick the camera between it and nothing.
    /// </summary>
    public int LoseHold { get; init; } = 45;

    /// <summary>
    /// How many ticks a target let go is still shot over the shoulder at, with none in its place: so heading past one toward another goes
    /// straight from one to the other, not through the trailing shot between them.
    /// </summary>
    public int TrailAfter { get; init; } = 30;

    /// <summary>
    /// The target held stays it while it's within this across the ground and the character doesn't head away from it by more than
    /// <see cref="HoldNearAngle"/>: running round a pack for a backstab keeps the camera on it.
    /// </summary>
    public float HoldNear { get; init; } = 10;

    /// <summary>
    /// How far from the way to the target held near the character may head and keep it, degrees: round it, not away from it. Walking past
    /// it or away, it's let go as one out of its cone is (<see cref="LoseHold"/>), and the shot stops orbiting it.
    /// </summary>
    public float HoldNearAngle { get; init; } = 110;

    /// <summary>Enemies within this of the target, across the ground, are framed with it: a pack is shown whole, centred.</summary>
    public float ClusterRadius { get; init; } = 8;

    /// <summary>
    /// The pack is framed only while the target is within this of the character; further, the shot looks at its middle but holds only the
    /// target, small beyond the character, rather than drawing back to hold them all.
    /// </summary>
    public float PackRange { get; init; } = 14;

    /// <summary>The weight of the target's pack, each, beside the character's and the target's 1.</summary>
    public float ClusterWeight { get; init; } = 0.5f;

    /// <summary>
    /// Over how much of <see cref="ClusterRadius"/> (from the target) and <see cref="PackRange"/> (from the character) a body fades into the
    /// pack and out of it, units: so it comes into the framing and the aim's lead bit by bit, not all at once.
    /// </summary>
    public float PackFade { get; init; } = 3;

    /// <summary>
    /// Taken off a shot, up to this, for framing two who deal with each other, a party member and their enemy, the other way round from
    /// how the battle's vantage sees them (<see cref="ScreenSide"/>, <see cref="Vantage"/>).
    /// </summary>
    public float SideCost { get; init; } = 1.5f;

    /// <summary>
    /// How far right of their enemy a party member is framed on the right before it costs nothing, as a share of the lens's half-width
    /// (on the left, as far left): over the shoulder, off to their side.
    /// </summary>
    public float SideAcross { get; init; } = 0.3f;

    /// <summary>
    /// How much how near the cone's middle an enemy is counts, <c>1 − (angle/ConeAngle)²</c> times this, beside how near it is
    /// (<see cref="NearScale"/>): within the cone, the nearer is likelier.
    /// </summary>
    public float AngleWeight { get; init; } = 0.5f;

    /// <summary>How fast a target's worth falls with its distance: nearer is likelier.</summary>
    public float NearScale { get; init; } = 6;

    /// <summary>
    /// How much likelier another enemy must be than the target held to take its place. Within <see cref="StrikeRange"/>, how much nearer,
    /// units.
    /// </summary>
    public float TargetMargin { get; init; } = 0.5f;

    /// <summary>
    /// Ticks in a row another enemy must be that much likelier to take the target's place, or, with none held, the likeliest to be taken:
    /// this many for one within <see cref="TargetQuickRange"/>, rising to <see cref="TargetHoldFar"/> for one at <see cref="TargetRange"/>.
    /// So a new target near is taken quickly, one across the field only once the player has headed for it a while.
    /// </summary>
    public int TargetHold { get; init; } = 15;

    /// <inheritdoc cref="TargetHold"/>
    public int TargetHoldFar { get; init; } = 90;

    /// <inheritdoc cref="TargetHold"/>
    public float TargetQuickRange { get; init; } = 10;

    /// <summary>
    /// Within this of the character across the ground, an enemy is a target by how near it is alone, wherever in front of them it is (not
    /// headed away from by more than <see cref="HoldNearAngle"/>): among a pack within reach, whom the camera looks at hardly matters, and
    /// the shot over the shoulder doesn't orbit it (<see cref="ShoulderNear"/>).
    /// </summary>
    public float StrikeRange { get; init; } = 5;

    // ---- The vantage: the side every shot is taken from

    /// <summary>
    /// How far the battle's vantage stands off the line between the party's middle and the enemies', times the line's length, and the
    /// least it stands off it (<see cref="Vantage"/>).
    /// </summary>
    public float VantageReach { get; init; } = 0.6f;

    /// <inheritdoc cref="VantageReach"/>
    public float VantageNearest { get; init; } = 10;

    /// <summary>
    /// The vantage is placed again once the line between the party and the enemies has turned this far, degrees, from the one it was
    /// placed for, as well as when a reserve comes onto the field, or the line's middle or length has moved by more than half the reach
    /// it was placed with.
    /// </summary>
    public float VantageTurn { get; init; } = 60;

    // ---- An arrow, a throw or a spell at someone further off: loosed, then the hit

    /// <summary>An action at someone further than this from the one who acts is shown as two shots: the loosing, then the hit.</summary>
    public float ReleaseDistance { get; init; } = 6;

    /// <summary>The share of its way an arrow, a throw or a flask has flown when the camera cuts to its target.</summary>
    public float HitAt { get; init; } = 0.5f;

    /// <summary>Ticks after a spell's visual starts at its caster's strike that the camera cuts to its target.</summary>
    public int ReleaseBeat { get; init; } = 10;

    /// <summary>
    /// The fewest ticks the loosing is shown before the cut to the hit, unless the blow lands first: a spell's visual may start early in its
    /// caster's cast, and cut to so soon the loosing was gone before it was seen.
    /// </summary>
    public int LoosingTicks { get; init; } = 45;

    /// <summary>
    /// How far the hit's shot is turned from looking along the way the missile came, degrees, toward the vantage's side: the target seen
    /// three-quarters on, the missile coming in from the side the loosing was shot from.
    /// </summary>
    public float HitTurn { get; init; } = 35;

    // ---- Framing the character and the target: over the shoulder

    /// <summary>Pitches tried, degrees: low first, over the shoulder. None from high above, which loses the third person's feel.</summary>
    public float[] Pitches { get; init; } = [4, 8, 12, 18, 24, 30];

    /// <summary>
    /// The shot over the shoulder doesn't orbit a target within <see cref="StrikeRange"/> (<see cref="CarryTurn"/>), and orbits one further
    /// only as far as it's beyond that, in full this much beyond: near, the way over their shoulder at it means little, and would swing
    /// round as they step about it.
    /// </summary>
    public float ShoulderNear { get; init; } = 3;

    /// <summary>
    /// How long a new framing takes to swing onto, seconds, its turn, pitch and distance easing in and out along the smoothest way there (a
    /// minimum-jerk curve): over the shoulder, found only once the character or the target has left the one held, and, with no enemy
    /// ahead, a view cleared of what hides the character. Longer for a swing so far round it would turn faster than
    /// <see cref="FramingTurn"/>.
    /// </summary>
    public float SwingTime { get; init; } = 2;

    /// <summary>
    /// How many ticks in a row the framing held over the shoulder may hide the character or the target before it's found again: a body
    /// passing between them for a moment doesn't swing the view. One out of the frame is found again at once.
    /// </summary>
    public int HiddenPatience { get; init; } = 45;

    /// <summary>The fastest a swing onto a new framing turns, degrees a second (<see cref="SwingTime"/>).</summary>
    public float FramingTurn { get; init; } = 60;

    /// <summary>Turns tried from straight over the character's shoulder at the target (or from the view now, with none), degrees.</summary>
    public float[] Yaws { get; init; } = [0, 15, -15, 30, -30, 50, -50, 80, -80, 120, -120];

    /// <summary>
    /// The turns tried in the player's turn and its aim at a blow: no further round than over the shoulder, so the character is seen from
    /// behind, never from the side or the front, however the pack stands.
    /// </summary>
    public float[] ShoulderYaws { get; init; } = [0, 15, -15, 30, -30, 45, -45];

    /// <summary>The pitch liked best, degrees: over the shoulder.</summary>
    public float PreferredPitch { get; init; } = 12;

    /// <summary>The nearest and farthest a framed shot goes from its aim.</summary>
    public float MinDistance { get; init; } = 6;

    /// <inheritdoc cref="MinDistance"/>
    public float MaxDistance { get; init; } = 35;

    /// <summary>
    /// The farthest a shot over the shoulder goes from its aim, which is near the character: the character stays large, the target
    /// small beyond them if it's far off.
    /// </summary>
    public float ShoulderDistance { get; init; } = 14;

    /// <summary>
    /// A shot over the shoulder aims this share of the way from the character to whom they head for, but no further than
    /// <see cref="AimLeadMost"/>: the character in the lower part of the screen, what's ahead above them.
    /// </summary>
    public float AimLead { get; init; } = 0.35f;

    /// <inheritdoc cref="AimLead"/>
    public float AimLeadMost { get; init; } = 3;

    /// <summary>
    /// With no enemy ahead, how far ahead of the character the aim leads, as how long from now: where the point that follows them will be
    /// then, seconds, no further than <see cref="AimLeadMost"/>. So it's on them standing, a little ahead walking and more running.
    /// </summary>
    public float LeadTime { get; init; } = 0.6f;

    /// <summary>
    /// How slowly the aim's lead from the character changes in their turn, seconds: it follows where it's wanted (toward the pack over the
    /// shoulder, the way they go with no enemy ahead) as a critically damped spring this long, so it neither jumps nor swings.
    /// </summary>
    public float LeadSmoothing { get; init; } = 0.25f;

    /// <summary>Taken off for each radian a framing turns the view from where it is.</summary>
    public float TurnCost { get; init; } = 0.4f;

    /// <summary>
    /// Taken off for each radian of pitch from the one liked best. A radian of it costs less than a subject hidden
    /// (<see cref="OcclusionPenalty"/>), so a shot goes high only where no lower one sees the target.
    /// </summary>
    public float PitchCost { get; init; } = 1.2f;

    /// <summary>Taken off for each unit of distance.</summary>
    public float DistanceCost { get; init; } = 0.01f;

    /// <summary>Taken off for each radian the eye is turned from straight behind the character, looking at the target.</summary>
    public float ShoulderCost { get; init; } = 0.8f;

    /// <summary>How much better another framing must score to replace the one held.</summary>
    public float SwitchMargin { get; init; } = 0.3f;

    // ---- Aiming

    /// <summary>The pitch a ranged aim looks down from, degrees.</summary>
    public float AimPitch { get; init; } = 55;

    /// <summary>The pitch an area spell's placement looks down from, degrees: near enough straight down, with the screen's up still the stick's.</summary>
    public float TopDownPitch { get; init; } = 80;

    /// <summary>How far an aiming shot stands from the reticle, which it keeps in the middle of the screen; further only to hold its ring.</summary>
    public float AimDistance { get; init; } = 20;

    /// <summary>
    /// The share of the half-height below the middle an area's ring may come down to as it's placed, as near as holds it: to the
    /// conversation box's top (<see cref="FrameFloor"/> is above it), so the ring fills the screen above the box.
    /// </summary>
    public float AreaFloor { get; init; } = 0.64f;

    /// <summary>
    /// The weight of a spell's reach, a point of its ring beyond its targets, as the spell is aimed at targets (state 10): some of the ring is
    /// shown where it can be, beside all of the targets and the caster.
    /// </summary>
    public float ReachWeight { get; init; } = 0.5f;

    /// <summary>
    /// The most the reach may draw an aimed spell's shot back, times the distance holding the caster and the targets alone: a wider
    /// spell's ring, far beyond its targets, isn't shown, so they aren't small and at the screen's edge.
    /// </summary>
    public float ReachStretch { get; init; } = 1.4f;

    /// <summary>The caps on the camera's motion.</summary>
    // ---- Start Early's director (BattleDirector, BeatShots)

    /// <summary>
    /// How far ahead a beat to come is held, as a share over the time the aim takes to cross to it at its cap: a beat is in the goal from
    /// <c>PlanSlack × 2√(d / AimAcceleration)</c> seconds before it strikes, plus <see cref="PlanMargin"/>, so the camera arrives as it lands.
    /// </summary>
    public float PlanSlack { get; init; } = 1.3f;

    /// <summary>The ticks a beat to come is held before it strikes beyond its crossing (<see cref="PlanSlack"/>).</summary>
    public int PlanMargin { get; init; } = 20;

    /// <summary>The most ticks ahead a beat to come is held: four seconds.</summary>
    public int PlanMost { get; init; } = 240;

    /// <summary>
    /// The ticks before a beat strikes from which, if the shot shown still doesn't hold it, the director cuts to one that does, so no result
    /// is ever off the screen. The planned move usually lands first.
    /// </summary>
    public int BeatCutAhead { get; init; } = 10;

    /// <summary>The farthest the eye stands from the beats one shot holds (<see cref="BeatShots"/>): wide shots are welcome.</summary>
    public float BeatDistance { get; init; } = 40;

    /// <summary>The pitches a shot holding several beats is tried at, low across the field first, up to near enough straight down.</summary>
    public float[] BeatPitches { get; init; } = [20, 35, 50, 65, 80];

    /// <summary>The step, degrees, of the yaws all the way round that a shot holding several beats is tried at.</summary>
    public float BeatYawStep { get; init; } = 30;

    /// <summary>The pitch a shot holding several beats is liked best at.</summary>
    public float BeatPitch { get; init; } = 30;

    /// <summary>What an action's combatant counts for beside the result it deals, in a shot holding beats: shown where it fits.</summary>
    public float SourceWeight { get; init; } = 0.5f;

    /// <summary>The radius a turn's combatant is held by when the composer asks whether its turn can open on a beat.</summary>
    public float TurnRadius { get; init; } = 0.6f;

    public MotionCaps Caps => new(AimAcceleration, TurnAcceleration, ZoomAcceleration, Settle, TurnSettle, ZoomSettle);

    /// <summary>The lens shots are judged by.</summary>
    public Lens Lens => new(FieldOfView, Aspect, FrameMargin) { Floor = FrameFloor };

    /// <summary>
    /// These numbers with some set by name, as <c>AimAcceleration=20,ConeAngle=30</c>: any above, its name's case ignored. The lists
    /// (<see cref="Pitches"/>, <see cref="Yaws"/>, <see cref="ShoulderYaws"/>, <see cref="WalkYaws"/>, the event's and the planned's) take values joined with <c>;</c>.
    /// </summary>
    public CameraTuning Parse(string settings)
    {
        var tuning = this with { };
        foreach (var part in settings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"'{part}' isn't Name=value.");
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            var property = typeof(CameraTuning).GetProperties().FirstOrDefault(p => p.CanWrite && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"The camera has no setting '{name}'.");
            object parsed = property.PropertyType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float[]) ? value.Split(';').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()
                : throw new FormatException($"'{name}' can't be set.");
            property.SetValue(tuning, parsed);
        }
        return tuning;
    }

    /// <summary>Every setting that differs from the defaults, as <see cref="Parse"/> reads them.</summary>
    public override string ToString()
    {
        var changed = new List<string>();
        foreach (var p in typeof(CameraTuning).GetProperties())
        {
            if (!p.CanWrite)
                continue;
            var (mine, theirs) = (Text(p.GetValue(this)), Text(p.GetValue(Default)));
            if (mine != theirs)
                changed.Add($"{p.Name}={mine}");
        }
        return changed.Count == 0 ? "defaults" : string.Join(',', changed);

        static string Text(object? value) => value is float[] list
            ? string.Join(';', list.Select(v => v.ToString(CultureInfo.InvariantCulture)))
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }
}
