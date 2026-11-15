using System.Numerics;
using HBHT.Core.Audio;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using static HBHT.Rom.Tests.TestBattles;

namespace HBHT.Rom.Tests;

/// <summary>
/// Mouse look in a battle's turns (ADR 0040), where its orbit is held on every tick, the mouse's drag turning it only on the ticks
/// it moves: the stick must be read against the camera drawn, the turn taken in hand must start from the shot shown rather than
/// jump, and A must still attack, since the orbit isn't R.
/// </summary>
public sealed class MouseLookBattleTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static float Degrees(Vector2 a, Vector2 b) =>
        MathF.Abs(MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b))) * 180 / MathF.PI;

    [Theory]
    [InlineData(CombatTurns.StartEarly)]
    [InlineData(CombatTurns.Original)]
    public void MouseLookReadsTheStickByTheCameraDrawnAndTakesItInHandWithoutAJump(CombatTurns turns)
    {
        var audio = GameAudio.Silent(Set);
        var recorder = new BattleRecorder(audio);
        var built = Plain(Set, recorder);
        var battle = built.Battle;
        var zone = new ZoneEngine(built.Game, built.Streamer);
        zone.Keys.Output = recorder;
        recorder.Keys = zone.Keys;
        var early = turns == CombatTurns.StartEarly;
        if (!early)
            battle.CombatCamera.Style = Core.Cameras.CameraStyle.Enhanced;
        var drawing = new BattleDrawing(battle, recorder, null, audio) { Budget = null, TicksAhead = 97, Turns = turns };
        zone.Camera = drawing.Camera;
        EnhancedCamera Enhanced() => early ? drawing.Director.Camera : battle.CombatCamera.Enhanced!;

        Combatant? turn = null;
        var (turnTick, worstApart, worstJump, releases, attacks, worstBack) = (0, 0f, 0f, 0, 0, 0f);
        float? turnedTo = null;
        var turnedWay = 0f;
        var wasReleased = false;

        // Walking forward for 90 ticks of each turn, the mouse turning the camera on ticks 30 to 39, then A.
        PlayerInput Read()
        {
            var drawn = early ? drawing.Camera.LookXZ : battle.CombatCamera.Enhanced?.Camera.LookXZ ?? battle.Camera.LookXZ;
            worstApart = MathF.Max(worstApart, Degrees(battle.Camera.LookXZ, drawn));
            var stick = turnTick < 90 ? new Vector2(0, 1) : Vector2.Zero;
            var drag = turnTick is >= 30 and < 40 ? new Vector2(2, 0) : Vector2.Zero;
            return new PlayerInput(stick, Orbit: true, OrbitStick: drag, Free: true);
        }

        for (var tick = 0; tick < 60 * 60 * 20; tick++)
        {
            var playing = drawing.TakesPresses && battle.WaitsOnPlayer && !battle.Over;
            if (playing && battle.Current is { Controlled: false } c && battle.State == CombatState.Turn && !battle.Fade.Fading)
            {
                if (c != turn)
                    (turn, turnTick) = (c, 0);
                if (turnTick >= 120 && !drawing.Press(MenuAction.Accept))
                    drawing.Press(MenuAction.TabPrevious);
                if (battle.State is CombatState.TargetAttack or CombatState.TargetThrown)
                    attacks++;
            }
            else if (playing && battle.State is CombatState.TargetAttack or CombatState.TargetThrown)
            {
                if (!drawing.Press(MenuAction.Accept))
                    drawing.Press(MenuAction.Back);
            }
            var before = early ? drawing.Camera.LookXZ : battle.CombatCamera.Enhanced?.Camera.LookXZ ?? default;
            var stepTurn = playing && battle.State == CombatState.Turn && battle.Current is { Controlled: false };
            var step = drawing.Tick(zone, skyDrawn: false, readInput: Read);
            if (stepTurn)
                turnTick++;
            if (Enhanced() is { } enhanced)
            {
                if (enhanced.Released && !wasReleased)
                {
                    releases++;
                    worstJump = MathF.Max(worstJump, Degrees(battle.CombatCamera.Camera.LookXZ, before));
                }
                wasReleased = enhanced.Released;
            }
            // Once the mouse stops (tick 40), how far the camera drawn swings back from the furthest it turned the mouse's way.
            if (stepTurn && Enhanced() is { Released: true })
            {
                var look = early ? drawing.Camera.LookXZ : battle.CombatCamera.Enhanced!.Camera.LookXZ;
                var yaw = MathF.Atan2(look.Y, look.X) * 180 / MathF.PI;
                if (turnTick == 40)
                    (turnedTo, turnedWay) = (yaw, Math.Sign(Wrap(yaw - Before(before))));
                else if (turnTick is > 40 and < 90 && turnedTo is { } furthest)
                {
                    var on = Wrap(yaw - furthest) * turnedWay;
                    if (on > 0)
                        turnedTo = yaw;
                    else
                        worstBack = MathF.Max(worstBack, -on);
                }
            }
            if (step is BattleStep.Paused)
                battle.Unpause(flee: true);
            if (step is BattleStep.Ended)
                break;
        }

        Assert.True(releases > 0, "The mouse never took the camera in hand.");
        Assert.True(worstApart <= 1, $"The stick was read {worstApart:F1}° from the camera drawn.");
        Assert.True(worstJump <= 10, $"Taking the camera in hand turned it {worstJump:F0}° from the shot shown.");
        Assert.True(attacks > 0, "A never began an attack under mouse look's orbit.");
        Assert.True(worstBack <= 1, $"The camera swung {worstBack:F1}° back past where the mouse left it.");

        static float Before(Vector2 look) => MathF.Atan2(look.Y, look.X) * 180 / MathF.PI;
        static float Wrap(float degrees) => (degrees % 360 + 540) % 360 - 180;
    }
}
