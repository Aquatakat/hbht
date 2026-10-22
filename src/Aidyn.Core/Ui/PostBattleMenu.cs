using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The post-battle window (pause state 5, game mode 6: <c>DollMenu::InitMenu</c> at <c>0x800300B0</c> with
/// <c>PartyMenu.ListsCreate</c> at <c>0x80030C50</c>): the Party tab's header and the member's spells, skills and stats, starting
/// on the stats, with no silhouette and no trainer; over the silhouette the details' parchment with Train's title, what each
/// member earned and who gained a level, and under it what the cursor's row costs and the experience the member has to spend.
/// Checked in checkpoint 13's widgets.
/// </summary>
/// <remarks>
/// <para>
/// The stick's up and down move the list's cursor, its left and right change the list among the three, and C-left and C-right
/// the member. <b>A</b> asks to raise the row a level with experience alone (<c>PartyMenu.ListsAccept</c>, <c>0x80031A30</c>, the
/// list's Confirm with no trainer): an attribute, a skill above 0 or a spell already known (<see cref="Training"/>). A refusal
/// says why, and otherwise a question whose Yes trains it. <b>B</b> or Start closes the window, and the loot window follows
/// (pause state 6). C-down shows the row's details.
/// </para>
/// <para>
/// The level lines are made once, as the window is built: the award has already raised the levels
/// (<see cref="BattleSpoils.Levelled"/>), and the window only says so. Changing the member leaves them be.
/// </para>
/// </remarks>
public sealed class PostBattleMenu : IPartyWindow
{
    /// <summary>"Each member earned %ld XP." and "%s gained a level!" (common lines 491 and 492).</summary>
    public const int EarnedLine = 491, LevelLine = 492;

    /// <summary>The stats' refusals and question (common lines 510–512): at most, too little experience, and the price.</summary>
    public const int StatAtMost = 510, StatNeeds = 511, TrainStatFor = 512;

    /// <summary>Where line 491 starts, and the width it wraps to (from x 20 to 150).</summary>
    public static readonly (int X, int Y, int Wrap) EarnedPlace = (20, 92, 130);

    /// <summary>
    /// Where the level lines start across, and how wide they wrap (to x 155); the first is 4 under line 491's bottom as the main
    /// font wraps it, as the original measures it while the main font is set, though every line here is drawn in the slim one.
    /// </summary>
    public static readonly (int X, int Wrap, int Gap) LevelPlace = (20, 135, 4);

    /// <summary>The cost lines' places: "XP Cost" and "XP Remaining", 11 apart (<c>PartyMenu.CostLines</c>, <c>0x800317E0</c>).</summary>
    public static readonly (int X, int Y, int Spacing) CostPlace = (20, 186, 11);

    /// <summary>The three lists, in the order the stick steps through them (spells, skills, stats, as they're made).</summary>
    public static readonly IReadOnlyList<PartyList> Lists = [PartyList.Spells, PartyList.Skills, PartyList.Stats];

    private PostBattleMenu(PartyMenu party, BattleSpoils spoils)
    {
        Party = party;
        Spoils = spoils;
        var text = party.Text;
        Earned = text.Format(EarnedLine, (int)(spoils.Experience * Award(party)));
        Levelled = [.. spoils.Levelled.Select(m => text[LevelLine].Replace("%s", m.Name, StringComparison.Ordinal))];
        Party.ShowList(PartyList.Stats);
    }

    /// <summary>The post-battle window on the party, after the battle that gave <paramref name="spoils"/>.</summary>
    /// <param name="random">The stream skill checks draw from; a fresh one if none.</param>
    public static PostBattleMenu For(PluginSet plugins, Party party, BattleSpoils spoils, IRandom? random = null) =>
        new(PartyMenu.From(plugins, party, random: random), spoils);

    /// <summary>The award's multiplier the line shows the pool times (<c>Entity::AddExp</c>'s 1.5).</summary>
    private static float Award(PartyMenu party) => party.Shown?.Rules.Progression.ExpAward ?? 1.5f;

    /// <summary>The header and the member's lists.</summary>
    public PartyMenu Party { get; }

    PartyMenu IPartyWindow.Menu => Party;

    bool IPartyWindow.ChangesMember => true;

    /// <summary>What the battle gave.</summary>
    public BattleSpoils Spoils { get; }

    /// <summary>"Each member earned n XP.", n the pool times the award's multiplier.</summary>
    public string Earned { get; }

    /// <summary>"X gained a level!" for each member whose level rose, in the party's order.</summary>
    public IReadOnlyList<string> Levelled { get; }

    /// <summary>The title over the lines (<c>BORG8_TitleTrain</c>).</summary>
    public RecordId Title => MenuImages.TrainTitle;

    /// <summary>The popups over the window: the refusal and the question, and C-down's details.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>A refusal, over everything, which A, B or C-down closes.</summary>
    public MenuMessage? Message => Overlay.Message;

    /// <summary>The question that trains the row, Yes or No.</summary>
    public MenuChoice? Choice => Overlay.Choice;

    public bool HasPopup => Overlay.HasPopup || Party.HasPopup;

    public DetailsPane DetailsPane => Overlay.Details;

    /// <summary>The colour the popups' cursors pulse to now.</summary>
    public Rgba PopupHighlight => Overlay.Highlight;

    /// <summary>
    /// The two lines under the parchment (<c>PartyMenu.CostLines</c>): what raising the cursor's row costs, or MAX, blank with no row;
    /// and the experience the member shown has to spend.
    /// </summary>
    public IReadOnlyList<string> CostLines
    {
        get
        {
            var text = Party.Text;
            var remaining = text.Format(MenuText.XpRemainingLine, Party.Shown?.SpendableExp ?? 0);
            if (Party.Shown is not { } member || RowCost(member) is not { } cost)
                return [" ", remaining];
            return [cost is { } e and >= 0 ? text.Format(MenuText.XpCost, e) : text[MenuText.XpCostMax], remaining];
        }
    }

    public MenuDetails? Details() => Party.Details();

    public void PressDetails() => Overlay.PressDetails(!HasPopup && Details() is not null);

    public void ReleaseDetails() => Overlay.ReleaseDetails();

    public void Step()
    {
        Party.Step();
        Overlay.Step();
    }

    /// <summary>Moves the choice's cursor, or the list's.</summary>
    public void MoveCursor(int by)
    {
        if (Overlay.MoveCursor(by) || HasPopup)
            return;
        Party.MoveCursor(by);
    }

    /// <summary>The stick's left and right: the next list or the one before, of the three (<c>PartyMenu.ListsLeft</c>, <c>ListsRight</c>).</summary>
    public void Sideways(int direction)
    {
        if (HasPopup)
            return;
        Party.ChangeList(-direction, Lists);
    }

    public void ChangeMember(int by)
    {
        if (HasPopup)
            return;
        Party.ChangeMember(by);
    }

    /// <summary>A: closes a message, chooses Yes or No, or asks to raise the row.</summary>
    public void Accept()
    {
        if (Overlay.Dismiss())
            return;
        if (Choice is { } choice)
            Choose(choice);
        else if (!DetailsPane.Showing)
            Ask();
    }

    /// <summary>B, or Start: closes a message. False if nothing holds the window open, so it closes; the question has no B.</summary>
    public bool Back()
    {
        if (Overlay.Dismiss())
            return true;
        return Choice is not null || DetailsPane.Showing;
    }

    public bool CloseMessage() => Overlay.CloseMessage();

    /// <summary>The cursor's row's price in experience, −1 for MAX, or null with no row.</summary>
    private long? RowCost(Character member)
    {
        switch (Party.List)
        {
            case PartyList.Stats when Party.Stats.Current is { } row:
                return member.Stats.IsCapped(row.Stat) || member.Stats.TrainingCost(row.Stat) is not { } cost ? -1 : cost;
            case PartyList.Skills when Party.Skills.Current is { } row:
                return member.Skills.RawBase(row.Skill) >= member.Rules[row.Skill].BaseCap ? -1 : member.Skills.TrainingCost(row.Skill);
            case PartyList.Spells when Party.Spells.Current is { } row:
                var level = row.Spell.Level;
                return level >= member.Rules.Training.SpellMaxLevel ? -1 : CharacterRules.TrainingCost(level, row.Spell.Spell.TrainingCost);
            default:
                return null;
        }
    }

    // The list's Confirm with no trainer: the refusal, or the question with the price in experience.
    private void Ask()
    {
        if (Party.Shown is not { } member)
            return;
        var text = Party.Text;
        var gold = Party.Party.Gold;
        TrainingQuote quote;
        int ask;
        Func<TrainingRefusal, int> refusal;
        switch (Party.List)
        {
            case PartyList.Stats when Party.Stats.Current is { } row:
                quote = Training.QuoteStat(member, row.Stat);
                ask = TrainStatFor;
                refusal = r => r == TrainingRefusal.CantAfford ? StatNeeds : StatAtMost;
                break;
            case PartyList.Skills when Party.Skills.Current is { } row:
                quote = Training.QuoteSkill(member, row.Skill, null, gold);
                ask = MenuText.TrainSkillForXp;
                refusal = r => r switch
                {
                    TrainingRefusal.CantLearn => MenuText.SkillCantLearn,
                    TrainingRefusal.TrainerKnowsNoMore => MenuText.SkillCantTeach,
                    TrainingRefusal.AtMost => MenuText.SkillAtMost,
                    _ => MenuText.SkillNeedsXp,
                };
                break;
            case PartyList.Spells when Party.Spells.Current is { } row:
                quote = Training.QuoteSpell(member, row.Spell.Spell, null, gold);
                ask = MenuText.TrainSpellForXp;
                refusal = r => r switch
                {
                    TrainingRefusal.CantLearn => MenuText.OnlyWizardsTrain,
                    TrainingRefusal.TrainerKnowsNoMore => MenuText.SpellCantTeach,
                    TrainingRefusal.AtMost => MenuText.SpellAtMost,
                    TrainingRefusal.WizardTooLow => MenuText.SpellWizardTooLow,
                    TrainingRefusal.WrongSchool => MenuText.SpellWrongSchool,
                    _ => MenuText.SpellNeedsXp,
                };
                break;
            default:
                return;
        }
        if (quote.Allowed)
            Overlay.Ask(MenuChoice.Question(text.Format(ask, quote.Exp), [text[TrainingMenu.YesLine], text[TrainingMenu.NoLine]]));
        else
            Overlay.Say(text.Format(refusal(quote.Refusal), quote.Exp));
    }

    // Yes trains the row; either closes the question.
    private void Choose(MenuChoice choice)
    {
        Overlay.CloseChoice();
        if (choice.Choices.Cursor != 0 || Party.Shown is not { } member)
            return;
        var gold = Party.Party.Gold;
        switch (Party.List)
        {
            case PartyList.Stats when Party.Stats.Current is { } row:
                Training.TrainStat(member, row.Stat);
                break;
            case PartyList.Skills when Party.Skills.Current is { } row:
                Training.TrainSkill(member, row.Skill, null, ref gold);
                break;
            case PartyList.Spells when Party.Spells.Current is { } row:
                Training.TrainSpell(member, row.Spell.Spell, null, ref gold, out _);
                break;
        }
        Party.Refresh();
    }
}
