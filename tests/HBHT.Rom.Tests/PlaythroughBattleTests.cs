using System.Numerics;
using HBHT.Core;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Playthrough;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's battles (docs/systems/playthrough.md, layer 5) on NA 1.0's data and the user's checkpoints: the party played
/// by the combat AI, a lost battle played again and won outright, a companion's death, and experience spent by the builds after a win
/// and at a trainer.
/// </summary>
public sealed class PlaythroughBattleTests(Na10Import fixture, ITestOutputHelper output)
{
    private Na10Import.Loaded Data => fixture.Data;

    private PluginSet Set => Data.Set;

    /// <summary>The game at a story checkpoint, made into a save and loaded as the runtime loads one.</summary>
    private GameState AtCheckpoint(string label)
    {
        var name = TestRoms.Checkpoints().Select(row => row.Data).FirstOrDefault(n => n.Length > 0 && n[..^4].EndsWith($" - {label}", StringComparison.Ordinal))
            ?? "";
        var game = GameState.Load(Set, CheckpointSave.Convert(TestRoms.Checkpoint(name), Set, label));
        Assert.Empty(game.Problems);
        return game;
    }

    /// <summary>A host on the game, played to trek with the player at the controls.</summary>
    private static PlaythroughHost Host(GameState game, IBattlePolicy battles, Levelling? levelling = null, GameStart? start = null)
    {
        var host = PlaythroughHost.From(game, start: start, opening: false);
        host.Battles = battles;
        if (levelling is not null)
            host.Menus = levelling;
        // Spending a checkpoint's hoard of experience keeps the window open a while.
        host.MenuPatience = 60 * 60 * 10;
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        return host;
    }

    /// <summary>The encounter at a map object, as trek fights one used where the player stands (<c>TrekLoop.Battle</c>).</summary>
    private BattleSetup Encounter(PlaythroughHost host, string id)
    {
        var (objectId, _, party) = Data.Of<PartyObject>().Single(p => p.Id == RecordId.Parse(id));
        var (place, terrain) = host.Session.Trek.Here();
        return BattleSetup.WithParty(BattleSource.Encounter, objectId, party, place, terrain, ambush: false);
    }

    /// <summary>Cues a battle and plays until it's over and trek has the player again: the last battle fought.</summary>
    private static BattleFought Fight(PlaythroughHost host, BattleSetup setup, long most = 200_000)
    {
        var before = host.BattlesFought.Count;
        host.Session.CueBattle(setup);
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count > before && h.BattlesFought[^1].Ended is not null && h.PlayerHasControl, most));
        return host.BattlesFought[^1];
    }

    private void Report(PlaythroughHost host, BattlePilot? pilot = null, Levelling? levelling = null)
    {
        output.WriteLine(host.Clock.ToString());
        foreach (var b in host.BattlesFought)
            output.WriteLine(b.ToString());
        foreach (var n in host.Notes)
            output.WriteLine(n.ToString());
        foreach (var p in pilot?.Problems ?? [])
            output.WriteLine($"pilot: {p}");
        foreach (var p in levelling?.Bought ?? [])
            output.WriteLine($"bought: {p}");
        foreach (var r in levelling?.Refused ?? [])
            output.WriteLine($"refused: {r}");
        foreach (var m in host.Game.Party.Members)
            output.WriteLine($"{m.Name}: level {m.Level}, {m.Hp}/{m.MaxHp} hp, {m.SpendableExp} XP to spend");
    }

    [Fact]
    public void TheAiPlayedPartyWinsAMidGameEncounterWithoutForcing()
    {
        // Before Rooughah: Alaron, Rheda, Brenna and Sholeh, every one's turns the AI's, against the four Chaos Scouts of Overworld O17
        // (object 4), whose win sets flag 3576. All four come through.
        var pilot = new BattlePilot();
        var host = Host(AtCheckpoint("Before Rooughah"), pilot);
        var flag = Flag(3576);
        Assert.False(host.Game.Flags.IsSet(flag));
        var fought = Fight(host, Encounter(host, "aidyn:mapobject/0/O17/4"));
        Report(host, pilot);
        Assert.Equal((BattleOutcome.Won, false, false), (fought.Outcome, fought.WonOutright, fought.LostFirst));
        Assert.Single(host.BattlesFought);
        Assert.Empty(fought.Deaths);
        Assert.True(host.Game.Flags.IsSet(flag));
        Assert.Equal(4, host.Game.Party.Count);
        Assert.Empty(pilot.Problems);
        Assert.Empty(host.Warnings);
    }

    /// <summary>A every ten ticks until a conversation has turned a page, then nothing: talking to whoever the player faces.</summary>
    private sealed class TalkOnce : IInputPolicy
    {
        public TickInput Next(PlaythroughHost host) =>
            host.Pages.Count == 0 && host.Clock.Ticks % 10 == 0 ? new TickInput(default, [MenuAction.Accept]) : TickInput.None;
    }

    [Fact]
    public void ABattleThePartyLosesIsPlayedAgainFromItsStartAndWonOutrightSettingItsFlagOnce()
    {
        // A new game's Alaron alone, at Overworld I08 where it starts, against the Cyclopes of O16 (object 3), whose win sets flag 1238:
        // the AI plays him, and he falls. The game over doesn't come: the game starts again from where he stood as the battle was cued,
        // the battle is cued again and won on his first turn, through the game's own victory, so its flag is set, the gold and
        // experience come, the post-battle windows open, and trek comes back where the battle began, all once.
        var pilot = new BattlePilot();
        var host = PlaythroughHost.NewGame(Set);
        host.Battles = pilot;
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        var flag = Flag(1238);
        var setup = Encounter(host, "aidyn:mapobject/0/O16/3");
        Assert.Equal(flag, setup.EncounterFlag);
        Assert.False(host.Game.Flags.IsSet(flag));
        var gold = host.Game.Party.Gold;
        var exp = host.Game.Party.Slots[0]!.TotalExp;
        var place = host.Session.Where().Place;
        var transitions = host.Transitions.Count;
        host.Session.CueBattle(setup);
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count == 2 && h.BattlesFought[^1].Ended is not null && h.PlayerHasControl, 100_000));
        Report(host, pilot);

        var (lost, won) = (host.BattlesFought[0], host.BattlesFought[1]);
        Assert.Equal((BattleOutcome.Lost, false, false), (lost.Outcome, lost.WonOutright, lost.LostFirst));
        Assert.Equal([Entity(0x023F)], lost.Deaths);
        Assert.Equal((BattleOutcome.Won, true, true), (won.Outcome, won.WonOutright, won.LostFirst));
        Assert.Same(lost.Setup, won.Setup);
        Assert.Empty(pilot.Problems);
        Assert.Equal(HostEnd.None, host.End);

        // The loss went as far as its game over's cut-scene starting; then the game started again, and the replay went through the
        // game's own win once: one post-battle window, its gold and experience, the flag, and trek where the battle began.
        var since = host.Transitions.Skip(transitions).Select(t => t.Transition).ToList();
        Assert.Equal([typeof(Transition.StartedBattle), typeof(Transition.EndBattle), typeof(Transition.StartCutScene), typeof(Transition.StartedBattle),
            typeof(Transition.EndBattle), typeof(Transition.PostBattle), typeof(Transition.ResumeTrek)], since.Select(t => t.GetType()));
        var spoils = since.OfType<Transition.PostBattle>().Single().Spoils;
        Assert.True(host.Game.Flags.IsSet(flag));
        Assert.Equal(gold + (uint)spoils.Gold, host.Game.Party.Gold);
        Assert.True(host.Game.Party.Slots[0]!.TotalExp > exp);
        Assert.Equal((1, false), (host.Game.Party.Count, host.Game.Party.Slots[0]!.IsDead));
        Assert.Equal(place, host.Session.Where().Place);
        Assert.Empty(host.Warnings);
    }

    [Fact]
    public void ALostBattleWhoseEnemiesActFirstIsWonOnTheirFirstTurnWhenPlayedAgain()
    {
        // The dryads' four at Overworld H12/1, whom the male dryad's 0x03FA sets on the party, a fight the story means to be lost: they
        // take the first turns and their Lightning kills the party before it has one. So the replay isn't won on the party's first turn,
        // which never comes, but on the first turn anyone takes, theirs, through the game's own victory, and the game doesn't end.
        var pilot = new BattlePilot();
        var host = PlaythroughHost.NewGame(Set);
        host.Battles = pilot;
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        var setup = Encounter(host, "aidyn:mapobject/0/H12/1");
        host.Session.CueBattle(setup);
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count == 2 && h.BattlesFought[^1].Ended is not null && h.PlayerHasControl, 100_000));
        Report(host, pilot);
        var (lost, won) = (host.BattlesFought[0], host.BattlesFought[1]);
        Assert.Equal(BattleOutcome.Lost, lost.Outcome);
        Assert.Equal((BattleOutcome.Won, true, true), (won.Outcome, won.WonOutright, won.LostFirst));
        Assert.Empty(pilot.Problems);
        Assert.Equal(HostEnd.None, host.End);
        if (setup.EncounterFlag is { } flag)
            Assert.True(host.Game.Flags.IsSet(flag));
    }

    [Fact]
    public void ABattleNeitherSideCanEndIsWonOutrightAsAStalemate()
    {
        // A new game's Alaron alone talks to the Ogre in Overworld B06 (object 5), whose script fights him in arena E03. Each AI's way to
        // the other is the original's greedy one (ArenaGrid.FindPath), which a pocket stops: neither finds one, and both rest turn after
        // turn. With no hit for three minutes, the battle is won outright, said as a stalemate, its flag (3758) set as a win's.
        var overworld = RecordId.Decimal(RecordTypes.MapSet, 0);
        var (id, map, npc) = Data.Of<NpcObject>().Single(n => n.Map.MapSet == overworld && n.Map.Name == "B06" && n.Object.Index == 5);
        var flag = npc.BattleFlag!.Value;
        Assert.Equal(Flag(3758), flag);
        var start = new GameStart(Set.Get<MapSetRecord>(map.MapSet), map.Name, npc.Position + new Vector3(0, 2, 1.2f), new Vector2(0, -1), SetDown: true);
        var pilot = new BattlePilot();
        var host = Host(GameState.NewGame(Set), pilot, start: start);
        host.Input = new TalkOnce();
        Assert.False(host.Game.Flags.IsSet(flag));
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count == 1 && h.BattlesFought[^1].Ended is not null && h.PlayerHasControl, 100_000));
        Report(host, pilot);
        var fought = Assert.Single(host.BattlesFought);
        Assert.Equal((id, BattleOutcome.Won, true, true, false), (fought.Setup.Party!.Value, fought.Outcome, fought.WonOutright, fought.Stalemate, fought.LostFirst));
        Assert.Empty(fought.Deaths);
        Assert.True(host.Game.Flags.IsSet(flag));
        Assert.Single(pilot.Problems);
    }

    [Fact]
    public void ACompanionWhoDiesLeavesTheParty()
    {
        // Before Rooughah, the AI-played party wins against Overworld O16's Skeleton Archers and Skeletons (object 6), but a companion
        // dies: they leave the party as the battle ends, as in the original (Party.AfterBattle), their died flag set and their joined
        // flag kept, so the run sees a slot to fill. Without RewindDeaths the battle isn't played again: only Alaron's death or the whole
        // party's loses it.
        var pilot = new BattlePilot { RewindDeaths = false };
        var host = Host(AtCheckpoint("Before Rooughah"), pilot);
        Assert.False(host.Game.Party.HasEmptySlot());
        var fought = Fight(host, Encounter(host, "aidyn:mapobject/0/O16/6"));
        Report(host, pilot);
        Assert.Equal((BattleOutcome.Won, false, false), (fought.Outcome, fought.WonOutright, fought.LostFirst));
        var died = Assert.Single(fought.Deaths);
        Assert.NotEqual(Entity(0x023F), died);
        Assert.Equal(3, host.Game.Party.Count);
        Assert.False(host.Game.Party.Contains(died));
        Assert.True(host.Game.Party.HasEmptySlot());
        var flags = Set.Get<EntityRecord>(died).Member!;
        Assert.True(host.Game.Flags.IsSet(flags.Died));
        Assert.True(host.Game.Flags.IsSet(flags.Joined));
        if (flags.Left is { } left)
            Assert.False(host.Game.Flags.IsSet(left));
        Assert.Empty(pilot.Problems);
    }

    [Fact]
    public void ABattleWonWithACompanionDeadIsPlayedAgainAndWonOutright()
    {
        // The same battle, with RewindDeaths as the run has it: the win with a companion dead is undone, as a player reloads, and the
        // battle played again from its start and won outright. Nobody died that stands, and the party is whole.
        var pilot = new BattlePilot();
        var host = Host(AtCheckpoint("Before Rooughah"), pilot);
        var before = host.BattlesFought.Count;
        host.Session.CueBattle(Encounter(host, "aidyn:mapobject/0/O16/6"));
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count > before + 1 && h.BattlesFought[^1].Ended is not null && h.PlayerHasControl, 300_000));
        Report(host, pilot);
        var (first, replay) = (host.BattlesFought[^2], host.BattlesFought[^1]);
        Assert.True(first.Undone);
        Assert.Single(first.Deaths);
        Assert.Equal((BattleOutcome.Won, true, true, false), (replay.Outcome, replay.WonOutright, replay.DiedFirst, replay.LostFirst));
        Assert.Empty(replay.Deaths);
        Assert.Equal(4, host.Game.Party.Count);
        Assert.All(host.Game.Party.Members, m => Assert.False(Set.Get<EntityRecord>(m.Entity).Member is { } f && host.Game.Flags.IsSet(f.Died)));
        Assert.Empty(pilot.Problems);
    }

    [Fact]
    public void BetterGearInTheInventoryIsPutOnThroughThePartyTab()
    {
        // Before Shamsuk's Tower Alaron wears a Short Sword, Enchanted Hide and a Small Shield, with better in the party's inventory: the
        // Party tab's presses put on him what gains him most of each kind, each for more than what it replaced, which goes back in the
        // inventory. The game's own equip does it.
        var game = AtCheckpoint("Before Shamsuk's Tower");
        var alaron = game.Party.Members.Single(m => m.Entity == Entity(0x023F));
        var before = (alaron.Equipment.Weapon!.Item, alaron.Equipment.Armor!.Item, alaron.Equipment.Shield!.Item);
        var outfitting = new Outfitting();
        var host = Host(game, WinOutright.Instance, new Levelling(outfitting));
        Assert.True(outfitting.TryDress(host));
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.PlayerHasControl && h.Session.Mode == GameMode.Trek, 20_000));
        foreach (var worn in outfitting.Worn)
            output.WriteLine(worn.ToString());
        Assert.All(outfitting.Worn, w => Assert.True(w.Gain > 0));
        Assert.True(Outfitting.Worth(alaron, alaron.Equipment.Weapon!.Item) > Outfitting.Worth(alaron, before.Item1));
        Assert.True(Outfitting.Worth(alaron, alaron.Equipment.Armor!.Item) > Outfitting.Worth(alaron, before.Item2));
        Assert.True(Outfitting.Worth(alaron, alaron.Equipment.Shield!.Item) > Outfitting.Worth(alaron, before.Item3));
        Assert.NotNull(game.Party.Inventory.Find(before.Item1.Id));
        Assert.NotNull(game.Party.Inventory.Find(before.Item2.Id));
        // Nothing better is left: it doesn't open the tab again.
        Assert.Null(outfitting.Best(game.Party));
    }

    [Fact]
    public void AfterAWinEachMembersExperienceIsSpentByTheirBuildThroughThePostBattleWindow()
    {
        // Before Shamsuk's Tower the party carries some 440,000 experience each, unspent. The AI-played party beats three Goblins (Overworld
        // B06, object 6), and the post-battle window spends it by each member's build, pressing as a player does: only what the window
        // lists (attributes, skills above 0, spells known), at the prices it asks, in experience alone.
        var levelling = new Levelling();
        var pilot = new BattlePilot();
        var host = Host(AtCheckpoint("Before Shamsuk's Tower"), pilot, levelling);
        var party = host.Game.Party;
        var gold = party.Gold;
        var spent = party.Members.ToDictionary(m => m.Entity, m => m.SpendableExp);
        host.Session.CueBattle(Encounter(host, "aidyn:mapobject/0/B06/6"));
        // What each member has as the window opens: the battle has given its experience, and a blow may have raised a weapon skill
        // (Combatant.GainSkillExperience).
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.Session.Menu is { IsOpen: true, Window: PostBattleMenu }, 100_000));
        var before = party.Members.ToDictionary(m => m.Entity, m => (Exp: m.SpendableExp,
            Stats: Enum.GetValues<Stat>().ToDictionary(s => s, m.Stats.Trained), Skills: SkillId.All.ToDictionary(s => s, m.Skills.Base),
            Spells: m.Spells.ToDictionary(s => s.Spell.Id, s => s.Level)));
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.PlayerHasControl, 100_000));
        Report(host, pilot, levelling);
        var fought = Assert.Single(host.BattlesFought);
        Assert.Equal((BattleOutcome.Won, false), (fought.Outcome, fought.WonOutright));
        var spoils = host.Transitions.Select(t => t.Transition).OfType<Transition.PostBattle>().Single().Spoils;
        // The award, the pool × 1.5, to each living member.
        Assert.All(party.Members, m => Assert.Equal(spent[m.Entity] + (uint)(spoils.Experience * 1.5f), before[m.Entity].Exp));
        Assert.Empty(levelling.Refused);
        Assert.NotEmpty(levelling.Bought);
        Assert.All(levelling.Bought, p => Assert.Null(p.Trainer));
        // Gold isn't spent after a battle.
        Assert.Equal(gold + (uint)spoils.Gold, party.Gold);
        Assert.Equal(["Mage", "Mage", "Rogue", "Mage"], party.Members.Select(m => Builds.For(m, Builds.Default)!.Name));

        foreach (var member in party.Members)
        {
            var was = before[member.Entity];
            var bought = levelling.Bought.Where(p => p.Member == member.Entity).ToList();
            Assert.NotEmpty(bought);
            // What it cost came out of what was there and what the battle gave, and no more.
            Assert.Equal(was.Exp - (uint)bought.Sum(p => p.Pick.Quote.Exp), member.SpendableExp);
            foreach (var pick in bought.Select(p => p.Pick))
            {
                Assert.NotEqual(Stat.Level, pick.Stat);
                if (pick.Skill is { } skill)
                    Assert.True(was.Skills[skill] > 0, $"{member.Name}'s {skill} was at 0, which only a trainer starts.");
                if (pick.Spell is { } spell)
                    Assert.True(was.Spells.ContainsKey(spell.Id), $"{member.Name} learnt {spell.Name}, which only a trainer teaches.");
            }
            // Each is raised a level at a time from where it stood.
            foreach (var g in bought.GroupBy(p => (p.Pick.Stat, p.Pick.Skill, p.Pick.Spell?.Id)))
                Assert.Equal(Enumerable.Range(g.First().Pick.From, g.Count()), g.Select(p => p.Pick.From));
            foreach (var stat in Enum.GetValues<Stat>().Where(s => s != Stat.Level))
            {
                Assert.Equal(was.Stats[stat] + bought.Count(p => p.Pick.Stat == stat), member.Stats.Trained(stat));
                Assert.True(member.Stats.Trained(stat) <= member.Rules[stat].BaseCap);
            }
            foreach (var skill in SkillId.All)
                Assert.True(was.Skills[skill] + bought.Count(p => p.Pick.Skill == skill) == member.Skills.Base(skill),
                    $"{member.Name}'s {skill}: {was.Skills[skill]} + {bought.Count(p => p.Pick.Skill == skill)} bought, now {member.Skills.Base(skill)}");
            Assert.Equal(was.Spells.Count, member.Spells.Count);
            // Nothing more the build wants could be bought with what's left: what it waits for costs more.
            Assert.Null(Builds.Next(member, Builds.Default, null, 0));
        }
        Assert.Empty(pilot.Problems);
        Assert.Empty(host.Warnings);
    }

    [Fact]
    public void AtARealTrainerTheBuildsBuyWhatTheyWantWithinTheGold()
    {
        // Before Rooughah, in Terminor: the party at two of Terminor's trainers, the fighters' (STTE02, entity 0x02B5, in A07: Warrior 8,
        // Missile 6, Sword 10) and the Elemental mage (STTE07, 0x02BA, in E01: Wizard 9 and three spells), with the party's gold cut to
        // 1,500, by the training window's own presses. Each pick is the trainer's to teach, below the trainer's level, at the experience
        // and gold the window asks; the gold runs out before the experience does.
        var levelling = new Levelling();
        var host = Host(AtCheckpoint("Before Rooughah, in Terminor"), new BattlePilot(), levelling);
        var party = host.Game.Party;
        party.Gold = 1500;
        var exp = party.Members.ToDictionary(m => m.Entity, m => m.SpendableExp);
        var bought = new List<Purchase>();
        Trainer? last = null;
        foreach (var entity in new[] { Entity(0x02B5), Entity(0x02BA) })
        {
            // 1,500 at each: either alone would spend it all.
            party.Gold = 1500;
            var trainer = last = Trainer.From(Set.Get<EntityRecord>(entity), host.Game.Rules, Set.Get<SpellRecord>);
            var goldBefore = party.Gold;
            var here = levelling.Train(host, trainer);
            output.WriteLine($"{entity}: {goldBefore} gold, then {party.Gold}");
            foreach (var p in here)
                output.WriteLine($"  {p}");
            Assert.True(host.PlayerHasControl);
            Assert.All(here, p => Assert.Equal(entity, p.Trainer));
            Assert.Equal(goldBefore - (uint)here.Sum(p => p.Pick.Quote.Gold), party.Gold);
            foreach (var pick in here.Select(p => p.Pick))
            {
                if (pick.Skill is { } skill)
                    Assert.True(pick.From < trainer.Skills.Base(skill), $"{skill} from {pick.From} at a trainer of {trainer.Skills.Base(skill)}.");
                else
                    Assert.Contains(trainer.Spells, s => s.Spell.Id == pick.Spell!.Id && pick.From < s.Level);
                Assert.Null(pick.Stat);
                Assert.True(pick.Quote.Gold > 0);
            }
            bought.AddRange(here);
        }
        foreach (var r in levelling.Refused)
            output.WriteLine($"refused: {r}");
        Assert.Empty(levelling.Refused);
        Assert.Contains(bought, p => p.Trainer == Entity(0x02B5));
        Assert.Contains(bought, p => p.Trainer == Entity(0x02BA));
        foreach (var member in party.Members)
            Assert.Equal(exp[member.Entity] - (uint)bought.Where(p => p.Member == member.Entity).Sum(p => p.Pick.Quote.Exp), member.SpendableExp);
        // What's left wouldn't pay for anything more the builds want at the last trainer: the gold ran out.
        Assert.All(party.Members, m => Assert.Null(Builds.Next(m, Builds.Default, last, party.Gold)));
        Assert.Contains(party.Members, m => Builds.Next(m, Builds.Default, last, 1_000_000) is not null);
        Assert.Empty(host.Warnings);
    }
}
