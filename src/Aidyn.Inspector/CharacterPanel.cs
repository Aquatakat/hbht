using System.Globalization;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The characters the editor works with: an entity's sheet as its record starts it, or a party
/// member's as a loaded checkpoint holds it, and the party scripts play against, with its inventory.
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

    /// <summary>The items, or null if the plugins are older than the item records.</summary>
    public static ItemCatalog? Items(PluginSet plugins)
    {
        try
        {
            return ItemCatalog.From(plugins);
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>An entity as its record starts it, with its True Name by the editor's flags, wearing its gear.</summary>
    public static Character? Create(PluginSet plugins, CharacterRules rules, RecordId entity, FlagState? flags, ItemCatalog? items) =>
        plugins.TryGet<EntityRecord>(entity) is { } record
            ? Character.Create(record, rules, plugins.Get<SpellRecord>,
                flag => flags is not null && flags.Network.TryHandle(flag, out var handle) && flags.Network.Get(handle), items)
            : null;

    /// <summary>
    /// A party member as a checkpoint holds them, wearing what they wear, or null if their records are
    /// missing. The sheet already counts what's worn, so it isn't applied again.
    /// </summary>
    public static Character? Restore(PluginSet plugins, CharacterRules rules, CheckpointCharacter sheet, ItemCatalog? items)
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
        if (items is not null)
        {
            foreach (var worn in new[] { sheet.Weapon, sheet.Armor, sheet.Shield }.OfType<CheckpointItem>())
            {
                if (Instance(items, worn) is { } instance)
                    character.Equipment.Restore(instance);
            }
            for (var slot = 0; slot < sheet.Gear.Count; slot++)
            {
                if (sheet.Gear[slot] is { } gear && Instance(items, gear) is { } instance)
                    character.Equipment.Restore(instance, slot);
            }
        }
        return character;
    }

    /// <summary>
    /// The loaded checkpoint's party, with its gold and inventory, or a new game's: Alaron alone, with
    /// the weapons he doesn't wield in the inventory.
    /// </summary>
    /// <param name="writesFlags">Whether key items set and clear the editor's flags as the party gains and loses them.</param>
    public static Party NewParty(PluginSet plugins, CharacterRules rules, FlagState? flags, ItemCatalog? items, bool writesFlags = false)
    {
        var party = items is null ? new Party() : new Party(items, writesFlags ? flags?.Network : null);
        if (flags?.ConsoleParty is { } sheets)
        {
            foreach (var sheet in sheets)
            {
                if (Restore(plugins, rules, sheet, items) is { } member)
                    party.Add(member);
            }
            party.Gold = flags.ConsoleGold ?? 0;
            foreach (var stack in flags.ConsoleInventory ?? [])
            {
                if (items is not null && Instance(items, stack.Item) is { } held)
                    party.Inventory!.Add(held.Item, stack.Quantity, held);
            }
        }
        else if (Create(plugins, rules, Alaron, flags, items) is { } alaron)
        {
            party.Add(alaron);
        }
        return party;
    }

    /// <summary>An item's record ID from the console's: weapons are 0x07…, armor and shields 0x05… and 0x06…, the rest items.</summary>
    public static RecordId ItemId(ushort id) => (id >> 8) switch
    {
        7 => RecordId.Hex(RecordTypes.Weapon, id),
        5 or 6 => RecordId.Hex(RecordTypes.Armor, id),
        _ => RecordId.Hex(RecordTypes.Item, id),
    };

    /// <summary>An item as the console holds one, with its price, battery and charges, or null if no record has it.</summary>
    public static ItemInstance? Instance(ItemCatalog items, CheckpointItem held) => items.TryGet(ItemId(held.Id)) is { } item
        ? new ItemInstance(item)
        {
            Price = held.Price,
            Battery = held.StatMod is { Stat: 5, Amount: var left } ? left : null,
            Charges = held.Charges,
        }
        : null;
}

/// <summary>
/// An entity's page: its character sheet, with its hit points, experience, attributes, skills,
/// spells and equipment, and what raising each costs. Experience can be added, training tried after
/// a battle or at a trainer, and items put on and taken off, with the core's rules. A party member's
/// sheet can be a loaded checkpoint's.
/// </summary>
public partial class CharacterPanel : PagePanel
{
    private readonly FlagState? _flags;
    private readonly EntityRecord _entity;
    private readonly CharacterRules _rules;
    private readonly ItemCatalog? _items;
    private readonly Label _result = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly List<EntityRecord> _trainers;
    private Character _character;
    private Party? _party;
    private Trainer? _trainer;
    private uint _gold;
    private bool _fromCheckpoint;

    public CharacterPanel(PluginCatalog catalog, FlagState? flags, EntityRecord entity, CharacterRules rules, ItemCatalog? items, Action<RecordId> open)
        : base(catalog, open)
    {
        (_flags, _entity, _rules, _items) = (flags, entity, rules, items);
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

    /// <summary>
    /// The sheet as it starts, and the party's gold with it: the checkpoint's, or plenty to try training
    /// with. It's alone in a party whose inventory holds what it takes off, and the weapons it brings.
    /// </summary>
    private Character Build()
    {
        _gold = _fromCheckpoint ? _flags?.ConsoleGold ?? 0 : 10_000;
        var character = (_fromCheckpoint && CheckpointSheet() is { } sheet ? EditorCharacters.Restore(Plugins, _rules, sheet, _items) : null)
            ?? EditorCharacters.Create(Plugins, _rules, _entity.Id, _flags, _items)!;
        _party = _items is null ? null : new Party(_items);
        _party?.Add(character);
        return character;
    }

    private void Refresh()
    {
        ClearBody();
        var c = _character;

        Body.AddChild(Header());
        if (CheckpointSheet() is not null)
        {
            var source = new OptionButton { TooltipText = "Whose sheet: the record's, or the loaded checkpoint's." };
            source.AddItem("The record's sheet", 0);
            source.AddItem($"The sheet in {_flags!.Name}", 1);
            source.Selected = _fromCheckpoint ? 1 : 0;
            source.ItemSelected += i => Reset(i == 1);
            Body.AddChild(source);
        }
        Body.AddChild(Controls());
        _result.Text = _result.Text.Length == 0 ? " " : _result.Text;
        Body.AddChild(_result);

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
        Body.AddChild(stats);

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
        Body.AddChild(skills);
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
                var spell = c.FindSpell(id)?.Spell ?? Plugins.Get<SpellRecord>(id);
                var level = c.FindSpell(id)?.Level;
                var quote = Training.QuoteSpell(c, spell, _trainer, _gold);
                grid.AddChild(LinkButton(id, spell.Name));
                Row(grid, [level is { } l ? Num(l) : "—", spell.School.ToString(), Num(spell.Wizard), Price(quote)]);
                grid.AddChild(TrainButton(quote, () => Training.TrainSpell(c, spell, _trainer, ref _gold, out _),
                    level is null ? $"{spell.Name} learnt" : $"{spell.Name} raised to {level + 1}"));
            }
            Body.AddChild(grid);
        }

        if (_party is { } party)
            ShowEquipment(party);

        Heading("In combat");
        var armor = c.Equipment.ArmorProtection;
        Line($"Protection {c.Protection + armor}{(armor != 0 ? $" ({armor} of it from armor and shield)" : "")}, damage {c.Damage}, morale {_entity.Morale}. "
            + $"Worth {_entity.Experience:N0} experience before the award's × {_rules.Progression.ExpAward.ToString(CultureInfo.InvariantCulture)}.");
        if (_entity.Resistances.Count > 0)
            Line("Takes " + string.Join(", ", _entity.Resistances.Select(r => $"{r.Damage.ToString(CultureInfo.InvariantCulture)} × {r.Element} damage")) + ".");
        if (_party is null)
        {
            foreach (var gear in _entity.Weapons.Append(_entity.Armor).Append(_entity.Shield).OfType<RecordId>())
                Body.AddChild(LinkButton(gear, Catalog.Find(gear)?.Summary ?? gear.ToString()));
        }
        Line("Spell and potion effects, and what enchantments and resistances do, come with combat (2.5).", Dim);
    }

    /// <summary>
    /// What it wears, each with what it does and a button to take it off, what the party carries, and
    /// any item to put on, by the core's rules.
    /// </summary>
    private void ShowEquipment(Party party)
    {
        var c = _character;
        var equipment = c.Equipment;
        Heading("Equipment");
        var worn = Grid(["", "", "What it does", ""]);
        var slots = new List<(string Slot, ItemInstance? Item)> { ("Weapon", equipment.Weapon), ("Armor", equipment.Armor), ("Shield", equipment.Shield) };
        slots.AddRange(equipment.Gear.Select((g, i) => ($"Gear {i + 1}", g)));
        foreach (var (slot, item) in slots)
        {
            if (item is null)
                continue;
            worn.AddChild(new Label { Text = slot, Modulate = Dim });
            worn.AddChild(ItemLink(item.Id, item.Item.Icon, item.Item.Name));
            var does = ItemText.Does(NameOf, item.Item).Append(ItemText.State(item)).Where(t => t.Length > 0).ToList();
            worn.AddChild(new Label { Text = does.Count == 0 ? "—" : string.Join("; ", does) });
            var off = new Button { Text = "Take off", TooltipText = "Into the party's inventory, with its battery and charges." };
            off.Pressed += () =>
            {
                _result.Text = party.Unequip(c, item) ? $"{item.Item.Name} taken off."
                    : item.Item.Weapon?.Fixed == true ? $"{item.Item.Name} can't be taken off." : "The inventory has no room for it.";
                Refresh();
            };
            worn.AddChild(off);
        }
        if (worn.GetChildCount() == 4)
            Line("Nothing.", Dim);
        else
            Body.AddChild(worn);
        if (equipment.Enchantments.Any())
            Line("On while worn: " + string.Join(", ", equipment.Enchantments.Select(e => $"{NameOf(e.Spell)} at level {e.Level}")) + ".");

        var carried = party.Inventory!.Stacks;
        if (carried.Count > 0)
            Line("Carried: " + string.Join(", ", carried.Select(s => s.Quantity > 1 ? $"{s.Item.Name} × {s.Quantity}" : s.Item.Name)) + ".", Dim);

        // Anything worn, what the party carries first. Scrolls are read in trek, and worn only in combat.
        var wearable = _items!.All.Where(i => i.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield || (i.IsGear && i.Kind != ItemKind.Scroll))
            .OrderBy(i => party.Inventory.Has(i.Id) ? 0 : 1).ThenBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var row = new HBoxContainer();
        var choice = new OptionButton { TooltipText = "An item to put on. If the party doesn't carry it, it's given one first.", FitToLongestItem = false };
        foreach (var item in wearable)
            choice.AddItem($"{item.Name} ({item.Kind}{(party.Inventory.Has(item.Id) ? ", carried" : "")})");
        var put = new Button { Text = "Put on", TooltipText = "By the core's rules: what it needs, and what it replaces goes to the inventory." };
        put.Pressed += () =>
        {
            if (choice.Selected < 0)
                return;
            var item = wearable[choice.Selected];
            var given = !party.Inventory.Has(item.Id) && party.Inventory.Add(item);
            var refusal = party.Equip(c, item.Id);
            if (refusal != EquipRefusal.None && given)
                party.Inventory.Take(item.Id);
            _result.Text = refusal == EquipRefusal.None ? $"{item.Name} put on." : $"{c.Name} can't put on {item.Name}: {ItemText.Refusal(refusal)}";
            Refresh();
        };
        row.AddChild(choice);
        row.AddChild(put);
        Body.AddChild(row);
    }

    private Control Header()
    {
        var c = _character;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        if (_entity.Portrait is { } portrait && Catalog.Find(portrait)?.Asset("png") is { } png)
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
            _trainer = i == 0 ? null : Trainer.From(_trainers[(int)i - 1], _rules, Plugins.Get<SpellRecord>);
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

    private static string Modified(int modified, int @base) => modified == @base ? Num(modified) : $"{modified} ({modified - @base:+0;-0})";

}
