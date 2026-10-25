using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// Combat's buttons as <see cref="Battle.Press"/> dispatches them (<c>Combat.Controls</c>), headless, in the rats' battle the port
/// sets up from checkpoint 01: A through the attack, its confirmation and the AI's walk, Start's pause, the held menus and B.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class BattlePressTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private sealed record Run(Built Built, ZoneEngine Zone)
    {
        public Battle Battle => Built.Battle;

        public void Tick() => Battle.Tick(Zone, false, _ => null);

        /// <summary>Plays on, skipping the members' turns that <paramref name="take"/> doesn't want, until one it does, faded in.</summary>
        public Combatant Turn(Func<Combatant, bool> take)
        {
            for (var i = 0; i < 200_000 && !Battle.Over; i++)
            {
                if (Battle.Current is { Controlled: false } c && Battle.State == CombatState.Turn && !Battle.Fade.Fading && Battle.Lines.Hold is null)
                {
                    if (take(c))
                        return c;
                    if (Battle.State == CombatState.Turn)
                        Assert.True(Battle.Press(MenuAction.TabPrevious));
                }
                Tick();
            }
            throw new InvalidOperationException("No turn came that was wanted.");
        }
    }

    private Run Start()
    {
        var built = Plain(Set, 1);
        return new Run(built, Zone(built));
    }

    [Fact]
    public void AAttacksThenConfirmsAndNothingReachesTheBattleWhileItFadesIn()
    {
        var run = Start();
        var battle = run.Battle;
        Assert.True(battle.Fade.Fading);
        Assert.False(battle.Press(MenuAction.Accept));
        Assert.False(battle.Press(MenuAction.Start));

        // A in a turn with nothing in reach does nothing, and the turn is skipped, until Brenna's. A flask in her hand aims freely
        // (state 18): C-left and C-right step its cursor, left and right don't, B puts it away, and A again confirms.
        var brenna = run.Turn(c =>
        {
            if (c.Sheet.Name == "Brenna")
                return true;
            Assert.False(battle.Press(MenuAction.Accept));
            Assert.Equal(CombatState.Turn, battle.State);
            return false;
        });
        run.Built.Game.Party.Inventory.Add(PotionId(BasePotion.FireFlask), 2);
        brenna.HoldPotion(battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], battle);
        Assert.True(battle.Press(MenuAction.Accept));
        Assert.Equal(CombatState.TargetThrown, battle.State);
        var cursor = battle.TargetsA.Cursor;
        Assert.False(battle.Press(MenuAction.Left));
        Assert.Equal(cursor, battle.TargetsA.Cursor);
        if (battle.TargetsA.Candidates > 1)
        {
            Assert.True(battle.Press(MenuAction.MemberLeft));
            Assert.NotEqual(cursor, battle.TargetsA.Cursor);
            Assert.True(battle.Press(MenuAction.MemberRight));
            Assert.Equal(cursor, battle.TargetsA.Cursor);
        }
        Assert.True(battle.Press(MenuAction.Back));
        Assert.Equal(CombatState.Turn, battle.State);
        Assert.True(battle.Press(MenuAction.Accept));
        Assert.Equal(CombatState.TargetThrown, battle.State);
        Assert.True(battle.Press(MenuAction.Accept));
        Assert.NotEqual(CombatState.TargetThrown, battle.State);
        Assert.NotEqual(CombatState.Turn, battle.State);
        Assert.Same(brenna, battle.Current);
    }

    [Fact]
    public void ASkipsAnAisWalkAndStartPausesATurn()
    {
        var run = Start();
        var battle = run.Battle;
        var skipped = false;
        for (var i = 0; i < 100_000 && !skipped && !battle.Over; i++)
        {
            if (battle.Current is { Controlled: false } && battle.State == CombatState.Turn && !battle.Fade.Fading && battle.Lines.Hold is null)
                battle.Press(MenuAction.TabPrevious);
            run.Tick();
            if (battle.State != CombatState.Moving || battle.Current is not { } c || !c.Body.Moving)
                continue;
            // Combat.InputMoving: A ends the walk at once, at the leg's end.
            Assert.True(battle.Press(MenuAction.Accept));
            Assert.False(c.Body.Moving);
            skipped = true;
        }
        Assert.True(skipped);

        // Start in a turn the player plays: the pause menu, from the turn, with the Party tab and Flee while the party can.
        run.Turn(_ => true);
        Assert.True(battle.Press(MenuAction.Start));
        Assert.Equal(CombatState.Pause, battle.State);
        Assert.True(battle.PausedFromTurn);
        // Nothing else reaches it while it's paused.
        Assert.False(battle.Press(MenuAction.Start));
        Assert.False(battle.Press(MenuAction.Accept));
        battle.Unpause(flee: false);
        Assert.Equal(CombatState.Turn, battle.State);
    }

    [Fact]
    public void AHeldMenuTakesOnlyTheStickAndItsOwnButtonLetGo()
    {
        var run = Start();
        var battle = run.Battle;
        // A wizard with spells to list: C-down's menu.
        var caster = run.Turn(c => c.Sheet.Spells.Count > 0);
        Assert.True(battle.Press(MenuAction.Details));
        var menu = battle.Menu!;
        Assert.False(menu.Items);
        Assert.Equal(CombatState.MenuSpells, battle.State);
        var cursor = menu.Cursor;
        Assert.False(battle.Press(MenuAction.Start));
        Assert.False(battle.Press(MenuAction.Accept));
        Assert.False(battle.Press(MenuAction.AppraiseRelease));
        Assert.Same(menu, battle.Menu);
        Assert.True(battle.Press(MenuAction.Down));
        Assert.Equal(menu.Rows.Count > 1 ? (cursor + 1) % menu.Rows.Count : cursor, menu.Cursor);
        Assert.True(battle.Press(MenuAction.Up));
        Assert.Equal(cursor, menu.Cursor);
        // Letting C-down go chooses the row and closes the menu.
        Assert.True(battle.Press(MenuAction.DetailsRelease));
        Assert.Null(battle.Menu);
        Assert.NotEqual(CombatState.MenuSpells, battle.State);
        Assert.Same(caster, battle.Current);
    }
}
