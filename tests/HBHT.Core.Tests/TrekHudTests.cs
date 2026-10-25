using System.Numerics;
using HBHT.Core.Calendar;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>Trek's HUD: the sundial, the compass, the portraits' slide and the minimap's rules, checked against 1.0's code and the user's captures.</summary>
public class TrekHudTests
{
    private static uint At(float hours) => 6 * GameClock.Week + 2 * GameClock.Day + (uint)(hours * GameClock.Hour);

    [Fact]
    public void TheSunGoesRoundTheSundialTwiceADay()
    {
        // The capture's glow is centred about (72.5, 208.8), peach: a quarter to four in the afternoon.
        var glow = Sundial.Draw(At(15.75f), 1, sky: true).Single(s => s.Image == Sundial.SunGlow);
        Assert.InRange(glow.X + 10, 71.5f, 73.5f);
        Assert.InRange(glow.Y + 10, 207.5f, 209.5f);
        Assert.Equal(new Rgba(255, 190, 150, 160), glow.Color);

        // Noon and midnight are both at the top, the sun's glow by day and its dot by night.
        Assert.Equal((39f, 164f), Sun(At(12)));
        Assert.Equal((39f, 164f), Sun(At(0)));
        Assert.Contains(Sundial.Draw(At(0), 1, true), s => s.Image == Sundial.SunDot);
        Assert.DoesNotContain(Sundial.Draw(At(0), 1, true), s => s.Image == Sundial.SunGlow);

        static (float, float) Sun(uint time) => Sundial.Draw(time, 1, true).Where(s => s.Image == Sundial.SunGlow || s.Image == Sundial.SunDot)
            .Select(s => (s.X, s.Y)).First();
    }

    [Fact]
    public void DawnBlendsTheNightIntoTheDay()
    {
        Assert.Equal((new Rgba(120, 100, 190, 0), (byte)0, (byte)160), Sundial.Light(5, 1));
        Assert.Equal((new Rgba(187, 145, 170, 0), (byte)80, (byte)80), Sundial.Light(6, 1));
        Assert.Equal((new Rgba(255, 190, 150, 0), (byte)160, (byte)0), Sundial.Light(7, 1));
        // The blend's colour isn't dimmed with the screen, but its alphas are.
        Assert.Equal((new Rgba(187, 145, 170, 0), (byte)40, (byte)40), Sundial.Light(6, 0.5f));
    }

    [Fact]
    public void UnderACaveSkyTheSundialHasNoSunOrMoon()
    {
        var sprites = Sundial.Draw(At(12), 1, sky: false).Select(s => s.Image).ToList();
        Assert.Equal([Sundial.Ring, Sundial.Rays], sprites);
    }

    [Fact]
    public void TheCompassTurnsTheWayTheCameraLooksToTheTop()
    {
        // The capture: W at the top, its top-left about (266, 166), N on the right and S on the left.
        var letters = Compass.Draw(Vector2.Normalize(new Vector2(-1, 0.11f)), 1).Skip(1).ToDictionary(s => s.Image);
        var west = letters[Compass.West];
        Assert.InRange(west.X, 265, 268);
        Assert.InRange(west.Y, 165, 167);
        Assert.InRange(west.Color.A, 250, 255);
        Assert.True(letters[Compass.North].X > 285 && letters[Compass.South].X < 250);
        Assert.InRange(letters[Compass.East].Color.A, 65, 70);

        // Facing north, -Z: N at the top and E on the right.
        var north = Compass.Draw(new Vector2(0, -1), 1).Skip(1).ToDictionary(s => s.Image);
        Assert.Equal((263f, 166f), (north[Compass.North].X, north[Compass.North].Y));
        Assert.Equal((288f, 191f), (north[Compass.East].X, north[Compass.East].Y));
    }

    [Fact]
    public void ThePortraitsSlideUpAndDownOver14Frames()
    {
        var portraits = new PartyPortraits();
        var frames = 0;
        while (portraits.Top > PartyPortraits.Up)
        {
            portraits.StepFrame();
            frames++;
        }
        Assert.Equal(14, frames);
        portraits.StepFrame();
        Assert.Equal(PartyPortraits.Up, portraits.Top);

        portraits.Shown = false;
        for (var i = 0; i < 14; i++)
            portraits.StepFrame();
        Assert.Equal(PartyPortraits.Down, portraits.Top);
        Assert.Empty(portraits.Draw([null], 1));
    }

    [Fact]
    public void ThePortraitsAreHalfSeenInTheirFrames()
    {
        var portraits = new PartyPortraits();
        for (var i = 0; i < 14; i++)
            portraits.StepFrame();
        var face = RecordId.Hex(RecordTypes.Image, 0x0125);
        var sprites = portraits.Draw([face, null, null, null], 1).ToList();
        // Four frames even for empty slots, then the one portrait, 30 square at (90, 190), as the capture has it.
        Assert.Equal(4, sprites.Count(s => s.Image == PartyPortraits.Frame));
        var portrait = sprites.Single(s => s.Image == face);
        Assert.Equal((90f, 190f, 0.75f), (portrait.X, portrait.Y, portrait.Scale));
        Assert.Equal(new Rgba(255, 255, 255, 128), portrait.Color);
    }

    /// <summary>Two groups: A01 has two pieces by height, B01 one, and C01 is the other group's.</summary>
    private static readonly MinimapRecord Plan = new(RecordId.Decimal(RecordTypes.Minimap, 99), RecordId.Decimal(RecordTypes.MapSet, 99),
        [
            new MinimapSection(1, 1, 1, RecordId.Hex(RecordTypes.Image, 1), 10, 10),
            new MinimapSection(1, 1, 1, RecordId.Hex(RecordTypes.Image, 2), 10, 30),
            new MinimapSection(2, 1, 1, RecordId.Hex(RecordTypes.Image, 3), 40, 10, Seen: true),
            new MinimapSection(3, 1, 2, RecordId.Hex(RecordTypes.Image, 4), 0, 0),
        ],
        [new MinimapRule(1, 1, 1, Bounds: [new MinimapBound(MinimapAxis.Y, Above: 4)])]);

    [Fact]
    public void TheRulesChooseAPieceOrTheChunksFirst()
    {
        var flags = ItemData.Flags();
        Assert.Equal(1, Minimap.Select(Plan, 1, 1, new Vector3(0, 5, 0), flags));
        Assert.Equal(0, Minimap.Select(Plan, 1, 1, new Vector3(0, 4, 0), flags));
        Assert.Equal(2, Minimap.Select(Plan, 2, 1, default, flags));
        Assert.Null(Minimap.Select(Plan, 9, 9, default, flags));
    }

    [Fact]
    public void ThePlayersPieceShowsWithItsGroupsSeenPiecesAndTheBoxGrowsLinearly()
    {
        var seen = new MinimapSeen(PluginSet.Load([]));
        var minimap = new Minimap(seen, _ => (20, 10));
        minimap.Load(Plan);
        minimap.Choose(1, 1, default, ItemData.Flags());
        Assert.True(minimap.Showing && seen.IsSeen(Plan, 0));
        minimap.StepFrame();
        // Its group's seen pieces: B01's, seen from the start, and A01's lower one, now.
        Assert.Equal([RecordId.Hex(RecordTypes.Image, 3), RecordId.Hex(RecordTypes.Image, 1)], Pieces());

        // Shrinking: the plan's left goes from 177 to 237 a 14th at a time, linearly, the vignette marking it.
        minimap.Toggle(false);
        var lefts = new List<float>();
        for (var i = 0; i < 15; i++)
        {
            minimap.StepFrame();
            lefts.Add(minimap.Draw(1).Last().X);
        }
        Assert.Equal([181, 185, 189, 194], lefts.Take(4));
        Assert.Equal(237, lefts[13]);
        Assert.Equal(237, lefts[14]);
        // Centred: 237 + 30 less half the piece's 20 for its left.
        Assert.Equal(257f, minimap.Draw(1).Single(s => s.Image == RecordId.Hex(RecordTypes.Image, 1)).X);

        // Another group: its pieces alone, and the old group fading out where it was.
        minimap.Choose(3, 1, default, ItemData.Flags());
        minimap.StepFrame();
        Assert.Contains(RecordId.Hex(RecordTypes.Image, 4), Pieces());
        for (var i = 0; i < 26; i++)
            minimap.StepFrame();
        Assert.Equal([RecordId.Hex(RecordTypes.Image, 4)], Pieces());

        // No piece for the chunk: no minimap, the compass's place.
        minimap.Choose(9, 9, default, ItemData.Flags());
        Assert.False(minimap.Showing);

        List<RecordId> Pieces() => [.. minimap.Draw(1).Where(s => s.Clip is not null).Select(s => s.Image)];
    }
}
