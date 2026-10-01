using System.Globalization;
using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The characters the editor works with: an entity's sheet as its record starts it, or a party
/// member's as a loaded checkpoint holds it, and the party scripts play against.
/// </summary>
public static class EditorCharacters
{
    public static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);

    /// <summary>The character rules, or null if the plugins are older than the characters' records.</summary>
    public static CharacterRules? Rules(PluginSet plugins)
    {
        try
        {
            return CharacterRules.From(plugins);
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>An entity as its record starts it, with its True Name by the editor's flags.</summary>
    public static Character? Create(PluginSet plugins, CharacterRules rules, RecordId entity, FlagState? flags) =>
        plugins.TryGet<EntityRecord>(entity) is { } record
            ? Character.Create(record, rules, plugins.Get<SpellRecord>,
                flag => flags is not null && flags.Network.TryHandle(flag, out var handle) && flags.Network.Get(handle))
            : null;

    /// <summary>A party member as a checkpoint holds them, or null if their records are missing.</summary>
    public static Character? Restore(PluginSet plugins, CharacterRules rules, CheckpointCharacter sheet)
    {
        if (plugins.TryGet<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id)) is not { } entity)
            return null;
        var spells = new List<LearnedSpell>();
        foreach (var (id, level) in sheet.Spells)
        {
            if (plugins.TryGet<SpellRecord>(RecordId.Hex(RecordTypes.Spell, id)) is { } spell)
                spells.Add(new LearnedSpell(spell, level));
        }
        var character = Character.Restore(entity, rules, new CharacterStats(rules, sheet.Trained, sheet.Current, sheet.Base, sheet.Modified),
            new CharacterSkills(rules, sheet.SkillBase, sheet.SkillModified), spells);
        character.TrueName = (sheet.Flags & 1) != 0;
        character.TotalExp = sheet.TotalExp;
        character.SpendableExp = sheet.SpendableExp;
        character.Protection = sheet.Protection;
        character.Damage = sheet.Damage;
        return character;
    }

    /// <summary>The loaded checkpoint's party, or a new game's: Alaron alone.</summary>
    public static Party NewParty(PluginSet plugins, CharacterRules rules, FlagState? flags)
    {
        var party = new Party();
        if (flags?.ConsoleParty is { } sheets)
        {
            foreach (var sheet in sheets)
            {
                if (Restore(plugins, rules, sheet) is { } member)
                    party.Add(member);
            }
            party.Gold = flags.ConsoleGold ?? 0;
        }
        else if (Create(plugins, rules, Alaron, flags) is { } alaron)
        {
            party.Add(alaron);
        }
        return party;
    }
}

/// <summary>
/// An entity's page: its character sheet, with its hit points, experience, attributes, skills and
/// spells, and what raising each costs. Experience can be added, and training tried after a battle
/// or at a trainer, with the core's rules. A party member's sheet can be a loaded checkpoint's.
/// </summary>
public partial class CharacterPanel : ScrollContainer
{
    private static readonly Color Dim = new(0.65f, 0.65f, 0.65f);
    private static readonly Color Refused = new(0.85f, 0.55f, 0.45f);

    private readonly PluginCatalog _catalog;
    private readonly FlagState? _flags;
    private readonly EntityRecord _entity;
    private readonly CharacterRules _rules;
    private readonly Action<RecordId> _open;
    private readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _result = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly List<EntityRecord> _trainers;
    private Character _character;
    private Trainer? _trainer;
    private uint _gold;
    private bool _fromCheckpoint;

    public CharacterPanel(PluginCatalog catalog, FlagState? flags, EntityRecord entity, CharacterRules rules, Action<RecordId> open)
    {
        (_catalog, _flags, _entity, _rules, _open) = (catalog, flags, entity, rules, open);
        HorizontalScrollMode = ScrollMode.Disabled;
        _body.AddThemeConstantOverride("separation", 6);
        AddChild(_body);
        // Trainers are the plain NPCs, recruits who teach and anyone who trains for free.
        _trainers = [.. catalog.Plugins.All<EntityRecord>(RecordTypes.Entity)
            .Where(e => e.Category is EntityCategory.Generic or EntityCategory.Party || rules.Training.FreeTrainers.Contains(e.Id))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        _fromCheckpoint = CheckpointSheet() is not null;
        _character = Build();
        Refresh();
    }

    private CheckpointCharacter? CheckpointSheet() =>
        _flags?.ConsoleParty?.FirstOrDefault(s => RecordId.Hex(RecordTypes.Entity, s.Id) == _entity.Id);

    /// <summary>The sheet as it starts, and the party's gold with it: the checkpoint's, or plenty to try training with.</summary>
    private Character Build()
    {
        _gold = _fromCheckpoint ? _flags?.ConsoleGold ?? 0 : 10_000;
        return (_fromCheckpoint && CheckpointSheet() is { } sheet ? EditorCharacters.Restore(_catalog.Plugins, _rules, sheet) : null)
            ?? EditorCharacters.Create(_catalog.Plugins, _rules, _entity.Id, _flags)!;
    }

    private void Refresh()
    {
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        var c = _character;

        _body.AddChild(Header());
        if (CheckpointSheet() is not null)
        {
            var source = new OptionButton { TooltipText = "Whose sheet: the record's, or the loaded checkpoint's." };
            source.AddItem("The record's sheet", 0);
            source.AddItem($"The sheet in {_flags!.Name}", 1);
            source.Selected = _fromCheckpoint ? 1 : 0;
            source.ItemSelected += i => Reset(i == 1);
            _body.AddChild(source);
        }
        _body.AddChild(Controls());
        _result.Text = _result.Text.Length == 0 ? " " : _result.Text;
        _body.AddChild(_result);

        Heading("Attributes");
        var stats = Grid(["", "Base", "Modified", "Base cap", "Raise for", ""]);
        foreach (var stat in Enum.GetValues<Stat>())
        {
            var quote = Training.QuoteStat(c, stat);
            Row(stats, [_rules[stat].Name, Num(c.Stats.Base(stat)), Modified(c.Stats.Modified(stat), c.Stats.Base(stat)),
                Num(_rules[stat].BaseCap), stat == Stat.Level ? "levels by experience" : Price(quote)]);
            if (stat == Stat.Level || _trainer is not null)
                stats.AddChild(new Control());
            else
                stats.AddChild(TrainButton(quote, () => Training.TrainStat(c, stat), $"{_rules[stat].Name} raised to {c.Stats.Trained(stat) + 1}"));
        }
        _body.AddChild(stats);

        Heading("Skills");
        var offered = Training.SkillsOffered(c, _trainer).ToHashSet();
        var skills = Grid(["", "Level", "Modified", _trainer is null ? "Raise for, after a battle" : "Raise for, at the trainer", ""]);
        foreach (var skill in SkillId.All.Where(s => c.Skills.CanLearn(s) || offered.Contains(s)))
        {
            var quote = Training.QuoteSkill(c, skill, _trainer, _gold);
            var name = _rules[skill].Name + (_trainer is not null && offered.Contains(skill) ? $" (teaches {_trainer.Skills.Base(skill)})" : "");
            Row(skills, [name, Num(c.Skills.Base(skill)), Modified(c.Skills.Modified(skill), c.Skills.Base(skill)), offered.Contains(skill) ? Price(quote) : _trainer is null ? "only at a trainer" : "not offered"]);
            skills.AddChild(offered.Contains(skill)
                ? TrainButton(quote, () => Training.TrainSkill(c, skill, _trainer, ref _gold), $"{_rules[skill].Name} raised to {c.Skills.Base(skill) + 1}")
                : new Control());
        }
        _body.AddChild(skills);
        var never = SkillId.All.Where(s => !c.Skills.CanLearn(s)).Select(s => _rules[s].Name).ToList();
        if (never.Count > 0)
            Line($"Can never learn: {string.Join(", ", never)}.", Dim);

        Heading("Spells");
        var spells = Training.SpellsOffered(c, _trainer).Select(s => s.Id).Concat(c.Spells.Select(s => s.Spell.Id)).Distinct().ToList();
        if (spells.Count == 0)
            Line(c.Skills.CanLearn(Skill.Wizard) ? "None." : "None, and it can't learn Wizard, so it can learn no spells.", Dim);
        else
        {
            var grid = Grid(["", "Level", "School", "Wizard", "Raise for", ""]);
            foreach (var id in spells)
            {
                var spell = c.FindSpell(id)?.Spell ?? _catalog.Plugins.Get<SpellRecord>(id);
                var level = c.FindSpell(id)?.Level;
                var quote = Training.QuoteSpell(c, spell, _trainer, _gold);
                grid.AddChild(LinkButton(id, spell.Name));
                Row(grid, [level is { } l ? Num(l) : "—", spell.School.ToString(), Num(spell.Wizard), Price(quote)]);
                grid.AddChild(TrainButton(quote, () => Training.TrainSpell(c, spell, _trainer, ref _gold, out _),
                    level is null ? $"{spell.Name} learnt" : $"{spell.Name} raised to {level + 1}"));
            }
            _body.AddChild(grid);
        }

        Heading("In combat");
        Line($"Protection {c.Protection}, damage {c.Damage}, morale {_entity.Morale}. Worth {_entity.Experience:N0} experience before the award's × {_rules.Progression.ExpAward.ToString(CultureInfo.InvariantCulture)}.");
        if (_entity.Resistances.Count > 0)
            Line("Takes " + string.Join(", ", _entity.Resistances.Select(r => $"{r.Damage.ToString(CultureInfo.InvariantCulture)} × {r.Element} damage")) + ".");
        foreach (var gear in _entity.Weapons.Append(_entity.Armor).Append(_entity.Shield).OfType<RecordId>())
            _body.AddChild(LinkButton(gear, _catalog.Find(gear)?.Summary ?? gear.ToString()));
        Line("Gear's bonuses and spell and potion effects come with items (2.3) and combat (2.5).", Dim);
    }

    private Control Header()
    {
        var c = _character;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        if (_entity.Portrait is { } portrait && _catalog.Find(portrait)?.Asset("png") is { } png)
        {
            var image = new Image();
            if (image.LoadPngFromBuffer(File.ReadAllBytes(png.Path)) == Error.Ok)
            {
                row.AddChild(new TextureRect
                {
                    Texture = ImageTexture.CreateFromImage(image),
                    TextureFilter = TextureFilterEnum.Nearest,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    CustomMinimumSize = new Vector2(image.GetWidth(), image.GetHeight()) * 2,
                });
            }
        }
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var name = new Label { Text = c.Name };
        name.AddThemeFontSizeOverride("font_size", 20);
        text.AddChild(name);
        var next = c.NextLevelExp is { } n ? $", level {c.Level + 1} at {n:N0}" : ", the last level";
        text.AddChild(new Label { Text = $"Level {c.Level} · {c.Hp} of {c.MaxHp} hit points · {c.TotalExp:N0} experience{next} · {c.SpendableExp:N0} to spend" });
        var traits = new List<string> { _entity.Category.ToString(), c.Aspect.ToString(), c.School == MagicSchool.None ? "unschooled" : $"{c.School} school" };
        if (c.TrueName)
            traits.Add("True Name");
        else if (_entity.TrueNameFlag is { } flag)
            traits.Add($"True Name from {flag}");
        if (c.Heavy)
            traits.Add("heavy");
        text.AddChild(new Label { Text = string.Join(" · ", traits), Modulate = Dim });
        row.AddChild(text);
        return row;
    }

    private Control Controls()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        var exp = new SpinBox { MinValue = 0, MaxValue = 10_000_000, Step = 50, Value = 1000, TooltipText = "Experience to award, before the award multiplier." };
        var add = new Button { Text = "Award experience", TooltipText = "As a script or a battle does: × the award multiplier, and any levels it reaches." };
        add.Pressed += () =>
        {
            var level = _character.Level;
            _character.AddExp((int)exp.Value);
            _result.Text = _character.Level > level ? $"Reached level {_character.Level}." : "No new level.";
            Refresh();
        };
        row.AddChild(exp);
        row.AddChild(add);

        var trainer = new OptionButton { TooltipText = "Who teaches: no one, after a battle, or a trainer, whose own skills and spells limit what's offered." };
        trainer.AddItem("After a battle", 0);
        for (var i = 0; i < _trainers.Count; i++)
            trainer.AddItem($"At {_trainers[i].Name} ({_trainers[i].Id.Key})", i + 1);
        trainer.Selected = _trainer is null ? 0 : _trainers.FindIndex(t => t.Id == _trainer.Entity) + 1;
        trainer.ItemSelected += i =>
        {
            _trainer = i == 0 ? null : Trainer.From(_trainers[(int)i - 1], _rules, _catalog.Plugins.Get<SpellRecord>);
            Refresh();
        };
        row.AddChild(trainer);

        row.AddChild(new Label { Text = "Party gold" });
        var gold = new SpinBox { MinValue = 0, MaxValue = 10_000_000, Step = 10, Value = _gold };
        gold.ValueChanged += v =>
        {
            _gold = (uint)v;
            Refresh();
        };
        row.AddChild(gold);

        var reset = new Button { Text = "Reset", TooltipText = "Back to the sheet as it started." };
        reset.Pressed += () => Reset(_fromCheckpoint);
        row.AddChild(reset);
        return row;
    }

    private void Reset(bool fromCheckpoint)
    {
        _fromCheckpoint = fromCheckpoint;
        _character = Build();
        _result.Text = "";
        Refresh();
    }

    private Button TrainButton(TrainingQuote quote, Func<TrainingQuote> train, string done)
    {
        var button = new Button { Text = "+", Disabled = !quote.Allowed, TooltipText = quote.Allowed ? "Train it." : Refusal(quote.Refusal) };
        button.Pressed += () =>
        {
            var result = train();
            _result.Text = result.Allowed ? $"{done}, for {Price(result)}." : Refusal(result.Refusal);
            Refresh();
        };
        return button;
    }

    private static string Refusal(TrainingRefusal refusal) => refusal switch
    {
        TrainingRefusal.CantLearn => "It can't learn that.",
        TrainingRefusal.TrainerKnowsNoMore => "The trainer knows it no better.",
        TrainingRefusal.AtMost => "It's at its most.",
        TrainingRefusal.WizardTooLow => "Its Wizard skill is too low for that spell.",
        TrainingRefusal.WrongSchool => "That spell is of another school.",
        TrainingRefusal.CantAfford => "It can't afford that.",
        _ => "",
    };

    private static string Price(TrainingQuote quote) => quote.Refusal switch
    {
        TrainingRefusal.CantLearn when quote.Exp == 0 => "—",
        TrainingRefusal.AtMost => "at its most",
        TrainingRefusal.TrainerKnowsNoMore => "the trainer knows no more",
        TrainingRefusal.WizardTooLow => "Wizard too low",
        TrainingRefusal.WrongSchool => "another school",
        _ => quote.Gold > 0 ? $"{quote.Exp:N0} experience and {quote.Gold:N0} gold" : $"{quote.Exp:N0} experience",
    };

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Modified(int modified, int @base) => modified == @base ? Num(modified) : $"{modified} ({modified - @base:+0;-0})";

    private static GridContainer Grid(string[] headings)
    {
        var grid = new GridContainer { Columns = headings.Length };
        grid.AddThemeConstantOverride("h_separation", 16);
        foreach (var heading in headings)
            grid.AddChild(new Label { Text = heading, Modulate = Dim });
        return grid;
    }

    private static void Row(GridContainer grid, string[] cells)
    {
        foreach (var cell in cells)
            grid.AddChild(new Label { Text = cell });
    }

    private void Heading(string text)
    {
        _body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        _body.AddChild(label);
    }

    private void Line(string text, Color? color = null) =>
        _body.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = color ?? Colors.White });

    private Button LinkButton(RecordId id, string text)
    {
        var button = new Button { Text = text, Flat = true, Alignment = HorizontalAlignment.Left, TooltipText = $"Open {id}." };
        button.Pressed += () => _open(id);
        return button;
    }
}
