using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>Walking into map objects and onto trigger triangles in trek, as the story's cut-scenes need it.</summary>
public sealed partial class GameSessionTests
{
    [Fact]
    public void TheCampSouthOfTheCastleGivesItsJournalEntryAndItsCutSceneOnlyOnItsTriangles()
    {
        // M08's trigger (object 0, radius 5) sets 58, which enables the camp's dialogue (object 1, radius 200, by 5521).
        var (session, _) = Start("M08", 50, 76.2f);
        var trek = session.Trek;
        var flags = session.Game.Flags;
        Tick(session);
        // The trigger's write brings the flags up to date at once, as the original's read of 5626 after it does
        // (0x80010bc4), with nothing else reading a derived flag: its journal entry is waiting, and the dialogue enabled.
        Assert.True(flags.Peek(flags.Handle(RecordId.Decimal(RecordTypes.Flag, 58))));
        Assert.True(flags.Peek(flags.Handle(RecordId.Decimal(RecordTypes.Flag, 5521))));
        Assert.True(flags.Peek(flags.Handle(session.Game.StateFlagsRecord.NewJournalEntry)));
        // The next tick tells it, on the approach.
        Tick(session);
        Assert.Equal(TrekLoop.NewJournalEntry, trek.Use.Popup?.Body?.Text);
        for (var i = 0; i < 10; i++)
            Tick(session);

        // No grid cell lists the dialogue, so walking near it starts nothing.
        Assert.Null(trek.Cued);

        // Its trigger triangles start it, wherever they are, once the popup has closed.
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        trek.Use.Tread(map, 1, new System.Numerics.Vector3(1000, 0, 1000));
        Assert.Null(trek.Cued);
        trek.Use.Accept();
        trek.Use.Tread(map, 1, new System.Numerics.Vector3(1000, 0, 1000));
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x04E0), trek.Cued!.Value.Script.Id);
    }
}
