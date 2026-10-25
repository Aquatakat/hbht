using System.Globalization;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Import;

namespace HBHT.Rom.Tests;

[Collection(ImportedPluginCollection.Name)]
public sealed class QuestIndexImportTests(Na10Import fixture)
{
    [Fact]
    public void TheBasePluginsFlagsAreUsedByScriptsJournalAndMaps()
    {
        var plugins = fixture.Data.Set;
        var index = fixture.Data.Index;

        // The Gwernia opening: the Kendall cut-scene (Borg13 866) sets done flag 5, which derived flag 5423 reads.
        var kendall = Script(866);
        Assert.Contains(new FlagUse(Flag(5), kendall, FlagAccess.Set, FlagRole.ScriptDone), index.UsesOf(Flag(5)));
        Assert.Contains(new QuestEdge(kendall, Flag(5), FlagRole.ScriptDone, FlagAccess.Set), index.Upstream(Flag(5)));
        Assert.Equal([5, 9, 47], plugins.Get<FlagRecord>(Flag(5423)).Formula!.Inputs.Select(Number).Order());
        Assert.Equal([5, 9, 47], index.UsesBy(Flag(5423)).Where(u => u.Role == FlagRole.DerivedInput).Select(u => Number(u.Flag)).Order());
        Assert.Contains(new QuestEdge(Flag(5), Flag(5423), FlagRole.DerivedInput, FlagAccess.Read), index.Downstream(Flag(5)));
        Assert.Contains(new QuestEdge(Flag(47), Flag(5423), FlagRole.DerivedInput, FlagAccess.Read), index.Upstream(Flag(5423)));

        // Oriana's conversation (916) sets flag 50, which journal entry 1 lists.
        Assert.Contains(index.UsesOf(Flag(50)), u => u.By == Script(916) && u.Access == FlagAccess.Set && u.Role == FlagRole.ScriptOp && u.At is not null);
        Assert.Contains(new QuestEdge(Flag(50), RecordId.Decimal(RecordTypes.Journal, 1), FlagRole.JournalListed, FlagAccess.Read), index.Downstream(Flag(50)));

        // The keeper's conversation (890) checks flag 285 first and sets it. (The Lighthouse Key's own flag is 3430.)
        var keeper = Script(890);
        Assert.Equal([new QuestEdge(keeper, Flag(285), FlagRole.ScriptOp, FlagAccess.Set)], index.Upstream(Flag(285)));
        Assert.Contains(new FlagUse(Flag(285), keeper, FlagAccess.Read, FlagRole.ScriptOp, 0), index.UsesOf(Flag(285)));
        Assert.Contains(new QuestEdge(Flag(285), keeper, FlagRole.ScriptOp, FlagAccess.Read), index.Upstream(keeper));
        Assert.Contains(new QuestEdge(keeper, Flag(285), FlagRole.ScriptOp, FlagAccess.Set), index.Downstream(keeper));

        // The keeper's lines about the lighthouse, among the other scripts' that mention it, in script and node order.
        var found = index.FindText("lighthouse");
        Assert.Contains(found, t => t.Script == keeper);
        Assert.Equal(found, found.OrderBy(t => t.Script.Key, StringComparer.Ordinal).ThenBy(t => t.Node));
        Assert.Equal(found, index.FindText("LIGHTHOUSE"));
        Assert.All(found, t => Assert.Contains("lighthouse", plugins.Get<DialogueRecord>(t.Script).Nodes[t.Node].Text, StringComparison.OrdinalIgnoreCase));

        // A scene object's gate is a use by its voxel ID, except a gate on the constant flags 0 and 1.
        var gated = plugins.All<MapRecord>(RecordTypes.Map)
            .SelectMany(m => m.Objects.OfType<SceneObject>().Select(o => (Voxel: MapRecord.VoxelId(m.Id, o.Index), Gate: o.Visible!)))
            .ToList();
        Assert.Contains(gated, g => Number(g.Gate.Flag) > 1);
        foreach (var (voxel, gate) in gated.Take(2000))
        {
            var uses = index.UsesBy(voxel);
            if (Number(gate.Flag) > 1)
                Assert.Equal([new FlagUse(gate.Flag, voxel, FlagAccess.Read, FlagRole.ObjectVisible, Inverted: gate.Invert)], uses);
            else
                Assert.Empty(uses);
        }

        // The state flags record sets flags, all as the engine.
        var state = RecordId.Parse("aidyn:stateflags/0");
        Assert.All(index.UsesBy(state), u => Assert.Equal(FlagRole.Engine, u.Role));
        Assert.Contains(index.UsesBy(state), u => u.Access == FlagAccess.Set);
    }
}
