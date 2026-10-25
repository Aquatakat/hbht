using Aidyn.Plugins.Records;
using Aidyn.Core.Ui;

namespace Aidyn.Core.Tests;

/// <summary>
/// The title's intro cards (<c>Title.CardsFrame</c>) and its menu (<c>IntroMenu</c>), the System tab's pages in the title's kind, as 1.0's
/// code has them.
/// </summary>
public class TitleMenuTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private SystemMenu TitleMenu(Core.Characters.Character alaron) =>
        new(fixture.Plugins, new Settings(), TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), alaron), Path.GetTempPath(), _ => [],
            title: true);

    [Fact]
    public void TheCardsFadeIn15ATickHoldAndAreBlack36TicksFromTheirFadeOut()
    {
        var cards = new IntroCards(skippable: false);
        List<(IntroCard, int)> ticks = [];
        while (!cards.Ended)
        {
            cards.Step();
            ticks.Add((cards.Card, cards.Alpha));
        }

        // 53 ticks a card and its hold: the Pak's 30, the rest 120 (Title.CardFade, 0x8001b250).
        Assert.Equal(775, ticks.Count);
        Assert.Equal((IntroCard.ExpansionPak, 15), ticks[0]);
        Assert.Equal((IntroCard.ExpansionPak, 255), ticks[16]);
        Assert.Equal((IntroCard.ExpansionPak, 255), ticks[16 + 30]);
        Assert.Equal((IntroCard.ExpansionPak, 240), ticks[16 + 31]);
        Assert.Equal((IntroCard.ExpansionPak, 0), ticks[16 + 30 + 17]);
        Assert.Equal((IntroCard.Legal, 15), ticks[83]);
        // Each card's first tick, alpha 15.
        Assert.Equal([0, 83, 256, 429, 602], ticks.Select((t, i) => (t, i)).Where(x => x.t.Item2 == 15 && (x.i == 0 || ticks[x.i - 1].Item2 == 0)).Select(x => x.i));
        // No button skips the original's.
        Assert.False(new IntroCards(skippable: false).Press(MenuAction.Start));
    }

    [Fact]
    public void TheN64LogosCameraGoesRoundItTwoDegreesATick()
    {
        var cards = new IntroCards(skippable: false);
        while (cards.Card != IntroCard.Nintendo)
            cards.Step();
        Assert.Equal(0, cards.LogoAngle);
        cards.Step();
        cards.Step();
        Assert.Equal(4, cards.LogoAngle);
    }

    [Theory]
    [InlineData(MenuAction.Accept)]
    [InlineData(MenuAction.Back)]
    [InlineData(MenuAction.Start)]
    public void WithASaveToLoadABOrStartSkipsEveryCard(MenuAction press)
    {
        var cards = new IntroCards(skippable: true);
        cards.Step();
        Assert.False(cards.Press(MenuAction.Up));
        Assert.True(cards.Press(press));
        Assert.True(cards.Ended);
    }

    [Fact]
    public void TheTitlesListIsNewGameLoadGameAndOptionsInOffWhite()
    {
        var menu = TitleMenu(Characters.Create(Characters.Alaron()));

        Assert.Equal([SystemItem.NewGame, SystemItem.LoadGame, SystemItem.Options], menu.Items.Rows);
        Assert.Equal(["L159", "L160", "L161"], menu.Items.Rows.Select(menu.Label));
        Assert.Same(SystemStyle.Title, menu.Style);
        Assert.Equal(new Rgba(225, 225, 225, 255), menu.Style.Text);
        // B on the list does nothing: there's no menu to close.
        Assert.False(menu.Back());
        Assert.Equal(SystemPage.List, menu.Page);

        // Its options have no Rename Character.
        menu.Items.Point(2);
        menu.Accept();
        Assert.Equal(["Audio", "Video", "Enhancements", "Controls"], menu.Options!.Rows.Rows.Select(r => r.Label));
    }

    [Fact]
    public void ItsPagesCrossfade20ATick()
    {
        var menu = TitleMenu(Characters.Create(Characters.Alaron()));
        menu.FadeIn();
        Assert.True(menu.Fading);
        List<byte> alphas = [];
        while (menu.Fading)
        {
            menu.Step();
            alphas.Add(menu.PageAlpha);
        }

        // IntroMenu.Step (0x80042d0c): 13 ticks.
        Assert.Equal(13, alphas.Count);
        Assert.Equal([20, 40, 60], alphas.Take(3));
    }

    [Fact]
    public void NewGameAsksAlaronsNameAndStartEndsTheMenuWithIt()
    {
        var alaron = Characters.Create(Characters.Alaron());
        var menu = TitleMenu(alaron);
        // Start on the list chooses, as A does (IntroMenu.OnStart).
        Assert.True(menu.Start());
        Assert.Equal(SystemPage.Rename, menu.Page);
        Assert.Equal(alaron.Name, menu.Rename!.Name);

        menu.TypeName('x');
        // The cursor's on OK after typing? Start goes to OK, and again confirms.
        menu.Start();
        Assert.Null(menu.Ended);
        menu.Start();
        Assert.Equal(PauseExit.NewGame(alaron.RecordName + "x"), menu.Ended);
        // The session names him as the game begins; the menu leaves him be.
        Assert.Equal(alaron.RecordName, alaron.Name);
        // Nothing more reaches the name entry.
        Assert.False(menu.TypeName('y'));
    }

    [Fact]
    public void BOnTheNameEntryGoesBackToTheList()
    {
        var menu = TitleMenu(Characters.Create(Characters.Alaron()));
        menu.Accept();
        Assert.True(menu.Back());
        Assert.Equal(SystemPage.List, menu.Page);
        Assert.Null(menu.Rename);
        Assert.Null(menu.Ended);
    }
}
