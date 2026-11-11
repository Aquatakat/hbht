using HBHT.Core.Combat;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

public sealed partial class StartEarlyTests
{
    [Theory]
    [InlineData(CombatTurns.Original)]
    [InlineData(CombatTurns.StartEarly)]
    public void TheLogKeepsItsPortraitAndTheTurnsLineThroughSholehsPotions(CombatTurns turns)
    {
        // The user's Nov 11 play: Sholeh's portrait went from the log while they took potions from C-up, put them away and drank them.
        // Under Start Early an enemy's line, from an action still drawing as her turn opened early, came a tick after hers and stood
        // in the box, by its own portrait, all through her turn; and the battle's first line had no portrait till the next. Sholeh, with
        // a bow and potions, takes one in hand and puts it away, drinks one, and takes and puts back and drinks; every drawn tick has a
        // portrait, and once only her turn draws, the box shows the battle's own line.
        var stale = new List<string>();
        var output = TestContext.Current.TestOutputHelper;
        var potions = new[] { BasePotion.Strength, BasePotion.Dexterity, BasePotion.Healing, BasePotion.Stamina, BasePotion.Clarity, BasePotion.Charisma, BasePotion.Defence };
        var sholehTurns = 0;
        Combatant? lastTurn = null;
        var step = 0;
        string[] script = [];
        var problems = new List<string>();
        var early = Play(host =>
        {
            var built = Plain(Set, 1, host);
            var sholeh = built.Battle.Combatants.First(c => c.Sheet.Name == "Sholeh");
            var catalog = built.Game.Party.Inventory.Catalog;
            sholeh.Sheet.Equipment.RemoveWeapon();
            // The first bow she can wield goes on.
            _ = Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Weapon?.Missile?.Scene is not null)
                .Select(i => new HBHT.Core.Items.ItemInstance(catalog[i.Id]))
                .First(i => sholeh.Sheet.Equipment.EquipWeapon(i) == HBHT.Core.Items.EquipRefusal.None);
            sholeh.SetDefaultAction(built.Battle.Rules);
            foreach (var p in potions)
                built.Game.Party.Inventory.Add(PotionId(p), 5);
            return built;
        }, turns, run =>
        {
            var b = run.Battle;
            if (b.Fade.Fading || b.Current is not { } c)
                return;
            if (c != lastTurn)
            {
                lastTurn = c;
                step = 0;
                if (c.Sheet.Name == "Sholeh")
                {
                    script = (sholehTurns++ % 3) switch
                    {
                        0 => ["open", "release", "B", "A", "A"],
                        1 => ["open", "release", "A"],
                        _ => ["open", "release", "B", "open", "release", "A"],
                    };
                }
                else
                {
                    script = ["Z"];
                }
            }
            if (step >= script.Length)
            {
                // Whatever's left: confirm an aim, else skip.
                if (!run.Drawing.Press(MenuAction.Accept))
                    run.Drawing.Press(MenuAction.TabPrevious);
                return;
            }
            var press = script[step++] switch
            {
                "open" => MenuAction.Appraise,
                "release" => MenuAction.AppraiseRelease,
                "A" => MenuAction.Accept,
                "B" => MenuAction.Back,
                _ => MenuAction.TabPrevious,
            };
            var did = run.Drawing.Press(press);
            output?.WriteLine($"  press {press} by {c.Sheet.Name} in {b.State}: {did}");
            if (!did && press == MenuAction.AppraiseRelease)
                step = script.Length;
        }, begun: run => run.Each = r =>
        {
            var f = r.Drawing.Frame;
            var g = f.Gui;
            if (g.LogPortrait is null && g.Log.Length > 0)
                problems.Add($"drawn {r.Drawn} (tick {f.Tick}): '{g.Log}' with no portrait");
            if (r.Drawing.TakesPresses && r.Battle.WaitsOnPlayer && r.Battle.Current is { } cur
                && r.Drawing.Composer.DrawingNow().All(a => a == r.Drawing.Composer.Live) && g.Log != r.Battle.Lines.Text)
                stale.Add($"drawn {r.Drawn} (tick {f.Tick}): {cur.Sheet.Name}'s turn ({r.Battle.State}) beside '{g.Log}', the battle's '{r.Battle.Lines.Text}'");
        });
        output?.WriteLine($"Sholeh's turns: {sholehTurns}; outcome {early.Battle.Outcome}");
        Assert.True(sholehTurns >= 3, $"{sholehTurns} turns of Sholeh's");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
        Assert.True(stale.Count == 0, string.Join("\n", stale.Take(30)));
    }
}
