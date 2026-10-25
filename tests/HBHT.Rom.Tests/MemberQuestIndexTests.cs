using System.Globalization;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// What the quest index says of party members' flags over NA 1.0's imported plugins.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MemberQuestIndexTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    [Fact]
    public void GodricsJoinedFlagIsSetWhenAScriptAddsHimAndHisLeftFlagClearedThere()
    {
        var index = Data.Index;
        var script = Script(0x03A1);
        var godric = Entity(0x0248);

        // Node 29 of Godric's recruiting script is his AddMember.
        Assert.Equal("AddMember", Data.Set.Get<DialogueRecord>(script).Nodes[29].Ops!.Single().Command.ToString());
        Assert.Contains(new FlagUse(Flag(90), script, FlagAccess.Set, FlagRole.MemberAdded, 29), index.UsesOf(Flag(90)));
        Assert.Contains(new FlagUse(Flag(90), godric, FlagAccess.Set, FlagRole.Member), index.UsesOf(Flag(90)));
        Assert.Contains(new FlagUse(Flag(686), script, FlagAccess.Clear, FlagRole.MemberAdded, 29), index.UsesOf(Flag(686)));
        Assert.Contains(new FlagUse(Flag(686), godric, FlagAccess.Set, FlagRole.Member), index.UsesOf(Flag(686)));
        Assert.Contains(new FlagUse(Flag(616), godric, FlagAccess.Set, FlagRole.Member), index.UsesOf(Flag(616)));
        Assert.Contains(new QuestEdge(script, Flag(90), FlagRole.MemberAdded, FlagAccess.Set), index.Upstream(Flag(90)));
        Assert.Contains(new QuestEdge(godric, Flag(90), FlagRole.Member, FlagAccess.Set), index.Upstream(Flag(90)));
        Assert.Contains(new QuestEdge(script, Flag(686), FlagRole.MemberAdded, FlagAccess.Clear), index.Downstream(script));
    }

    [Fact]
    public void ARemoveMemberClearsTheJoinedFlagAndSetsTheLeftFlag()
    {
        var index = Data.Index;
        var script = Script(0x04EF);

        // Node 63 of script 0x04EF takes Dougal out of the party.
        var op = Data.Set.Get<DialogueRecord>(script).Nodes[63].Ops!.Single();
        Assert.Equal(("RemoveMember", Entity(0x0247)), (op.Command.ToString(), op.Target));
        Assert.Contains(new FlagUse(Flag(574), script, FlagAccess.Clear, FlagRole.MemberRemoved, 63), index.UsesOf(Flag(574)));
        Assert.Contains(new FlagUse(Flag(695), script, FlagAccess.Set, FlagRole.MemberRemoved, 63), index.UsesOf(Flag(695)));
        Assert.Equal(
            [Flag(574), Flag(695)],
            index.UsesBy(script).Where(u => u is { Role: FlagRole.MemberRemoved, At: 63 }).Select(u => u.Flag).OrderBy(f => Number(f)));
    }

    [Fact]
    public void ARecruitableMembersRecordUsesItsThreeFlagsAndBrennaHasTwo()
    {
        var index = Data.Index;

        Assert.Equal(
            [(90, FlagAccess.Set), (616, FlagAccess.Set), (686, FlagAccess.Set)],
            index.UsesBy(Entity(0x0248)).Where(u => u.Role == FlagRole.Member).Select(u => (Number(u.Flag), u.Access)).OrderBy(u => u.Item1));
        Assert.Equal([92, 613], index.UsesBy(Entity(0x0243)).Where(u => u.Role == FlagRole.Member).Select(u => Number(u.Flag)).Order());

        // Everyone with a Member and no one else: Alaron and Trahern use none.
        var members = Data.Set.All<EntityRecord>(RecordTypes.Entity).Where(e => e.Member is not null).Select(e => e.Id).ToHashSet();
        Assert.Equal(13, members.Count);
        var users = Data.Set.All<EntityRecord>(RecordTypes.Entity).Where(e => index.UsesBy(e.Id).Any(u => u.Role == FlagRole.Member)).Select(e => e.Id).ToHashSet();
        Assert.Equal(members, users);
        Assert.DoesNotContain(Alaron, users);
        Assert.DoesNotContain(Entity(0x0239), users);
    }

    private static readonly RecordId Alaron = Entity(0x023F);
}
