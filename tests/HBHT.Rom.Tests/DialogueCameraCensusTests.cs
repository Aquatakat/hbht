using HBHT.Core;
using HBHT.Core.Cameras;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>The enhanced conversation camera over the real data: the shots real scripts get, and where its shots fit about the npcs that talk.</summary>
public sealed class DialogueCameraCensusTests(Na10Import fixture, ITestOutputHelper output)
{
    private Na10Import.Loaded Data => fixture.Data;

    private (List<DialoguePage> Pages, IReadOnlyList<DialogueShot> Shots) Plan(int script, params int[] choices)
    {
        var record = Data.Set.Get<ScriptRecord>(Script(script));
        var pages = DialoguePlanner.FirstPath(GameState.NewGame(Data.Set), record, choices: choices);
        return (pages, DialoguePlanner.Plan(record, pages));
    }

    [Fact]
    public void BaldricsTalesGoWideOnceEachAndToAlaronOnce()
    {
        // His tale of Elisheva: 30 lines of his in a row, from page 5, after the menu.
        var (pages, shots) = Plan(0x046A, 2, 3);
        Assert.Equal(DialogueShot.TwoShot, shots[0]);
        Assert.All(pages.Take(35), p => Assert.NotEqual(Talker.None, p.Speaker));
        var tale = shots.Skip(5).Take(30).ToList();
        Assert.Equal(DialogueShot.Wide, tale[2]);
        // The cycle brings the wide and Alaron back every 12 lines: wide at lines 3, 13 and 25, Alaron at 5, 17 and 29, not the last.
        Assert.Equal([2, 12, 24], tale.Select((s, i) => (s, i)).Where(x => x.s == DialogueShot.Wide).Select(x => x.i));
        Assert.Equal([4, 16, 28], tale.Select((s, i) => (s, i)).Where(x => x.s == new DialogueShot(DialogueShotKind.Single, Talker.Player)).Select(x => x.i));
        Assert.Contains(new DialogueShot(DialogueShotKind.Single, Talker.Npc), tale);
    }

    [Fact]
    public void TheGoblinsLastWordBeforeTheFightGoesWide()
    {
        var (pages, shots) = Plan(0x03CD, 2, 0);
        Assert.StartsWith("SAY!!!!", pages[^1].Text);
        Assert.Equal(DialogueShot.Wide, shots[^1]);
        // His shouted menu punches in on him.
        Assert.Equal(new DialogueShot(DialogueShotKind.Punch, Talker.Npc), shots[1]);
    }

    [Fact]
    public void ATwoWayTalkGoesOverTheListenersShoulderToEachSpeaker()
    {
        // Phelan, before the journey: every line after the first at its speaker, over the other's shoulder.
        var (pages, shots) = Plan(0x03A4);
        Assert.Equal(DialogueShot.TwoShot, shots[0]);
        for (var i = 1; i < pages.Count; i++)
            Assert.Equal(new DialogueShot(DialogueShotKind.OverShoulder, pages[i].Speaker), shots[i]);
    }

    [Fact]
    public void RealScriptsKeepTheirBeatsUnderEverySeed()
    {
        var baldric = Data.Set.Get<ScriptRecord>(Script(0x046A));
        var tale = DialoguePlanner.FirstPath(GameState.NewGame(Data.Set), baldric, choices: [2, 3]);
        var goblin = Data.Set.Get<ScriptRecord>(Script(0x03CD));
        var menace = DialoguePlanner.FirstPath(GameState.NewGame(Data.Set), goblin, choices: [2, 0]);
        var phelan = Data.Set.Get<ScriptRecord>(Script(0x03A4));
        var talk = DialoguePlanner.FirstPath(GameState.NewGame(Data.Set), phelan);
        var openings = new HashSet<DialogueShot>();
        for (var i = 0; i < 300; i++)
        {
            var seed = DialoguePlanner.SeedOf(Script(0x046A), Script(0x046A), i);
            var shots = DialoguePlanner.Plan(baldric, tale, seed: seed).Skip(5).Take(30).ToList();
            Assert.Equal(DialogueShot.Wide, shots[2]);
            Assert.Equal(new DialogueShot(DialogueShotKind.Single, Talker.Player), shots[4]);
            Assert.NotEqual(new DialogueShot(DialogueShotKind.Single, Talker.Player), shots[^1]);
            var fight = DialoguePlanner.Plan(goblin, menace, seed: seed);
            Assert.Equal(DialogueShot.Wide, fight[^1]);
            Assert.Equal(new DialogueShot(DialogueShotKind.Punch, Talker.Npc), fight[1]);
            // Every line of a two-way talk shows its speaker, held in a two-shot or wide, or at them.
            var plan = DialoguePlanner.Plan(phelan, talk, seed: seed);
            openings.Add(plan[0]);
            for (var p = 1; p < talk.Count; p++)
                Assert.True(plan[p].Shows(talk[p].Speaker), $"{seed}: {p} {plan[p]}");
        }
        Assert.Equal(3, openings.Count);
    }

    [Theory]
    [InlineData(DialogueCensus.Touching)]
    [InlineData(DialogueCensus.Furthest)]
    public void TheShotsFitAboutNearlyEveryNpcThatTalks(float gap)
    {
        var placings = DialogueCensus.Run(Data.Set, DialogueTuning.Default, gap);
        var writer = new StringWriter();
        DialogueCensus.Report(placings, writer);
        output.WriteLine(writer.ToString());

        Assert.True(placings.Count > 2000, $"{placings.Count}");
        var all = DialogueCensus.Shares(placings);
        var interior = DialogueCensus.Shares(placings.Where(p => p.Interior).ToList());
        // Nov 14, 2026, the side chosen by what fits: touching, 97%, 98%, 97% and 0% of 2,599 placings, interiors 95%, 97%, 95% and 0%;
        // furthest, 97%, 98%, 97% and 0% of 2,431, interiors 94%, 98%, 94% and 0%.
        Assert.True(all.Shoulder > 0.94 && all.Single > 0.95 && all.TwoShot > 0.94 && all.OriginalOnly < 0.02, $"{all}");
        // Alaron's share of the frame's width, his shoulders counted (DialogueTuning.ShoulderPadOut): over the shoulder, a median of 26% and
        // 50–52% at the 90th percentile, which is the tight placings of small rooms (36% and 50–52% before the roomy placing was pulled
        // out, Nov 14, 2026); in the single, none.
        static float At(IEnumerable<float?> covers, double share)
        {
            var c = covers.OfType<float>().Order().ToList();
            return c[(int)(c.Count * share)];
        }
        Assert.True(At(placings.Select(p => p.ShoulderCover), 0.5) < 0.30f && At(placings.Select(p => p.ShoulderCover), 0.9) < 0.56f);
        Assert.True(At(placings.Select(p => p.SingleCover), 0.9) <= DialogueTuning.Default.SingleSliver);
        Assert.True(placings.Count(p => p.ShoulderPlacing == 0) > 0.75 * placings.Count(p => p.ShoulderPlacing >= 0));
        Assert.True(placings.Count(p => p.ShoulderPlacing <= 1) > 0.85 * placings.Count(p => p.ShoulderPlacing >= 0));
        Assert.True(placings.Count(p => p.SingleFellToShoulder) < 0.03 * placings.Count);
        Assert.True(interior.Shoulder > 0.91 && interior.Single > 0.94 && interior.TwoShot > 0.91 && interior.OriginalOnly < 0.02, $"{interior}");
    }
}
