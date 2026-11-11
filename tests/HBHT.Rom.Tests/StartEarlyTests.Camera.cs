using System.Globalization;
using System.Text;
using HBHT.Core.Cameras;
using HBHT.Core.Combat;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// How Start Early's director's camera moves, through the user's bandits' fight played as a player plays it (docs/systems/cameras.md,
/// "Start Early's director", the user's second play).
/// </summary>
public sealed partial class StartEarlyTests
{
    /// <summary>
    /// The player as they play: looks a moment as each turn opens, walks forward, stops, then attacks as <see cref="Attacking"/> does. So
    /// the camera's move onto each turn's shot, and its ride with a walker, are drawn before the turn is over.
    /// </summary>
    private static void Dwelling(Run run)
    {
        var battle = run.Battle;
        run.Input = null;
        if (battle.Fade.Fading)
            return;
        if (run.Acting.Turn != battle.Current)
        {
            run.Acting = (battle.Current, false);
            run.TurnTicks = 0;
        }
        if (battle.State == CombatState.Turn && run.TurnTicks < 150)
        {
            if (++run.TurnTicks is > 75 and <= 125)
                run.Input = () => new PlayerInput(new System.Numerics.Vector2(0, 1));
            return;
        }
        Attacking(run);
    }

    /// <summary>The user's fight (Human Bandits and their Boss in C03, four on the field and four reserves) under Start Early.</summary>
    private Run Bandits(Action<Run> player, Action<Run>? begun = null)
    {
        PartyEnemy Bandit() => new(RecordId.Hex(RecordTypes.Entity, 0x021B), 1, 1);
        var boss = new PartyEnemy(RecordId.Hex(RecordTypes.Entity, 0x021C), 1, 1);
        return Play(host => Plain(Set, 1, host, [Bandit(), Bandit(), Bandit(), boss, Bandit(), Bandit(), Bandit(), Bandit()]), CombatTurns.StartEarly,
            player, begun: begun);
    }

    [Fact]
    public void TheDirectorsCameraNeverGoesPastItsShotAndComesBack()
    {
        // The user's second play (Nov 11): the camera kept overshooting. Three causes, each checked here through their fight with a
        // player who looks, walks and attacks: a shot kept where the camera was while it still moved (the outcome hold and its brace),
        // a player's turn's cut dropped where it opened in a hold and glided to instead, half a turn round, and a glide ridden faster
        // than the camera could stop from. And two more of that play: the result of an action the camera stages (an AI's walk, a
        // loosing) planned toward from its first shot, and the camera's own cut made to a shot the director was still moving to.
        // HBHT_CAMERA_TRACE=<file> writes every drawn tick's goal and shot, tab-separated, to look at a move with.
        var path = Environment.GetEnvironmentVariable("HBHT_CAMERA_TRACE");
        var lines = new StringBuilder();
        var back = new SwingBack(30, 8);
        var past = new PastGoal(3);
        List<string> uncut = [], staged = [], notches = [];
        var (standing, stagedFor, beatCuts) = (0, 0, 0);
        Shot? shown = null;
        var ticks = 0;
        var early = Bandits(Dwelling, run =>
        {
            var director = run.Drawing.Director;
            var t = director.Tuning;
            run.Watch!.Trace = (f, composer, d, cut) =>
            {
                var cam = director.Camera;
                var m = cam.Motion!;
                var (g, s) = (cam.Goal, m.Current);
                var what = $"{cam.Kind}; {director.Framed}; leading {director.Lead}, live {composer.Live}";
                ticks++;
                back.Step(d, s, cut || cam.Quick, what);
                past.Step(d, g, s, cut || cam.Quick, what);

                // A player's turn opens on its shot: once the character has stood two ticks in it with no hold on, the view is no more
                // than the cut's angle round from the shot found. Further, it should have been cut to.
                standing = cam.Walking && cam.Opening && !cam.Holding && !cam.Panning && !cam.Released ? standing + 1 : 0;
                var round = MathF.Abs(Shot.Wrap(g.Yaw - s.Yaw)) * 180 / MathF.PI;
                if (standing == 2 && MathF.Max(g.Pitch, s.Pitch) < 75 * MathF.PI / 180 && round > t.EnterCutTurn + 5)
                    uncut.Add(string.Create(CultureInfo.InvariantCulture, $"at drawn tick {d} the turn's shot is {round:F0}° round from the view, not cut to ({what})"));

                // While the camera stages the action led with, that action's result isn't held ahead of its strike.
                var early = cam.Staging && director.Beats.Any(b => b.In > t.BeatCutAhead && b.Beat.Beat.Action == director.Lead);
                stagedFor = early ? stagedFor + 1 : 0;
                if (stagedFor == 2)
                    staged.Add($"at drawn tick {d} the result of the action staged is planned toward ({what})");

                // A cut that isn't the director's own, while it keeps a shot of its own, that moves the view hardly at all: the notch.
                if (cut && cam.Steady && director.BeatCuts == beatCuts && shown is { } was
                    && System.Numerics.Vector3.Distance(was.Aim, s.Aim) < 2 && MathF.Abs(Shot.Wrap(was.Yaw - s.Yaw)) < 0.17f
                    && MathF.Abs(was.Pitch - s.Pitch) < 0.17f && was != s)
                    notches.Add($"at drawn tick {d} a cut jumped what was left of the way to a shot kept ({what})");
                beatCuts = director.BeatCuts;
                shown = s;

                if (path is null)
                    return;
                var (ys, ps, zs) = m.Speeds;
                lines.Append(CultureInfo.InvariantCulture,
                    $"{d}\t{director.Lead?.Index}\t{(composer.Live is null ? 0 : 1)}\t{f.State}\t{cam.Kind}\t{director.Framed}\t{cam.Intent.Moved?.Why}\t{(cam.Holding ? 1 : 0)}\t{(cam.Walking ? 1 : 0)}\t{(cut ? 1 : 0)}\t{(cam.Quick ? 1 : 0)}\t")
                    .Append(CultureInfo.InvariantCulture, $"{Deg(g.Yaw):F2}\t{Deg(g.Pitch):F2}\t{g.Distance:F3}\t{g.Aim.X:F3}\t{g.Aim.Y:F3}\t{g.Aim.Z:F3}\t")
                    .Append(CultureInfo.InvariantCulture, $"{Deg(s.Yaw):F2}\t{Deg(s.Pitch):F2}\t{s.Distance:F3}\t{s.Aim.X:F3}\t{s.Aim.Y:F3}\t{s.Aim.Z:F3}\t")
                    .Append(CultureInfo.InvariantCulture, $"{Deg(ys):F2}\t{Deg(ps):F2}\t{zs:F4}\t{m.AimSpeed.Length():F3}\t{director.Beats.Count(b => b.In <= 0)}\t{director.Beats.Count(b => b.In > 0)}\t{director.Walkers.Count}\n");
            };
        });
        if (path is not null)
        {
            File.WriteAllText(path, "d\tlead\tlive\tstate\tkind\tframed\tmoved\tholding\twalking\tcut\tquick\tgyaw\tgpitch\tgdist\tgx\tgy\tgz\tyaw\tpitch\tdist\tx\ty\tz\tvyaw\tvpitch\tvzoom\tvaim\tplaying\tcoming\twalkers\n" + lines);
        }
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"{early.Drawn} drawn ticks, {ticks} filmed, {early.Drawing.Director.Camera.Cuts} cuts; swung back {back.Found.Count} times, " +
            $"went past its goal {past.Found.Count}; turns opened uncut {uncut.Count}; staged results planned toward {staged.Count}; notches {notches.Count}");
        Assert.True(ticks > 5000, $"{ticks} drawn ticks filmed");
        Assert.True(back.Found.Count == 0, string.Join("\n", back.Found.Take(10)));
        Assert.True(past.Found.Count == 0, string.Join("\n", past.Found.Take(10)));
        Assert.True(uncut.Count == 0, string.Join("\n", uncut.Take(10)));
        Assert.True(staged.Count == 0, string.Join("\n", staged.Take(10)));
        Assert.True(notches.Count == 0, string.Join("\n", notches.Take(10)));

        static float Deg(float r) => r * 180 / MathF.PI;
    }

    /// <summary>
    /// The view turning one way and then back, the user's overshoot, whatever its goal did: between cuts, a turn of at least
    /// <paramref name="least"/> degrees one way followed within a second by <paramref name="back"/> the other.
    /// </summary>
    private sealed class SwingBack(float least, float back)
    {
        private float _run, _returned;
        private int _sign, _since;
        private bool _armed;
        private Shot? _last;

        public List<string> Found { get; } = [];

        public void Step(int d, Shot shot, bool cut, string what)
        {
            if (cut || _last is not { } last)
            {
                (_run, _returned, _sign, _since, _armed, _last) = (0, 0, 0, 0, false, shot);
                return;
            }
            var by = Shot.Wrap(shot.Yaw - last.Yaw) * 180 / MathF.PI;
            _last = shot;
            var sign = MathF.Abs(by) < 1e-5f ? 0 : MathF.Sign(by);
            if (_armed)
            {
                // Coming back after a turn: how far, within the second. Going on the same way again, it was a pause.
                _since++;
                if (sign == _sign)
                {
                    (_run, _returned, _armed) = (_run + MathF.Abs(by), 0, false);
                    return;
                }
                _returned += MathF.Abs(by);
                if (_returned >= back)
                {
                    Found.Add(string.Create(CultureInfo.InvariantCulture, $"at drawn tick {d}: turned {_run:F0}° and then {_returned:F0}° back within {_since} ticks ({what})"));
                    (_run, _returned, _sign, _armed) = (0, 0, 0, false);
                }
                else if (_since > 60)
                {
                    (_run, _returned, _sign, _armed) = (0, 0, 0, false);
                }
                return;
            }
            if (sign == 0)
                return;
            if (sign == _sign || _sign == 0)
                (_sign, _run) = (sign, _run + MathF.Abs(by));
            else if (_run >= least)
                (_armed, _returned, _since) = (true, MathF.Abs(by), 0);
            else
                (_sign, _run) = (sign, MathF.Abs(by));
        }
    }

    /// <summary>
    /// The view turning past its goal: between cuts, the yaw crossing a goal that stood still as it did, and then being more than
    /// <paramref name="most"/> degrees past it before coming back, the goal not having jumped meanwhile.
    /// </summary>
    private sealed class PastGoal(float most)
    {
        private (Shot Goal, Shot Shot)? _last;
        private (int At, int Side, float Past, string What)? _crossed;

        public List<string> Found { get; } = [];

        public void Step(int d, Shot goal, Shot shot, bool cut, string what)
        {
            if (cut || _last is not { } last)
            {
                (_last, _crossed) = ((goal, shot), null);
                return;
            }
            _last = (goal, shot);
            var (was, now) = (Off(last.Goal, last.Shot), Off(goal, shot));
            var moved = MathF.Abs(Off(goal, last.Goal));
            if (_crossed is { } c)
            {
                // Back across, the goal jumped, or a second and a half gone: over.
                if (MathF.Sign(now) != c.Side || moved > 2 || d - c.At > 90)
                {
                    if (c.Past > most)
                        Found.Add(string.Create(CultureInfo.InvariantCulture, $"at drawn tick {c.At}: went {c.Past:F1}° past its goal ({c.What})"));
                    _crossed = null;
                }
                else
                {
                    _crossed = c with { Past = MathF.Max(c.Past, MathF.Abs(now)) };
                }
            }
            if (_crossed is null && was * now < 0 && moved < 0.5f)
                _crossed = (d, MathF.Sign(now), MathF.Abs(now), what);

            static float Off(Shot a, Shot b) => Shot.Wrap(a.Yaw - b.Yaw) * 180 / MathF.PI;
        }
    }
}
