using System.Text.Json;
using Aidyn.Core.Cinematics;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// The cinematics' player (<c>Cinematic.Tick</c>, <c>0x80053934</c> in 1.0), the caption box (<c>TextBand</c>) and the
/// Theatre's page, as 1.0's code has them.
/// </summary>
public class CinematicTests
{
    private static readonly RecordId Flag = RecordId.Decimal(RecordTypes.Flag, 3);

    private static CinematicShot Shot(int length, IReadOnlyList<CinematicCaption>? captions = null, IReadOnlyList<CinematicFade>? fades = null) =>
        new(RecordId.Hex(RecordTypes.Scene, 0x2218), RecordId.Hex(RecordTypes.Animation, 0x2219), length,
            RecordId.Hex(RecordTypes.Sequence, 0x0601), 0.1f, 35, captions ?? [], fades ?? []);

    private static CinematicPlayer Player(params CinematicShot[] shots) =>
        new(new CinematicRecord(RecordId.Decimal(RecordTypes.Cinematic, 0), "Gabrion's Search", shots, Flag), "Brian");

    /// <summary>Each tick's picture: the shot and its time, or null when the tick drew none.</summary>
    private static List<(int Shot, int Time)?> Run(CinematicPlayer player)
    {
        var ticks = new List<(int, int)?>();
        while (!player.Ended)
        {
            player.Step();
            ticks.Add(player.Drew ? (player.ShotIndex, player.Time) : null);
        }
        return ticks;
    }

    [Fact]
    public void AShotShowsTimesTwoToItsLengthLessTwoWithAnUndrawnTickBetweenShots()
    {
        var ticks = Run(Player(Shot(10), Shot(10)));

        // Shot 0 draws times 2 to 8, then a tick frees it; shot 1 starts and draws at once.
        Assert.Equal([.. Enumerable.Range(2, 7).Select(t => ((int, int)?)(0, t))], ticks[..7]);
        Assert.Null(ticks[7]);
        Assert.Equal([.. Enumerable.Range(2, 7).Select(t => ((int, int)?)(1, t))], ticks[8..15]);
        // Then the shot is freed, the list's end is found, and two more ticks pass before the end.
        Assert.Equal(19, ticks.Count);
        Assert.All(ticks[15..], Assert.Null);
    }

    [Fact]
    public void BOrStartEndsItTwoTicksLater()
    {
        var player = Player(Shot(100));
        for (var i = 0; i < 5; i++)
            player.Step();
        player.Skip();

        player.Step();
        Assert.False(player.Drew);
        Assert.False(player.Ended);
        player.Step();
        Assert.True(player.Ended);
        Assert.True(player.Skipped);
    }

    [Fact]
    public void ACaptionShowsFromItsStartForItsLengthWithTheLeadersName()
    {
        var player = Player(Shot(40, [new(4, 8, "Alaron! ALARON!"), new(10, 12, "Sir!")]));
        var shown = new List<string?>();
        for (var i = 0; i < 20; i++)
        {
            player.Step();
            shown.Add(player.Caption);
        }

        // The caption clock is the scene's time less 1: due at 4, the fourth tick, and shown for 8 − 4 ticks.
        Assert.Equal([null, null, null, "Brian! BRIAN!", "Brian! BRIAN!", "Brian! BRIAN!", "Brian! BRIAN!", null, null,
            "Sir!", "Sir!", null], shown[..12]);
    }

    [Fact]
    public void AShotWithoutCaptionsLetsTheLastOneRunOn()
    {
        // The caption outlasts its shot by a few ticks, which the next shot, having no list, carries on.
        var player = Player(Shot(10, [new(5, 12, "Long")]), Shot(10));
        var shown = new List<string?>();
        while (!player.Ended)
        {
            player.Step();
            if (player.Drew)
                shown.Add(player.Caption);
        }

        // Shown on the shot's 5th to 7th pictures, then the next shot's first four.
        Assert.Equal([null, null, null, null, "Long", "Long", "Long", "Long", "Long", "Long", "Long", null, null, null], shown);
    }

    [Fact]
    public void ACutFromColourFadesBackOverItsLengthLessFour()
    {
        var player = Player(Shot(100, fades: [new(CinematicFadeColor.Clear, 0, 40, 0)]));

        // Due on the first tick, which takes two ticks' worth; the colour is black until a fade sets another.
        player.Step();
        Assert.Equal(new Rgba(0, 0, 0, (byte)(int)((1 - 2f / 36) * 255)), player.Fade);
        for (var i = 0; i < 33; i++)
            player.Step();
        Assert.Equal(7, player.Fade.A);
        player.Step();
        Assert.Equal(0, player.Fade.A);
    }

    [Fact]
    public void AFadeToWhiteHoldsSixtyFourTicksAfterItsRampAndThenStops()
    {
        var player = Player(Shot(200, fades: [new(CinematicFadeColor.White, 10, 20, 0)]));
        var alphas = new List<int>();
        for (var i = 0; i < 120; i++)
        {
            player.Step();
            alphas.Add(player.Fade.A);
        }

        // Due on the clock's 10th tick, ramping over 16 ticks, and drawn 80 in all.
        Assert.All(alphas[..9], a => Assert.Equal(0, a));
        Assert.Equal([15, 31, 47], alphas[9..12]);
        Assert.Equal(255, alphas[24]);
        Assert.All(alphas[24..89], a => Assert.Equal(255, a));
        Assert.Equal(0, alphas[89]);
        Assert.Equal(new Rgba(255, 255, 255, 0), player.Fade);
    }

    [Fact]
    public void AShortFadeRampsForItsWholeLengthAndCutsOff()
    {
        // The Shadow's lightning: 4 in the table, 8 ticks.
        var player = Player(Shot(100, fades: [new(CinematicFadeColor.White, 2, 8, 0)]));
        var alphas = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            player.Step();
            alphas.Add(player.Fade.A);
        }

        Assert.Equal([0, 31, 63, 95, 127, 159, 191, 223, 255, 0, 0, 0], alphas);
    }

    [Fact]
    public void AFadesColourCarriesIntoTheNextShot()
    {
        var player = Player(Shot(20, fades: [new(CinematicFadeColor.Red, 0, 10, 0)]), Shot(20, fades: [new(CinematicFadeColor.Clear, 0, 10, 0)]));
        while (player.ShotIndex < 1 || !player.Drew)
            player.Step();

        // From red back to the picture.
        Assert.Equal(new Rgba(255, 0, 0, (byte)(int)((1 - 2f / 6) * 255)), player.Fade);
    }

    [Fact]
    public void ARecordWithoutAnAspectIsFramedForTheConsole()
    {
        var record = JsonSerializer.Deserialize<CinematicRecord>(
            """{ "id": "aidyn:cinematic/0", "title": null, "shots": [], "doneFlag": "aidyn:flag/3" }""", PluginJson.Options)!;

        Assert.Equal(4f / 3, record.Aspect);
    }

    // The caption box.

    private static TextLayout Font() => new(TextLayoutTests.Font());

    private static string Lines(int count) => string.Join('\n', Enumerable.Repeat("a", count));

    [Fact]
    public void TheCaptionBoxRunsFromItsTopToTheScreensFootInsideTheBars()
    {
        var layout = CaptionBox.Layout(Font(), "a", 320, 240);

        Assert.Equal(new ScreenRect(16, 200, 304, 240), layout.Panel.Band);
        Assert.Equal((11, 0, 16, 16), (layout.Panel.Top, layout.Panel.Bottom, layout.Panel.Left, layout.Panel.Right));
        Assert.Equal(new TextBlock("a", 25, 205, new ScreenRect(25, 205, 295, 225)), layout.Text);
    }

    [Theory]
    [InlineData(2, 198)]
    [InlineData(3, 187)]
    [InlineData(4, 176)]
    public void TheCaptionBoxGrowsUpwardFromTwoLines(int lines, int top)
    {
        var layout = CaptionBox.Layout(Font(), Lines(lines), 320, 240);

        Assert.Equal(top, layout.Panel.Band.Top);
        Assert.Equal(top + 5, layout.Text.Y);
    }

    [Fact]
    public void TheCaptionBoxIsTheFramesWidthAtTheFootOfAnyScreen()
    {
        var layout = CaptionBox.Layout(Font(), "a", 480, 360);

        Assert.Equal(new ScreenRect(96, 320, 384, 360), layout.Panel.Band);
        Assert.Equal((105, 325), (layout.Text.X, layout.Text.Y));
    }

    // The Theatre.

    [Fact]
    public void TheTheatreListsTitlesSeenAndDashesOtherwiseAndPlaysOnlyTitles()
    {
        using var plugins = new CinematicPlugins();
        var seen = new HashSet<RecordId> { RecordId.Decimal(RecordTypes.Flag, 3), RecordId.Decimal(RecordTypes.Flag, 590) };
        var theatre = new TheatreMenu(plugins.Plugins, seen.Contains);

        Assert.Equal(["Gabrion's Search", TheatreRow.Dashes, "Rooughah Naming", TheatreRow.Dashes], theatre.Items.Rows.Select(r => r.Label));
        theatre.MoveCursor(1);
        theatre.Accept();
        Assert.Null(theatre.Chosen);
        theatre.MoveCursor(1);
        theatre.Accept();
        Assert.Equal("Rooughah Naming", theatre.Chosen?.Title);
    }

    [Fact]
    public void TheaterOpensItsPageAndBGoesBackToTheListOnTheaterStill()
    {
        using var plugins = new CinematicPlugins();
        var system = new SystemMenu(plugins.Plugins, new Settings(), TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron())), plugins.Saves,
            isSet: id => id == RecordId.Decimal(RecordTypes.Flag, 3));
        system.MoveCursor((int)SystemItem.Theater);
        system.Accept();

        Assert.Equal(SystemPage.Theatre, system.Page);
        system.Accept();
        Assert.Equal("Gabrion's Search", system.TheatreChosen?.Title);
        Assert.True(system.Back());
        Assert.Equal((SystemPage.List, SystemItem.Theater), (system.Page, system.Items.Current));
        Assert.Null(system.TheatreChosen);
    }

    /// <summary>The four cinematics' records, with their titles and done flags.</summary>
    private sealed class CinematicPlugins : IDisposable
    {
        private readonly TempPlugins _temp = new();

        public CinematicPlugins()
        {
            var titles = new[] { ("Gabrion's Search", 3), ("Shadow Self Merge", 569), ("Rooughah Naming", 590), ("Coronation of Alaron", 601) };
            var records = titles.Select((t, i) => new CinematicRecord(RecordId.Decimal(RecordTypes.Cinematic, i), t.Item1, [Shot(100)],
                RecordId.Decimal(RecordTypes.Flag, t.Item2)));
            Plugins = _temp.Aidyn(("cinematics.json", JsonSerializer.Serialize(records, PluginJson.Options)));
        }

        public PluginSet Plugins { get; }

        /// <summary>A folder with no saves in it.</summary>
        public string Saves => _temp.Root;

        public void Dispose() => _temp.Dispose();
    }
}
