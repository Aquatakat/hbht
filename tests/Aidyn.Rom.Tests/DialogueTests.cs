using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;

namespace Aidyn.Rom.Tests;

public class DialogueTests
{
    private static readonly Lazy<(BorgArchive Archive, List<(int Index, Borg13Script Script)> Scripts)> Na10 = new(() =>
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var scripts = archive.Listings.Where(l => l.Type == BorgType.Dialogue)
            .Select(l => (l.Index, Borg13Script.Parse(archive.Read(l.Index)))).ToList();
        return (archive, scripts);
    });

    [Fact]
    public void EveryBorg13Parses()
    {
        var scripts = Na10.Value.Scripts.Select(s => s.Script).ToList();
        var nodes = scripts.SelectMany(s => s.Nodes).ToList();

        Assert.Equal(892, scripts.Count);
        Assert.Equal(26_837 - 153, nodes.Count);
        Assert.Equal([596, 211, 78, 6, 1],
            Enum.GetValues<Borg13StartMode>().Select(m => scripts.Count(s => s.StartMode == m)));
        Assert.Equal([23_641 + 1_314, 937, 118, 674],
            Enum.GetValues<Borg13NodeKind>().Select(k => nodes.Count(n => n.Kind == k)));
        Assert.All(scripts, s => Assert.Equal(s.StartMode == Borg13StartMode.Backdrop, s.Backdrop != 14));
    }

    [Fact]
    public void LinksMatchTheNodeKinds()
    {
        foreach (var node in Na10.Value.Scripts.SelectMany(s => s.Script.Nodes))
        {
            switch (node.Kind)
            {
                case Borg13NodeKind.Line:
                    Assert.InRange(node.Links.Count, 0, 1);
                    break;
                case Borg13NodeKind.If:
                    Assert.Equal(2, node.Links.Count);
                    Assert.True(node.Condition?.Opcode is Borg13Opcode.CheckMember or Borg13Opcode.CheckFlag or Borg13Opcode.CheckItem);
                    break;
                case Borg13NodeKind.Case:
                    Assert.InRange(node.Links.Count, 2, 8);
                    Assert.True(node.Condition?.Opcode is Borg13Opcode.CheckSkill or Borg13Opcode.CheckPartySize or Borg13Opcode.CheckGold);
                    break;
            }
        }
    }

    /// <summary>
    /// The game runs only the first effect op of a node. The tools evidently allowed one: only
    /// four nodes have a second, and one of those is a SetFlag that never runs.
    /// </summary>
    [Fact]
    public void AlmostEveryNodeHasAtMostOneEffect()
    {
        var extra = Na10.Value.Scripts
            .SelectMany(s => s.Script.Nodes.Select(n => (s.Index, n.Index, Effects: n.Ops.Where(o => o.Opcode.IsEffect()).ToList())))
            .Where(x => x.Effects.Count > 1)
            .Select(x => (x.Item1, x.Item2, x.Effects[1].Opcode))
            .ToList();

        Assert.Equal([(887, 33, Borg13Opcode.Wait), (1019, 88, Borg13Opcode.SetFlag), (1020, 78, Borg13Opcode.Wait),
            (1146, 78, Borg13Opcode.Wait)], extra);
    }

    [Fact]
    public void OperandsReferToThingsThatExist()
    {
        var (archive, scripts) = Na10.Value;
        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));
        // 0xCCCC is the tools' fill for an unset value. Script 1294 has it as an actor and a speaker.
        var entities = db.Entities.Select(e => e.Id).Concat(db.DialogEntities.Select(e => e.Id)).Append(new ItemId(0xCCCC)).ToHashSet();

        foreach (var (_, script) in scripts)
        {
            // Conversations don't set their flag, and two of them name derived flags.
            if (script.StartMode != Borg13StartMode.Conversation)
                Assert.InRange(script.DoneFlag, 2, db.GameStateFunnel.BitFlagCount - 1);
            Assert.All(script.Actors, a => Assert.Contains(a, entities));
            foreach (var op in script.Nodes.SelectMany(n => n.Ops))
            {
                switch (op.Opcode)
                {
                    case Borg13Opcode.SetFlag or Borg13Opcode.ClearFlag:
                        Assert.InRange(op.Value, 2, db.GameStateFunnel.BitFlagCount - 1);
                        break;
                    case Borg13Opcode.CheckFlag:
                        Assert.InRange(op.Value, 0, db.GameStateFunnel.Flags.Count - 1);
                        break;
                    case Borg13Opcode.SetSpeaker or Borg13Opcode.CameraCutToActor or Borg13Opcode.AddMember
                        or Borg13Opcode.RemoveMember or Borg13Opcode.CheckMember:
                        Assert.Contains(new ItemId(op.Value), entities);
                        break;
                    case Borg13Opcode.RandomLine:
                        Assert.Equal(BorgType.Dialogue, archive.Listings[op.Value].Type);
                        break;
                }
            }
        }
    }
}
