using System.Numerics;
using Aidyn.Core.Combat;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// The battle screen (<c>combatgui.cpp</c> and the radar), checked against 1.0's code and the user's capture
/// (<c>roms/combat.png</c>: Brenna's turn against four rats with six in reserve). Checkpoint 02's RAM checks the frames,
/// the portraits and the radar in Rom.Tests' <c>BattleCheckpointTests</c>.
/// </summary>
public sealed class CombatGuiTests
{
    // Fighters' entities are 0x2000 + their slot: two members, four enemies on the field and two reserves.
    private static readonly RecordId[] Party = [Entity(0x2000), Entity(0x2001)];
    private static readonly RecordId[] Enemies = [.. Enumerable.Range(2, 6).Select(i => Entity(0x2000 + i))];

    private static Battle Fight() => BattleFixtures.Create(2, 4, [.. Enumerable.Range(0, 6).Select(s => BattleFixtures.Fighter(s, 15))]);

    /// <summary>The screen for the fight, every combatant's portrait in its frame and the reserves in theirs, slid in.</summary>
    private static CombatGui Screen(Battle battle)
    {
        var gui = new CombatGui(Party, Enemies);
        for (var slot = 0; slot < 6; slot++)
            gui.Assign(Entity(0x2000 + slot), slot, enemy: slot >= 2);
        Settle(gui, battle);
        return gui;
    }

    private static void Settle(CombatGui gui, Battle battle)
    {
        for (var i = 0; i < CombatGui.TweenTicks; i++)
            gui.Tick(battle);
    }

    private static RecordId? Portrait(RecordId entity) => RecordId.Hex(RecordTypes.Image, 0x0163);

    [Fact]
    public void TheFramesRunDownTheColumnsAndTheReservesAreSmallInTwoColumns()
    {
        // The capture: four rats large at x 21 (their bars at 47..49 from y 20, 48, 76, 104), the party at 270, and six
        // reserves small, four at x 21 and two at 35, from y 130.
        var gui = new CombatGui([.. Enumerable.Range(0, 4).Select(i => Entity(0x2000 + i))], [.. Enumerable.Repeat(Entity(0x026E), 10)]);

        var bars = gui.Bars;
        Assert.Equal(16, bars.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal((true, true, 270, 18 + 28 * i, 274, 20 + 28 * i, 23, 23), (bars[i].Party, bars[i].Large, bars[i].FrameX, bars[i].FrameY, bars[i].X, bars[i].Y, bars[i].Width, bars[i].Height));
            Assert.False(bars[i].Hidden);
        }
        // Two spares under the party, hidden and kept for a placeholder.
        Assert.All(bars.Skip(4).Take(2), b => Assert.True(b is { Party: true, Hidden: true, Blocked: true }));
        Assert.Equal([130, 158], bars.Skip(4).Take(2).Select(b => b.FrameY));
        for (var j = 0; j < 4; j++)
        {
            var large = bars[6 + j];
            Assert.Equal((false, true, 21, 18 + 28 * j, 23, 20 + 28 * j), (large.Party, large.Large, large.FrameX, large.FrameY, large.X, large.Y));
            var small = bars[10 + j];
            Assert.Equal((false, 21, 130 + 12 * j, 22, 131 + 12 * j, 11, 10), (small.Large, small.FrameX, small.FrameY, small.X, small.Y, small.Width, small.Height));
            Assert.Equal(CombatGui.SmallFrame, small.Frame);
        }
        Assert.Equal([(35, 130), (35, 142)], bars.Skip(14).Select(b => (b.FrameX, b.FrameY)));
        Assert.Equal((36, 131), (bars[14].X, bars[14].Y));
    }

    [Fact]
    public void APortraitSlidesFromTheMiddleIntoItsFrameOverASecond()
    {
        var battle = Fight();
        var gui = new CombatGui(Party, Enemies);
        gui.Assign(Party[0], 0, enemy: false);
        var alaron = gui.Portraits[0];
        Assert.Equal((160, 120, 40, 40), (alaron.X, alaron.Y, alaron.Width, alaron.Height));

        for (var i = 0; i < 30; i++)
            gui.Tick(battle);
        // x in a straight line, y easing out (sin(πt/2)), the size easing in and out; each truncated.
        Assert.Equal((217, 49, 31, 31), (alaron.X, alaron.Y, alaron.Width, alaron.Height));

        for (var i = 0; i < 30; i++)
            gui.Tick(battle);
        Assert.Equal((274, 20, 23, 23), (alaron.X, alaron.Y, alaron.Width, alaron.Height));
        Assert.False(alaron.Sliding);
    }

    [Fact]
    public void CombatantsTakeTheirSidesFramesAndTheReservesTheSmallOnesOnTheFirstTick()
    {
        var battle = Fight();
        var gui = Screen(battle);

        Assert.Equal([PortraitState.Party, PortraitState.Party, PortraitState.Placeholder, PortraitState.Placeholder,
            PortraitState.Enemy, PortraitState.Enemy, PortraitState.Enemy, PortraitState.Enemy, PortraitState.Waiting, PortraitState.Waiting],
            gui.Portraits.Select(p => p.State));
        Assert.Equal([0, 1, 4, 5, 6, 7, 8, 9], gui.Portraits.Where(p => p.Bar is not null).Select(p => gui.Bars.ToList().IndexOf(p.Bar!)));
        Assert.Equal([(22, 131), (22, 143)], gui.Portraits.Skip(8).Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void TheDeadKeepTheirFramesUnderASkullUntilAReserveTakesTheirPlace()
    {
        var battle = Fight();
        var gui = Screen(battle);
        var rat = gui.Portraits[5];

        gui.MarkGone(3);
        Assert.Equal(PortraitState.Fallen, rat.State);
        Assert.True(rat.Skull);
        Assert.Equal(0, rat.SkullAlpha);
        Settle(gui, battle);
        Assert.Equal(255, rat.SkullAlpha);
        Assert.Equal(CombatGui.FrameColor, rat.Bar!.Tint);

        // The first reserve joins in the dead one's slot: they swap frames, and the dead one is out of the fight.
        var large = rat.Bar;
        var reserve = gui.Portraits[8];
        var small = reserve.Bar;
        gui.Assign(Enemies[4], 3, enemy: true);
        Assert.Same(large, reserve.Bar);
        Assert.Same(small, rat.Bar);
        Assert.Equal(PortraitState.Gone, rat.State);
        Assert.Equal(PortraitState.Enemy, reserve.State);
        Settle(gui, battle);
        Assert.Equal((23, 48, 23, 23), (reserve.X, reserve.Y, reserve.Width, reserve.Height));
    }

    [Fact]
    public void TheFledHaveOrangeFrames()
    {
        var battle = Fight();
        var gui = Screen(battle);

        gui.MarkGone(4, fled: true);
        gui.MarkReserveFled(Enemies[5]);
        gui.Tick(battle);

        Assert.True(gui.Portraits[6] is { State: PortraitState.Fallen, Fled: true, Skull: true });
        Assert.Equal(CombatGui.FledColor, gui.Portraits[6].Bar!.Tint);
        Assert.True(gui.Portraits[9] is { State: PortraitState.Gone, Fled: true, Skull: true });
        Assert.Equal(CombatGui.FledColor, gui.Portraits[9].Bar!.Tint);
    }

    [Fact]
    public void TheTargetSetsTintTheFramesTheBrightOnesOnlyWhileATargetIsChosen()
    {
        var battle = Fight();
        var gui = Screen(battle);
        Rgba FrameOf(int slot) => gui.Portraits.Single(p => p.Slot == slot).Bar!.Tint;

        // An attack's candidates are dim green, a spell's dim red, whichever side they're on; the spell's over the attack's.
        battle.Marks[2] = TargetMarks.AttackCandidate | TargetMarks.AttackSelected;
        battle.Marks[3] = TargetMarks.AttackCandidate;
        battle.Marks[0] = TargetMarks.SpellCandidate | TargetMarks.SpellSelected;
        battle.Marks[4] = TargetMarks.AttackCandidate | TargetMarks.SpellCandidate;
        gui.Tick(battle);
        Assert.Equal(CombatGui.AttackCandidateColor, FrameOf(2));
        Assert.Equal(CombatGui.AttackCandidateColor, FrameOf(3));
        Assert.Equal(CombatGui.SpellCandidateColor, FrameOf(0));
        Assert.Equal(CombatGui.SpellCandidateColor, FrameOf(4));
        Assert.Equal(CombatGui.FrameColor, FrameOf(1));

        // Aiming an attack (state 9) or a flask (18) brightens set A's selected; a spell (10) or its area (11) set B's.
        gui.TintState = CombatState.TargetAttack;
        gui.Tick(battle);
        Assert.Equal(CombatGui.AttackSelectedColor, FrameOf(2));
        Assert.Equal(CombatGui.SpellCandidateColor, FrameOf(0));
        gui.TintState = CombatState.PlaceArea;
        gui.Tick(battle);
        Assert.Equal(CombatGui.AttackCandidateColor, FrameOf(2));
        Assert.Equal(CombatGui.SpellSelectedColor, FrameOf(0));

        // The dead take no tint.
        gui.MarkGone(3);
        gui.Tick(battle);
        Assert.Equal(CombatGui.FrameColor, FrameOf(3));
    }

    [Fact]
    public void TheHpBarsSitBesideThePortraitsTheirColourFromGreenToRed()
    {
        var battle = Fight();
        var gui = Screen(battle);
        var rhedaMax = battle.Slots[1]!.Sheet.MaxHp;
        battle.Slots[1]!.Sheet.TakeDamage(rhedaMax / 2);
        battle.Slots[2]!.Sheet.TakeDamage(1000);

        var bars = gui.Draw(battle, Portrait).HpBars;

        // Black behind, then the fill from the bottom: the party's 4 left of the portrait, an enemy's 24 right.
        Assert.Equal(new HudFill(270, 20, 3, 23, new Rgba(0, 0, 0, 255)), bars[0]);
        Assert.Equal(new HudFill(270, 20, 3, 23, new Rgba(0, 255, 0, 255)), bars[1]);
        var rheda = battle.Slots[1]!.Sheet;
        var fill = (int)(23 * ((float)rheda.Hp / rheda.MaxHp));
        Assert.Equal(new HudFill(270, 48 + 23 - fill, 3, fill, CombatGui.HpColor(rheda.Hp, rheda.MaxHp)), bars[3]);
        Assert.Equal(new HudFill(47, 20, 3, 23, new Rgba(0, 0, 0, 255)), bars[4]);
        // None left still shows a pixel.
        Assert.Equal(new HudFill(47, 42, 3, 1, new Rgba(255, 0, 0, 255)), bars[5]);
        // Reserves waiting have no bar.
        Assert.Equal(12, bars.Count);

        Assert.Equal(new Rgba(128, 127, 0, 255), CombatGui.HpColor(1, 2));
        Assert.Equal(new Rgba(0, 255, 0, 255), CombatGui.HpColor(30, 30));
    }

    [Fact]
    public void TheLogBoxShowsALineAtOnceAndAQueuedOneAtTheFlush()
    {
        var log = new CombatLog();
        Assert.Equal(" ", log.Text);
        log.Show("Brenna begins action");
        log.Show("Brenna attacks Giant Rat");
        log.Queue("Giant Rat loses 4 hit points");
        log.Queue("Giant Rat killed");
        Assert.Equal("Brenna attacks Giant Rat", log.Text);
        log.Flush();
        Assert.Equal("Giant Rat killed", log.Text);

        // Banish's third line comes up in place of the queued one.
        log.Queue("Rheda casts Banishing");
        log.QueueThird("Skeleton banished");
        log.Flush();
        Assert.Equal("Skeleton banished", log.Text);
        Assert.Equal("Skeleton banished", log.Queued);
        Assert.False(log.ThirdWaiting);

        // A reserve fleeing as it comes holds the box on its line for a second, then the shown one comes back.
        log.HoldOn("Enemy reinforcements flee");
        Assert.Equal("Enemy reinforcements flee", log.Text);
        for (var i = 1; i < CombatLog.HoldTicks; i++)
            Assert.True(log.StepHold());
        Assert.False(log.StepHold());
        Assert.Equal("Skeleton banished", log.Text);

        // Locked as the party falls, nothing changes it.
        log.Lock("The party is incapacitated...", null);
        log.Show("Giant Rat begins action");
        Assert.Equal("The party is incapacitated...", log.Text);
    }

    [Fact]
    public void TheLogBoxsLineIsTheCapturesAndTheBoxFloatsAsTheConversationBoxDoes()
    {
        var box = CombatLogBox.Layout("Brenna begins action", null, 320, 240);
        // The capture's text runs from x 76, its glyphs' rows from 190.
        Assert.Equal((76, 190, new ScreenRect(76, 190, 241, 230)), (box.Text.X, box.Text.Y, box.Text.Bounds));
        // Floating as the conversation box does, its border and portrait level with the box's top as that box has them.
        Assert.Equal((20, 180, 26, 186), (box.BorderX, box.BorderY, box.PortraitX, box.PortraitY));
        Assert.Equal(new ScreenRect(16, 180, 304, 228), box.Panel.Band);
        Assert.Equal((10, 12, 16, 16), (box.Panel.Top, box.Panel.Bottom, box.Panel.Left, box.Panel.Right));

        var battle = Fight();
        battle.Write("Brenna begins action");
        Assert.Equal("Brenna begins action", battle.Lines.Text);
    }

    [Fact]
    public void OnAWideScreenTheColumnsKeepToTheirCornersAndTheBoxFloatsAtTheBottomCentre()
    {
        var battle = Fight();
        var gui = Screen(battle);

        var inBox = gui.Draw(battle, Portrait, RadarAnchor.LogBox, 640, 360);
        Assert.Equal((21f, 18f), (inBox.Frames.First(f => f.X < 320).X, inBox.Frames.First(f => f.X < 320).Y));
        Assert.Equal((270 + 320f, 18f), (inBox.Frames[0].X, inBox.Frames[0].Y));
        Assert.Equal((76 + 160, 190 + 120), (inBox.Log.Text.X, inBox.Log.Text.Y));
        Assert.Equal(new ScreenRect(176, 300, 464, 348), inBox.Log.Panel.Band);
        Assert.Equal((245 + 160f, 170 + 120f), (inBox.RadarRing.X, inBox.RadarRing.Y));

        var corner = gui.Draw(battle, Portrait, RadarAnchor.Corner, 640, 360);
        Assert.Equal((245 + 320f, 170 + 120f), (corner.RadarRing.X, corner.RadarRing.Y));
    }

    [Fact]
    public void TheRadarTurnsSoTheCameraLooksUpItAndHoldsFarBodiesAtItsEdge()
    {
        // Looking along +X, a body 10 along +X is 5 up and one 10 along +Z is 5 right; one 100 out is held at 20.
        Assert.Equal((0, 5, false), CombatRadar.Place(new Vector3(10, 0, 0), Vector2.UnitX));
        Assert.Equal((5, 0, false), CombatRadar.Place(new Vector3(0, 3, 10), Vector2.UnitX));
        Assert.Equal((0, 20, true), CombatRadar.Place(new Vector3(100, 0, 0), Vector2.UnitX));
        // Looking north (−Z), north is up and east (+X) right.
        Assert.Equal((0, 5, false), CombatRadar.Place(new Vector3(0, 0, -10), -Vector2.UnitY));
        Assert.Equal((5, 0, false), CombatRadar.Place(new Vector3(10, 0, 0), -Vector2.UnitY));
        // Truncated toward zero.
        Assert.Equal((-1, 0, false), CombatRadar.Place(new Vector3(0, 0, -3.9f), Vector2.UnitX));
    }

    [Fact]
    public void TheRadarsPulseSwingsFromNothingToMinus150AndBackIn76Frames()
    {
        var radar = new CombatRadar();
        for (var i = 0; i < 37; i++)
            radar.StepFrame();
        Assert.Equal(-148, radar.Pulse);
        radar.StepFrame();
        Assert.Equal(-150, radar.Pulse);
        for (var i = 0; i < 37; i++)
            radar.StepFrame();
        Assert.Equal(-2, radar.Pulse);
        radar.StepFrame();
        Assert.Equal(0, radar.Pulse);
        radar.StepFrame();
        Assert.Equal(-4, radar.Pulse);
    }
}
