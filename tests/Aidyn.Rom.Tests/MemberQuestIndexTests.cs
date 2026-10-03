using System.Globalization;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using RomFlagRole = Aidyn.Rom.Quests.FlagRole;

namespace Aidyn.Rom.Tests;

/// <summary>
/// What the quest index says of party members' flags over NA 1.0's imported plugins, and that it agrees with the ROM's quest atlas
/// on which scripts add and remove them.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MemberQuestIndexTests(MapObjectFixture fixture)
{
    private MapObjectFixture.Loaded Data => fixture.Data;

    private static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    private static RecordId Script(int borg) => RecordId.Hex(RecordTypes.Dialogue, borg);

    private static RecordId Entity(int n) => RecordId.Hex(RecordTypes.Entity, n);

    private static int Number(RecordId flag) => int.Parse(flag.Key, CultureInfo.InvariantCulture);

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

    /// <summary>
    /// The ROM's atlas records an AddMember or RemoveMember as a write of the member's joined flag, and their left flag if they have one,
    /// by a script's node. The index lists the same uses by the same scripts: the atlas marks a node's second effect ignored and the
    /// index has no such mark, so both are compared as uses.
    /// </summary>
    [Fact]
    public void ScriptsAddAndRemoveMembersAsTheRomsQuestAtlasSays()
    {
        RomFlagRole[] roles = [RomFlagRole.MemberAdded, RomFlagRole.MemberRemoved];
        var rom = Data.Atlas.References
            .Where(r => r.Flag > 1 && roles.Contains(r.Role) && r.Source is Quests.ScriptSource)
            .Select(r => (By: Script(((Quests.ScriptSource)r.Source).Script), At: ((Quests.ScriptSource)r.Source).Node,
                Flag: (int)r.Flag, Access: r.Access.ToString(), Role: r.Role.ToString()))
            .ToHashSet();
        var plugin = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue)
            .SelectMany(s => Data.Index.UsesBy(s.Id))
            .Where(u => u.Role is FlagRole.MemberAdded or FlagRole.MemberRemoved)
            .Select(u => (u.By, u.At, Flag: Number(u.Flag), Access: u.Access.ToString(), Role: u.Role.ToString()))
            .ToHashSet();

        Assert.Contains(rom, u => u is { Role: "MemberAdded", Access: "Set" });
        Assert.Contains(rom, u => u is { Role: "MemberAdded", Access: "Clear" });
        Assert.Contains(rom, u => u is { Role: "MemberRemoved", Access: "Set" });
        Assert.Contains(rom, u => u is { Role: "MemberRemoved", Access: "Clear" });
        Assert.Empty(rom.Except(plugin).Select(u => $"ROM only: {u}"));
        Assert.Empty(plugin.Except(rom).Select(u => $"plugin only: {u}"));
    }
}
