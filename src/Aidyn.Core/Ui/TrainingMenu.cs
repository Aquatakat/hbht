using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The pause menu's training state at a trainer (<c>WidgetDollMenu</c> with <c>WidgetMenuSpells</c>, pause
/// state 4), which a script's Train command opens: the Party tab's header and the member's list of skills
/// or spells, with no silhouette, beside the trainer's list of the same on the details' parchment, and
/// under it what the cursor's row would cost. Checked in 1.0's code (<c>WidgetMenuSpells</c> at
/// <c>0x80031c0c</c>).
/// </summary>
/// <remarks>
/// <para>
/// The trainer's list is what they teach: their skills, weapon skills and shield above level 0, at their
/// own levels, and their spells at their ranks (<c>init_skill_trainer</c>). Nothing is greyed out, not even
/// what the member can't learn. The stick's up and down move its cursor, and the member's list follows it
/// to the same skill or spell, or turns plain if the member hasn't it (<c>SetHighlight</c>). The stick's
/// left and right change both lists between skills and spells; C-left and C-right change the member.
/// </para>
/// <para>
/// <b>A</b> asks the member's list to train the row (<c>Confirm</c>): a refusal says why, and otherwise a
/// question with the prices, whose Yes trains it (<see cref="Training"/>). The first spell learnt shows
/// help. <b>B</b> or Start closes the window. <b>C-down</b> shows the row's details: the skill's
/// description, or the trainer's spell.
/// </para>
/// </remarks>
public sealed class TrainingMenu : IPartyWindow
{
    /// <summary>The trainer's list's place and bounds in the 320 × 240 frame, as the loot's pile's.</summary>
    public static readonly ScreenRect ClassicList = BarterMenu.ClassicPile;

    /// <summary>The trainer's list: <see cref="ClassicList"/> with its rows 13 apart, so six show.</summary>
    public static readonly ListLayout Layout = new(ClassicList, 13);

    /// <summary>The choice's Yes and No (common lines 31 and 32).</summary>
    public const int YesLine = 31, NoLine = 32;

    private readonly FlagNetwork? _flags;

    private TrainingMenu(PartyMenu party, Trainer trainer, FlagNetwork? flags)
    {
        Party = party;
        Trainer = trainer;
        _flags = flags;
        Skills = Layout.List<SkillRow>([.. trainer.SkillsOffered.Select(s => new SkillRow(s, party.Shown!.Rules[s].Name,
            s.IsSkill ? party.Shown.Rules[s].Icon : null, trainer.Skills.Base(s), trainer.Skills.Modified(s) - trainer.Skills.Base(s)))]);
        Spells = Layout.List<SpellRow>([.. trainer.Spells.Select(s => new SpellRow(new LearnedSpell(s.Spell, s.Level),
            MenuImages.AspectIcon(PartyMenuRows.SpellAspect(s.Spell)), MenuImages.SchoolIcon(s.Spell.School), s.Spell.Icon))]);
        Party.ShowList(PartyList.Skills);
        Follow();
    }

    /// <summary>The training window on the party, at a trainer (<c>init_skill_trainer</c>).</summary>
    /// <param name="flags">The flags the first spell learnt sets.</param>
    /// <param name="random">The stream skill checks draw from; a fresh one if none.</param>
    public static TrainingMenu For(PluginSet plugins, Party party, Trainer trainer, FlagNetwork? flags, IRandom? random = null) =>
        new(PartyMenu.From(plugins, party, random: random), trainer, flags);

    /// <summary>The header and the member's lists.</summary>
    public PartyMenu Party { get; }

    PartyMenu IPartyWindow.Menu => Party;

    bool IPartyWindow.ChangesMember => true;

    /// <summary>The stick's left and right both change the lists between skills and spells (<see cref="Switch"/>).</summary>
    void IPartyWindow.Sideways(int direction) => Switch();

    public Trainer Trainer { get; }

    /// <summary>
    /// The trainer's skills, at their levels (<c>WidgetSkillInfo</c> on the trainer's <c>CharSkills</c>): one above
    /// 10 reads as 10 with the rest as a difference, up to 15, as the original's modified level is the raw one.
    /// </summary>
    public MenuList<SkillRow> Skills { get; }

    /// <summary>The trainer's spells, at their ranks.</summary>
    public MenuList<SpellRow> Spells { get; }

    /// <summary>Whether both lists show spells, rather than skills.</summary>
    public bool OnSpells => Party.List == PartyList.Spells;

    /// <summary>The trainer's list's title (<c>BORG8_TitleTrain</c>).</summary>
    public RecordId Title => MenuImages.TrainTitle;

    /// <summary>
    /// Whether the member's list has the trainer's cursor row, so its own cursor is on it and pulses; if not,
    /// the member's list is all plain (<c>WidgetMenuSpells::SetHighlight</c>).
    /// </summary>
    public bool Followed { get; private set; }

    /// <summary>The popups over the window: the refusal, the question and the help, and C-down's details.</summary>
    public MenuOverlay Overlay { get; } = new();

    /// <summary>A refusal, over everything, which A, B or C-down closes.</summary>
    public MenuMessage? Message => Overlay.Message;

    /// <summary>The question that trains the row, Yes or No.</summary>
    public MenuChoice? Choice => Overlay.Choice;

    /// <summary>The help on the first spell learnt, on the parchment, which A or B closes (<c>WidgetMenuPrompt</c>).</summary>
    public string? Help => Overlay.Help;

    /// <summary>Whether a popup or the details are taking the presses.</summary>
    public bool HasPopup => Overlay.HasPopup;

    /// <summary>C-down's details of the active row, which fade as the Party tab's do (<see cref="PopupFade"/>).</summary>
    public DetailsPane DetailsPane => Overlay.Details;

    /// <summary>C-down pressed: shows the details, unless a popup is up or they show already.</summary>
    public void PressDetails() => Overlay.PressDetails(!HasPopup && Details() is not null);

    /// <summary>C-down let go: the details fade out once they're open.</summary>
    public void ReleaseDetails() => Overlay.ReleaseDetails();

    /// <summary>The colour the popups' cursors pulse to now.</summary>
    public Rgba PopupHighlight => Overlay.Highlight;

    /// <summary>
    /// The three lines under the trainer's list (<c>GetPrices</c>, <c>0x80032088</c>): what the member's own
    /// list says the trainer's row costs in experience, what the member has to spend, and the gold. The
    /// costs are blank with no row, and the gold at a trainer who charges none.
    /// </summary>
    /// <remarks>
    /// The prices are the member's whatever the trainer knows: a skill or a spell they know at level n costs
    /// (n + 1)² × its cost at 80%, or MAX from level 10, and (n + 1) × 40 gold even then, or 200 from level 0.
    /// A spell they don't know costs its own cost at 80% and 200 gold. A skill they can never learn reads
    /// as level −1, so it costs 0 and 0.
    /// </remarks>
    public IReadOnlyList<string> PriceLines
    {
        get
        {
            var text = Party.Text;
            var remaining = text.Format(MenuText.XpRemainingLine, Party.Shown?.SpendableExp ?? 0);
            if (Party.Shown is not { } student || ShownCost(student) is not { } cost)
                return [" ", remaining, " "];
            var exp = cost.Exp is { } e ? text.Format(MenuText.XpCost, e) : text[MenuText.XpCostMax];
            return [exp, remaining, Trainer.Free ? " " : text.Format(MenuText.GoldCost, cost.Gold)];
        }
    }

    /// <summary>Changes both lists between skills and spells (the stick's left and right).</summary>
    public void Switch()
    {
        if (HasPopup)
            return;
        Party.ShowList(OnSpells ? PartyList.Skills : PartyList.Spells);
        Follow();
    }

    /// <summary>Shows the next member, or with a negative step the one before (C-right and C-left).</summary>
    public void ChangeMember(int by)
    {
        if (HasPopup)
            return;
        Party.ChangeMember(by);
        Follow();
    }

    /// <summary>Moves the cursor of the choice, or of the trainer's list, which the member's follows.</summary>
    public void MoveCursor(int by)
    {
        if (Overlay.MoveCursor(by) || HasPopup)
            return;
        if (OnSpells)
            Spells.Move(by);
        else
            Skills.Move(by);
        Follow();
    }

    /// <summary>Puts the trainer's cursor on a row, as hovering does.</summary>
    public void Point(int row)
    {
        if (HasPopup)
            return;
        if (OnSpells)
            Spells.Point(row);
        else
            Skills.Point(row);
        Follow();
    }

    /// <summary>The trainer's cursor row's skill, if the skills show and it has one.</summary>
    public SkillId? CurrentSkill => !OnSpells && Skills.Current is { } row ? row.Skill : null;

    /// <summary>The trainer's cursor row's spell, if the spells show and it has one.</summary>
    public SpellRow? CurrentSpell => OnSpells ? Spells.Current : null;

    /// <summary>What C-down shows: the skill's description, or the trainer's spell (<c>WidgetMenuSpells</c> at <c>0x800327fc</c>).</summary>
    public MenuDetails? Details()
    {
        if (CurrentSkill is { } skill && Party.Shown is { } shown)
            return MenuDetailsBuilder.Skill(shown.Rules[skill]);
        if (CurrentSpell is { } spell)
            return MenuDetailsBuilder.Spell(spell.Spell.Spell, Party.Text, Party.Party.Inventory.Catalog);
        return null;
    }

    /// <summary>Steps the cursors' pulses and the details' fade by a tick.</summary>
    public void Step()
    {
        Party.Step();
        Overlay.Step();
    }

    /// <summary>A: closes a message or the help, chooses Yes or No, or asks to train the trainer's row.</summary>
    public void Accept()
    {
        if (Overlay.Dismiss())
            return;
        if (Choice is { } choice)
            Choose(choice);
        else if (!DetailsPane.Showing)
            Ask();
    }

    /// <summary>B, or Start: closes a message or the help. False if nothing holds the window open, so it closes; the question has no B.</summary>
    public bool Back()
    {
        if (Overlay.Dismiss())
            return true;
        return Choice is not null || DetailsPane.Showing;
    }

    /// <summary>C-down: closes a message. True if it did, so C-down doesn't show the details too.</summary>
    public bool CloseMessage() => Overlay.CloseMessage();

    // WidgetMenuSpells::AFunc: the member's list's Confirm, for the trainer's row.
    private void Ask()
    {
        if (Party.Shown is not { } student)
            return;
        TrainingQuote quote;
        var gold = Party.Party.Gold;
        if (CurrentSkill is { } skill)
            quote = Training.QuoteSkill(student, skill, Trainer, gold);
        else if (CurrentSpell is { } spell)
            quote = Training.QuoteSpell(student, spell.Spell.Spell, Trainer, gold);
        else
            return;
        var text = Party.Text;
        var charges = quote.Gold != 0;
        int line;
        if (quote.Allowed)
        {
            line = OnSpells ? charges ? MenuText.TrainSpellFor : MenuText.TrainSpellForXp : charges ? MenuText.TrainSkillFor : MenuText.TrainSkillForXp;
            Overlay.Ask(MenuChoice.Question(text.Format(line, quote.Exp, quote.Gold), [text[YesLine], text[NoLine]]));
            return;
        }
        line = (quote.Refusal, OnSpells) switch
        {
            (TrainingRefusal.CantLearn, false) => MenuText.SkillCantLearn,
            (TrainingRefusal.TrainerKnowsNoMore, false) => MenuText.SkillCantTeach,
            (TrainingRefusal.AtMost, false) => MenuText.SkillAtMost,
            (TrainingRefusal.CantAfford, false) => charges ? MenuText.SkillNeeds : MenuText.SkillNeedsXp,
            (TrainingRefusal.CantLearn, true) => MenuText.OnlyWizardsTrain,
            (TrainingRefusal.TrainerKnowsNoMore, true) => MenuText.SpellCantTeach,
            (TrainingRefusal.AtMost, true) => MenuText.SpellAtMost,
            (TrainingRefusal.WizardTooLow, true) => MenuText.SpellWizardTooLow,
            (TrainingRefusal.WrongSchool, true) => MenuText.SpellWrongSchool,
            (TrainingRefusal.CantAfford, true) => charges ? MenuText.SpellNeeds : MenuText.SpellNeedsXp,
            _ => MenuText.SkillCantLearn,
        };
        Overlay.Say(text.Format(line, quote.Exp, quote.Gold));
    }

    // The member's list's GetExpPrice and GetGoldPrice for the trainer's row: null with no row, and no
    // experience price at MAX.
    private (uint? Exp, int Gold)? ShownCost(Character student)
    {
        var rules = student.Rules;
        if (CurrentSkill is { } skill)
        {
            var level = student.Skills.RawBase(skill);
            var gold = level == 0 ? rules.Training.FirstLevelGold : (level + 1) * rules.Training.GoldPerLevel;
            return (level >= rules[skill].BaseCap ? null : Discount(student.Skills.TrainingCost(skill), rules), gold);
        }
        if (CurrentSpell is not { } row)
            return null;
        if (student.FindSpell(row.Spell.Spell.Id) is not { } known)
            return (Discount(row.Spell.Spell.TrainingCost, rules), rules.Training.FirstLevelGold);
        return (known.Level >= rules.Training.SpellMaxLevel ? null : Discount((known.Level + 1) * (known.Level + 1) * row.Spell.Spell.TrainingCost, rules),
            (known.Level + 1) * rules.Training.GoldPerLevel);
    }

    // A trainer's price: the full price in single precision × 80%, truncated, as Training's.
    private static uint Discount(int price, CharacterRules rules) => (uint)((float)(uint)price * rules.Training.TrainerExp);

    // WST_AButtonFunc and Purchase: Yes trains the row; either closes the question.
    private void Choose(MenuChoice choice)
    {
        Overlay.CloseChoice();
        if (choice.Choices.Cursor != 0 || Party.Shown is not { } student)
            return;
        var gold = Party.Party.Gold;
        if (CurrentSkill is { } skill)
        {
            Training.TrainSkill(student, skill, Trainer, ref gold);
        }
        else if (CurrentSpell is { } row)
        {
            Training.TrainSpell(student, row.Spell.Spell, Trainer, ref gold, out var learnt);
            var flag = student.Rules.Training.FirstSpellFlag;
            if (learnt && flag is { } f && _flags is not null && !_flags.IsSet(f))
            {
                _flags.TrySet(f, true);
                Overlay.ShowHelp(Party.Text[MenuText.FirstSpell]);
            }
        }
        Party.Party.Gold = gold;
        Party.Refresh();
        Follow();
    }

    // WidgetMenuSpells::SetHighlight: the member's list's cursor goes to the trainer's row, if it has it.
    private void Follow()
    {
        int at;
        if (OnSpells)
        {
            var id = Spells.Current?.Spell.Spell.Id;
            at = id is null ? -1 : Party.Spells.Rows.ToList().FindIndex(r => r.Spell.Spell.Id == id);
            if (at >= 0)
                Party.Spells.Point(at);
        }
        else
        {
            var skill = Skills.Current?.Skill;
            at = skill is null ? -1 : Party.Skills.Rows.ToList().FindIndex(r => r.Skill == skill);
            if (at >= 0)
                Party.Skills.Point(at);
        }
        Followed = at >= 0;
    }
}
