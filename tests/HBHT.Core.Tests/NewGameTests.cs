namespace HBHT.Core.Tests;

/// <summary>Plugins without the new game record, from an older import, aren't played: the game says to import again.</summary>
public sealed class NewGameTests
{
    [Fact]
    public void PluginsWithoutTheNewGameRecordSayToImportAgain()
    {
        using var temp = new TempPlugins();
        var plugins = temp.Aidyn();
        var refused = Assert.Throws<InvalidDataException>(() => GameSession.NewGame(plugins));
        Assert.Contains("aidyn:newgame/0", refused.Message);
        Assert.Contains("hbht import", refused.Message);
    }
}
