using System.Globalization;
using System.Numerics;
using HBHT.Core.Trek;
using HBHT.Core.Ui;

namespace HBHT.Core.Combat;

/// <summary>
/// The party's turns played by a script, for movies and tests that compare one battle under different settings with the same inputs
/// (the runtime's <c>--battle-plan</c>). Each member's turn runs its steps in order, the stick and presses going to the battle as the
/// player's would, through the drawing; a member with none runs the plan's <c>*</c>.
/// </summary>
/// <remarks>
/// A plan is <c>Name=step/step/…;Name=…;*=…</c>, a name matched against the start of the member's. The steps:
/// <c>wait:n</c> waits n ticks; <c>stick:x:y:n</c> holds the stick at (x, y) for n ticks; <c>orbit:x:y:n</c> holds the camera's stick;
/// <c>accept</c> (A: attack, then confirm), <c>back</c> and <c>skip</c> press; <c>spell:Name</c> chooses the spell whose row starts with
/// that from the spell menu. Once a turn's steps are done it skips, so a battle never waits for good. A step the battle refuses is passed.
/// </remarks>
public sealed class BattlePlan
{
    private readonly Dictionary<string, string[]> _plans;
    private Combatant? _turn;
    private int _step;
    private int _ticks;

    private BattlePlan(Dictionary<string, string[]> plans) => _plans = plans;

    /// <summary>A plan from its text, as above.</summary>
    public static BattlePlan Parse(string text)
    {
        var plans = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"'{part}' isn't Name=steps.");
            plans[part[..eq].Trim()] = part[(eq + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return new BattlePlan(plans);
    }

    /// <summary>
    /// A tick, before the drawing's: presses what the step says, and gives the stick it holds, or null when it holds none (the player's
    /// own then goes on).
    /// </summary>
    public PlayerInput? Tick(BattleDrawing drawing)
    {
        var battle = drawing.Battle;
        if (!drawing.TakesPresses || !battle.WaitsOnPlayer || battle.Over || battle.Fade.Fading || battle.Current is not { Controlled: false } current)
            return null;
        if (_turn != current)
        {
            _turn = current;
            _step = 0;
            _ticks = 0;
        }
        var steps = _plans.FirstOrDefault(p => p.Key != "*" && current.Sheet.Name.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase)).Value
            ?? _plans.GetValueOrDefault("*") ?? [];
        if (_step >= steps.Length)
        {
            // Done: skip the turn, or put away an aim the battle refused first.
            drawing.Press(battle.State == CombatState.Turn ? MenuAction.TabPrevious : MenuAction.Back);
            return null;
        }
        var step = steps[_step].Split(':');
        float F(int i) => float.Parse(step[i], CultureInfo.InvariantCulture);
        int N(int i) => int.Parse(step[i], CultureInfo.InvariantCulture);
        switch (step[0].ToLowerInvariant())
        {
            case "wait":
                return Hold(N(1), null);
            case "stick":
                return Hold(N(3), new PlayerInput(new Vector2(F(1), F(2))));
            case "orbit":
                return Hold(N(3), new PlayerInput(Vector2.Zero, Orbit: true, OrbitStick: new Vector2(F(1), F(2)), Free: true));
            case "accept":
                drawing.Press(MenuAction.Accept);
                break;
            case "back":
                drawing.Press(MenuAction.Back);
                break;
            case "skip":
                drawing.Press(MenuAction.TabPrevious);
                break;
            case "spell":
                if (battle.OpenMenu(items: false) && battle.Menu is { } menu)
                {
                    var name = string.Join(':', step.Skip(1));
                    for (var i = 0; i < menu.Rows.Count && !menu.Current!.Text.StartsWith(name, StringComparison.OrdinalIgnoreCase); i++)
                        battle.MoveMenu(1);
                    battle.CloseMenu(items: false);
                }
                break;
            default:
                throw new FormatException($"A battle plan has no step '{steps[_step]}'.");
        }
        _step++;
        return null;
    }

    /// <summary>Holds a step for <paramref name="ticks"/> ticks, then goes on to the next.</summary>
    private PlayerInput? Hold(int ticks, PlayerInput? input)
    {
        if (++_ticks >= ticks)
        {
            _ticks = 0;
            _step++;
        }
        return input ?? default(PlayerInput);
    }
}
