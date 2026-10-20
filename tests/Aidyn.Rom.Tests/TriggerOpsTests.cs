using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Events;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Rom.Tests;

/// <summary>
/// A plugin's triggers run their conditions and ops as a script's nodes do (<see cref="ScriptOps"/>), over a new game on
/// NA 1.0's records: skill checks honour the random flag, the gold's and experience's sounds play, and a True Name given
/// is saved.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class TriggerOpsTests(Na10Import fixture)
{
    /// <summary>A script no plugin has, for the triggers to be about.</summary>
    private static readonly RecordId Ended = Script(0xFFF0);
    private static readonly RecordId Marker = Flag(3442);

    private static TriggerRecord On(int n, IReadOnlyList<TriggerCondition>? conditions, params TriggerEffect[] effects) =>
        new(RecordId.Decimal(RecordTypes.Trigger, 9000 + n), GameEvents.ScriptEnded, Ended, conditions, effects);

    private static TriggerEffect.Op Op(DialogueCommand command, int? value = null, RecordId? target = null) => new(command, value, target);

    private static TriggerEffect? Fire(Triggers triggers, GameState game, IScriptHost? host = null)
    {
        var warnings = new List<string>();
        var effect = triggers.Fire(game, GameEvents.ScriptEnded, Ended, warnings.Add, host);
        Assert.Empty(warnings);
        return effect;
    }

    [Fact]
    public void ASkillCheckRollsWhileTheRandomFlagIsSetAsAScriptsDoes()
    {
        var game = GameState.NewGame(fixture.Data.Set);
        game.Flags.Set(game.StateFlagsRecord.RandomSkillChecks!.Value, true);

        // A script's check rolls the party's step, then, with the flag set, 0–21 in its place (ScriptOps.SkillCheck). A seed
        // whose roll isn't the step tells them apart.
        (int Step, int Roll) Check(uint seed)
        {
            var random = new Lcg(seed);
            var step = game.Party.SkillCheck(Skill.Ranger, random) & 0xFF;
            return (step, random.Range(0, 21));
        }
        var seed = Enumerable.Range(1, 1000).Select(s => (uint)s).First(s => Check(s) is var (step, roll) && step != roll);
        var (step, roll) = Check(seed);

        var triggers = new Triggers([On(0, [new TriggerCondition(DialogueCommand.CheckSkill, (int)Skill.Ranger,
            Range: new DialogueRange(roll, roll))], Op(DialogueCommand.SetFlag, target: Marker))]);
        game.Random.World.Seed = seed;
        Fire(triggers, game);
        Assert.True(game.Flags.IsSet(Marker));

        // The step alone, as the trigger's own copy of the op rolled, doesn't hold.
        game.Flags.Set(Marker, false);
        var stepOnly = new Triggers([On(0, [new TriggerCondition(DialogueCommand.CheckSkill, (int)Skill.Ranger,
            Range: new DialogueRange(step, step))], Op(DialogueCommand.SetFlag, target: Marker))]);
        game.Random.World.Seed = seed;
        Fire(stepOnly, game);
        Assert.False(game.Flags.IsSet(Marker));
    }

    [Fact]
    public void GoldAndExperienceCueTheirSoundsAsAScriptsDo()
    {
        var game = GameState.NewGame(fixture.Data.Set);
        var triggers = new Triggers([On(0, null,
            Op(DialogueCommand.AddGold, 0x1_0019),
            Op(DialogueCommand.TakeGold, 10),
            Op(DialogueCommand.TakeGold, 1000),
            Op(DialogueCommand.AddExp, 50, GameState.Alaron))]);
        var gold = game.Party.Gold;
        var host = new CueLog();

        Fire(triggers, game, host);

        // AddGold's amount is a u16, as the VM masks it; the second TakeGold is more than the party has, so does nothing.
        Assert.Equal(gold + 25 - 10, game.Party.Gold);
        Assert.Equal([new SoundCue(ScriptSound.GoldGained), new SoundCue(ScriptSound.GoldPaid), new SoundCue(ScriptSound.Experience)],
            host.Seen);

        // With no host, the ops still happen, silently.
        Fire(triggers, game);
        Assert.Equal(gold + 2 * (25 - 10), game.Party.Gold);
    }

    [Fact]
    public void ATrueNameATriggerGivesIsSavedAndLoaded()
    {
        var set = fixture.Data.Set;
        // Everyone who can join knows theirs from their record, so a character who can't, whose record gives none and has no
        // flag to, as a mod's member might.
        var member = set.All<EntityRecord>(RecordTypes.Entity).First(e => e is { TrueName: false, TrueNameFlag: null }).Id;
        var game = GameState.NewGame(set);
        var triggers = new Triggers([On(0, null, Op(DialogueCommand.AddMember, target: member), new TriggerEffect.TrueName(member))]);

        Fire(triggers, game);
        Assert.True(game.Party.Get(member)!.TrueName);

        var save = SaveFile.Parse(SaveFile.Serialize(game.Save()));
        Assert.True(GameState.Load(set, save).Party.Get(member)!.TrueName);

        // A save from before True Names were saved takes theirs from the record and the flags, which give none.
        var older = save with { Party = save.Party with { Members = [.. save.Party.Members.Select(m => m with { TrueName = null })] } };
        Assert.False(GameState.Load(set, older).Party.Get(member)!.TrueName);
    }
}
