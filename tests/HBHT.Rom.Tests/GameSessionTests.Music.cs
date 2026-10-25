using HBHT.Core;
using HBHT.Core.Audio;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>The music as trek hands over to a cut-scene's stage, on NA 1.0's data.</summary>
public sealed partial class GameSessionTests
{
    /// <summary>A tick, and the audio's, as the runtime steps it every tick.</summary>
    private static SessionStep TickHeard(GameSession session, GameAudio audio)
    {
        var step = Tick(session);
        audio.Step();
        return step;
    }

    [Fact]
    public void AStagesMusicTakesOverOnceItHasFadedInSoTheDreamWorldStopsTheWizardsSchools()
    {
        // The Wizard's School, the Jundar interiors' K01, whose music is its own (Zones.JundarBgmList).
        var (session, _) = Start("K01", 8.3f, 8.4f, mapSet: 11);
        var audio = session.Zone.Audio;
        var school = RecordId.Hex(RecordTypes.Sequence, 0x061C);
        for (var i = 0; i < 120; i++)
            TickHeard(session, audio);
        Assert.Equal(school, audio.MusicPlaying);

        // Ardra heals Alaron (#1926), staged in the school itself: its music plays on.
        var heal = PlayCutScene(session, 0x03FE);
        Assert.Equal((RecordId.Decimal(RecordTypes.MapSet, 11), 11, 1), (heal.Stage.Set.Id, heal.Stage.Chunk.Row, heal.Stage.Chunk.Column));
        while (heal.Fade.Fading)
            TickHeard(session, audio);
        TickHeard(session, audio);
        Assert.Equal(school, audio.MusicPlaying);
        session.EndCutScene();

        // The dream world (#1981) is staged in the Misc interiors' C03, which has no music: the school's plays on through the stage's
        // fade-in (Bgm.Update waits for the fade) and stops once the screen is up.
        var dream = PlayCutScene(session, 0x03FF);
        Assert.Equal((RecordId.Decimal(RecordTypes.MapSet, 6), 3, 3), (dream.Stage.Set.Id, dream.Stage.Chunk.Row, dream.Stage.Chunk.Column));
        Assert.True(dream.Fade.Fading);
        TickHeard(session, audio);
        Assert.Equal(school, audio.MusicPlaying);
        while (dream.Fade.Fading)
            TickHeard(session, audio);
        TickHeard(session, audio);
        Assert.Null(audio.MusicPlaying);

        // Trek coming back to the school asks for its music again, which starts over.
        session.EndCutScene();
        for (var i = 0; i < 120; i++)
            TickHeard(session, audio);
        Assert.Equal(school, audio.MusicPlaying);
    }
}
