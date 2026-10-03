using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>What the editor says of items.</summary>
public static class ItemText
{
    /// <summary>What wearing or wielding an item does, a phrase each.</summary>
    public static IEnumerable<string> Does(Func<RecordId, string> name, Item item)
    {
        var bonuses = item.Bonuses;
        if (bonuses.Aspect != Aspect.None)
            yield return $"only for the {bonuses.Aspect} aspect";
        if (item.Armor is { } armor)
        {
            if (armor.Protection != 0)
                yield return $"{armor.Protection:+0;-0} protection";
            if (armor.Dexterity != 0)
                yield return $"{armor.Dexterity:+0;-0} Dexterity";
            if (armor.Stealth != 0)
                yield return $"{armor.Stealth:+0;-0} Stealth";
        }
        if (item.Gear is { } gear)
        {
            if (gear.Protection != 0)
                yield return $"{gear.Protection:+0;-0} protection";
            if (gear.Damage != 0)
                yield return $"{gear.Damage:+0;-0} damage";
        }
        if (bonuses.Stat is { } stat)
            yield return item.BatteryCapacity is { } capacity ? $"a battery of {capacity} Stamina for spells" : $"{stat.Amount:+0;-0} {name(stat.Stat)}";
        if (bonuses.Skill is { } skill)
            yield return $"{skill.Amount:+0;-0} {name(skill.Skill)}";
        if (bonuses.Spell is { } spell)
            yield return $"casts {name(spell.Spell)} at level {spell.Level}, {spell.Charges} charge{(spell.Charges == 1 ? "" : "s")}";
        if (bonuses.Enchantment is { } enchantment)
            yield return $"{name(enchantment.Spell)} at level {enchantment.Level} " + (item.Kind == ItemKind.Weapon ? "as it strikes" : "while worn");
        if (bonuses.Resistance is { } resistance)
            yield return $"takes {resistance.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture)} × {resistance.Element} damage";
    }

    /// <summary>What's left in an item's battery and of its charges, where it has them and they've been used.</summary>
    public static string State(ItemInstance item)
    {
        var parts = new List<string>();
        if (item.Battery is { } battery && battery != item.Item.BatteryCapacity)
            parts.Add($"battery at {battery} of {item.Item.BatteryCapacity}");
        if (item.Charges is { } charges && charges != item.Item.Bonuses.Spell?.Charges)
            parts.Add($"{charges} charges left");
        return string.Join(", ", parts);
    }

    /// <summary>Why a character can't equip an item, said of the character as "they".</summary>
    public static string Refusal(EquipRefusal refusal, Item item) => refusal switch
    {
        EquipRefusal.CantUse => item.Kind switch
        {
            ItemKind.Weapon => $"they can't wield {item.Weapon!.Class} weapons",
            ItemKind.Shield => "they have no shield skill",
            _ => "it isn't worn",
        },
        EquipRefusal.WrongAspect => $"it's only for the {item.Bonuses.Aspect} aspect",
        EquipRefusal.NeedsStrength => $"their base Strength is below {item.RequiredStrength}",
        EquipRefusal.NeedsIntelligence => $"their base Intelligence is below {item.RequiredIntelligence}",
        EquipRefusal.NoRoom => "they've no room for it",
        EquipRefusal.Fixed => "what it would replace can't be taken off",
        _ => "",
    };

    public static string Trade(TradeResult result) => result switch
    {
        TradeResult.CantAfford => "The party can't afford it.",
        TradeResult.StackFull => "The party's stack of it is full.",
        TradeResult.InventoryFull => "The inventory has no slot for it.",
        TradeResult.Refused => "The shopkeeper won't buy it: it would sell for 1 gold or less.",
        TradeResult.NoneLeft => "There's none of it.",
        _ => "",
    };

    public static string Stack(RecordId id, int count, ItemCatalog items) =>
        (items.TryGet(id)?.Name ?? id.ToString()) + (count > 1 ? $" × {count}" : "");
}

/// <summary>
/// A weapon's, armor's or item's page: its icon, what it is, and what wearing it does, with what it
/// would do for each of the editor's party, by the core's rules.
/// </summary>
public partial class ItemPanel : PagePanel
{
    private readonly EditorGame? _game;
    private readonly CharacterRules? _rules;
    private readonly ItemCatalog _items;
    private readonly Item _item;

    public ItemPanel(PluginCatalog catalog, EditorGame? game, CharacterRules? rules, ItemCatalog items, Item item, Action<RecordId> open)
        : base(catalog, open)
    {
        (_game, _rules, _items, _item) = (game, rules, items, item);
        Build();
    }

    private void Build()
    {
        var item = _item;
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        if (Icon(item.Icon) is { } icon)
        {
            header.AddChild(new TextureRect
            {
                Texture = icon,
                TextureFilter = TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = icon.GetSize() * 3,
            });
        }
        var text = new VBoxContainer();
        var name = new Label { Text = item.Name };
        name.AddThemeFontSizeOverride("font_size", 20);
        text.AddChild(name);
        var traits = new List<string> { Kind(item), $"{item.Price:N0} gold" };
        if (item.Traits.OwnSlot)
            traits.Add("a slot of its own");
        if (item.Traits.MustTake)
            traits.Add("loot can't be left with it");
        if (item.Weapon?.Fixed == true)
            traits.Add("can't be taken off");
        text.AddChild(new Label { Text = string.Join(" · ", traits), Modulate = Dim });
        header.AddChild(text);
        Body.AddChild(header);

        if (item.Weapon is { } weapon)
        {
            Heading("Wielding it");
            Body.AddChild(LinkButton(RecordId.Decimal(RecordTypes.WeaponSkill, (int)weapon.Class), $"{weapon.Class} skill, which must be above 0"));
            Line($"Damage {weapon.Damage}, hit {weapon.Hit}, range {weapon.Range}" + (weapon.DamageType != Element.None ? $", {weapon.DamageType} damage" : "") + ".");
        }
        if (item.Armor is { Slot: ArmorSlot.Shield } shield)
            Line($"Defense {shield.Defense}. Carrying it needs the shield skill.");
        var needs = new List<string>();
        if (item.RequiredStrength > 0)
            needs.Add($"base Strength {item.RequiredStrength}");
        if (item.RequiredIntelligence > 0)
            needs.Add($"base Intelligence {item.RequiredIntelligence}");
        if (needs.Count > 0)
            Line($"Needs {string.Join(" and ", needs)}.");

        var does = ItemText.Does(NameOf, item).ToList();
        if (does.Count > 0)
        {
            Heading(item.Weapon is not null ? "What it does" : "Wearing it");
            foreach (var phrase in does)
                Line(char.ToUpperInvariant(phrase[0]) + phrase[1..] + ".");
            foreach (var spell in new[] { item.Bonuses.Spell?.Spell, item.Bonuses.Enchantment?.Spell }.OfType<RecordId>().Distinct())
                Body.AddChild(LinkButton(spell, NameOf(spell)));
            if (item.Bonuses.Skill is not null)
                Line("A skill bonus counts only while the wearer's base level in it is above 0.", Dim);
        }
        if (item.Kind == ItemKind.Potion)
            Line("What potions do comes with combat (2.5).", Dim);

        if (item.KeyItem is { } key)
        {
            Heading("Key item");
            Line("While the party has it, it sets:");
            Body.AddChild(LinkButton(key.Held, $"flag {key.Held.Key}, cleared when the last is taken"));
            if (key.Region is { } region)
            {
                Line("When first gained, it reveals a region of the world map:");
                Body.AddChild(LinkButton(region, $"flag {region.Key}"));
            }
        }

        if (_rules is not null && (item.Kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield || item.IsGear))
            ShowOnParty(_rules);
    }

    private static string Kind(Item item) => item.Kind switch
    {
        ItemKind.Weapon => $"{item.Weapon!.Class} weapon",
        ItemKind.Armor => "body armor",
        ItemKind.Misc => "item",
        _ => item.Kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// For each of the editor's party, what putting it on would do to their sheet, or why they can't:
    /// equipped by the core on a fresh copy of the party. A scroll is read, as in trek.
    /// </summary>
    private void ShowOnParty(CharacterRules rules)
    {
        var count = NewParty(rules).Members.Count();
        var scroll = _item.Kind == ItemKind.Scroll;
        Heading(scroll ? "Reading it" : "Equipping it");
        var grid = Grid(["", ""]);
        for (var i = 0; i < count; i++)
        {
            var party = NewParty(rules);
            var member = party.Members.ElementAt(i);
            var before = Sheet(member, rules);
            var wornBefore = member.Equipment.All.ToList();
            party.Inventory!.Add(_item);
            string result;
            if (_item.Kind == ItemKind.Scroll)
            {
                result = party.ReadScroll(member, _item.Id, Plugins.Get<SpellRecord>) switch
                {
                    ScrollResult.Learned => $"learns {NameOf(_item.Bonuses.Spell!.Spell)}",
                    ScrollResult.NotAWizard => "can't; they can't learn Wizard",
                    ScrollResult.NoSpell => "can't; it holds no spell",
                    ScrollResult.AlreadyKnown => "knows its spell already",
                    ScrollResult.NeedsWizard => $"can't; their base Wizard is below {Plugins.Get<SpellRecord>(_item.Bonuses.Spell!.Spell).Wizard}",
                    ScrollResult.WrongSchool => "can't; the spell is of another school",
                    _ => "",
                };
            }
            else if (party.Equip(member, _item.Id) is var refusal && refusal != EquipRefusal.None)
            {
                result = $"can't; {ItemText.Refusal(refusal, _item)}";
            }
            else
            {
                var changes = before.Zip(Sheet(member, rules)).Where(p => p.First.Value != p.Second.Value)
                    .Select(p => $"{p.First.Name} {p.First.Value} → {p.Second.Value}").ToList();
                var replaced = wornBefore.Where(w => !member.Equipment.All.Contains(w)).Select(w => w.Item.Name).ToList();
                result = (replaced.Count > 0 ? $"replacing {string.Join(", ", replaced)}: " : "")
                    + (changes.Count > 0 ? string.Join(", ", changes) : "no change to the sheet");
            }
            grid.AddChild(new Label { Text = EditorCharacters.Label(_game, member) });
            grid.AddChild(new Label { Text = result, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        }
        Body.AddChild(grid);
    }

    private Party NewParty(CharacterRules rules) => EditorCharacters.CopyParty(Plugins, rules, _game, _items);

    /// <summary>The values putting an item on can change, by name.</summary>
    private static List<(string Name, int Value)> Sheet(Character c, CharacterRules rules)
    {
        var values = Enum.GetValues<Stat>().Select(s => (rules[s].Name, c.Stats.Modified(s))).ToList();
        values.Add(("hit points", c.MaxHp));
        values.AddRange(SkillId.All.Where(c.Skills.CanLearn).Select(s => (rules[s].Name, c.Skills.Modified(s))));
        values.Add(("protection", c.Protection + c.Equipment.ArmorProtection));
        values.Add(("damage", c.Damage));
        return values;
    }
}

/// <summary>
/// A shop's page: its shopkeeper, and its stock at the prices the editor's party would pay, with what
/// it would get for what it carries and how its best appraiser judges each price, on a copy of the
/// party. Under that, the shop's window against the editor's game, as a script opens it.
/// </summary>
public partial class ShopPanel : PagePanel
{
    private readonly EditorGame? _game;
    private readonly CharacterRules? _rules;
    private readonly ItemCatalog _items;
    private readonly ShopRecord _record;
    private readonly EntityRecord? _shopkeeper;
    private readonly IReadOnlyList<string>? _common;
    private readonly VBoxContainer _window = new();
    private Party _party = new();
    private Shop? _shop;
    private string _barter = "";

    public ShopPanel(PluginCatalog catalog, EditorGame? game, CharacterRules? rules, ItemCatalog items, ShopRecord record, Action<RecordId> open)
        : base(catalog, open)
    {
        (_game, _rules, _items, _record) = (game, rules, items, record);
        _shopkeeper = Plugins.TryGet<EntityRecord>(record.Shopkeeper);
        _common = Plugins.All<StringsRecord>(RecordTypes.Strings).FirstOrDefault(s => s.Name == "Common")?.Lines;
        _party = _rules is null ? new Party(_items) : EditorCharacters.CopyParty(Plugins, _rules, _game, _items);
        if (_shopkeeper is not null)
        {
            var stamina = _party.Members.Select(m => m.Stats.Modified(Stat.Stamina)).ToList();
            _shop = Shop.Open(_record, _shopkeeper, _party, _items);
            var barterer = _party.Members.Select((m, i) => (m, Paid: stamina[i] - m.Stats.Modified(Stat.Stamina))).FirstOrDefault(p => p.Paid != 0);
            _barter = barterer.m is { } who
                ? $"{who.Name} barters, with Merchant {who.Skills.Modified(Skill.Merchant)}, for {barterer.Paid} Stamina."
                : "No one barters: no member has Merchant, or the Stamina it costs, so the party's counts as 0.";
        }
        Build();
        OpenWindow();
    }

    /// <summary>The shop's window, if the editor has a game to open it against.</summary>
    public BarterWindowPanel? Window => _window.GetChildren().OfType<BarterWindowPanel>().FirstOrDefault();

    private void Build()
    {
        if (_shopkeeper is null || _shop is not { } shop)
        {
            Line($"No entity {_record.Shopkeeper} runs it.");
            return;
        }
        Body.AddChild(LinkButton(_shopkeeper.Id, $"{_shopkeeper.Name}'s shop, Merchant {_shopkeeper.Skills.Merchant ?? 0}"));
        var members = string.Join(", ", _party.Members.Select(m => m.Name));
        Line($"For {(_game is { IsNewGame: false } ? $"{_game.Name}'s party" : "a new game's party")} ({members}): {_barter} "
            + $"The barter factor is {Num(shop.Barter)}: buying costs the price × {Num(1 + shop.Barter)}, and selling pays it × {Num((1 - shop.Barter) * _items.Rules.SellShare)}.");

        Heading("Stock");
        var stock = Grid(["", "Base", "Shop's", "Buy for", "Sell for", "The party judges"]);
        foreach (var stack in shop.Stock.Stacks)
        {
            stock.AddChild(ItemLink(stack.Item.Id, stack.Item.Icon, stack.Item.Name));
            var sell = shop.SellPrice(new ItemInstance(stack.Item));
            Row(stock, $"{stack.Item.Price:N0}", $"{stack.Instance.Price:N0}", $"{shop.BuyPrice(stack.Instance):N0}", sell <= 1 ? "won't buy" : $"{sell:N0}",
                Appraisal(shop.Appraise(stack.Instance)));
        }
        Body.AddChild(stock);
        Line("Appraisal is the line the shop shows on C-up; the best appraiser counts, dead or not.", Dim);

        Heading("The party's inventory");
        if (_party.Inventory!.Stacks.Count == 0)
        {
            Line("Nothing.", Dim);
        }
        else
        {
            var carried = Grid(["", "Held", "Sell for"]);
            foreach (var stack in _party.Inventory.Stacks)
            {
                carried.AddChild(ItemLink(stack.Item.Id, stack.Item.Icon, stack.Item.Name));
                var price = shop.SellPrice(stack.Instance);
                Row(carried, $"{stack.Quantity}", price <= 1 ? "won't buy" : $"{price:N0}");
            }
            Body.AddChild(carried);
        }
        Line("Equipped items must be taken off to sell. The shop doesn't stock what it buys.", Dim);

        if (_game is null)
            return;
        Heading("The window");
        var again = new Button
        {
            Text = "Open again",
            TooltipText = "Open the shop's window afresh on the game's party, as a script does: the barterer pays its Stamina again.",
        };
        again.Pressed += OpenWindow;
        Body.AddChild(again);
        Body.AddChild(_window);
    }

    /// <summary>Opens the shop's window on the editor's game's party, which costs the barterer Stamina, as in the game.</summary>
    private void OpenWindow()
    {
        if (_game is null || _shopkeeper is null)
            return;
        foreach (var child in _window.GetChildren())
            child.QueueFree();
        var party = _game.Game.Party;
        var shop = Shop.Open(_record, _shopkeeper, party, party.Inventory!.Catalog, _game.Game.Flags);
        _window.AddChild(BarterWindowPanel.ForShop(Plugins, _game, shop));
    }

    private string Appraisal(int index) =>
        _common is { } lines && MenuText.Appraisal + index < lines.Count ? lines[MenuText.Appraisal + index].Replace('\n', ' ') : $"line {MenuText.Appraisal + index}";
}

/// <summary>A loot table's page: what it gives and its chances, with rolls by the core's rules.</summary>
public partial class LootPanel : PagePanel
{
    private readonly ItemCatalog _items;
    private readonly LootRecord _loot;
    private readonly EditorGame? _game;
    private readonly Lcg _random = new((uint)System.Random.Shared.Next());
    private readonly VBoxContainer _rolls = new();
    private readonly VBoxContainer _window = new();

    public LootPanel(PluginCatalog catalog, EditorGame? game, ItemCatalog items, LootRecord loot, Action<RecordId> open)
        : base(catalog, open)
    {
        (_items, _loot, _game) = (items, loot, game);
        Build();
    }

    /// <summary>The loot window on the last roll, if one is open.</summary>
    public BarterWindowPanel? Window => _window.GetChildren().OfType<BarterWindowPanel>().FirstOrDefault();

    private void Build()
    {
        var loot = _loot;
        var name = new Label { Text = loot.Name };
        name.AddThemeFontSizeOverride("font_size", 20);
        Body.AddChild(name);
        Line($"Gold: {loot.GoldMin * 2:N0} to {loot.GoldMax * 2:N0}, rolled between the bounds and doubled. Opening a container that names it gives this instead of the container's own gold.");

        Heading("What it gives");
        var grid = Grid(["", "Chance", "Count"]);
        var rules = _items.Rules;
        grid.AddChild(new Label { Text = $"{NameOf(rules.Spice)}, {NameOf(rules.Herb)} or {NameOf(rules.Gemstone)}, picked first" });
        Row(grid, $"{loot.ReagentChance}%", Count(loot.ReagentMin, loot.ReagentMax));
        for (var i = 0; i < loot.Drops.Count; i++)
        {
            var drop = loot.Drops[i];
            if (drop.Item is { } id && _items.TryGet(id) is { } item)
                grid.AddChild(ItemLink(id, item.Icon, item.Name));
            else
                grid.AddChild(new Label { Text = drop.Item?.ToString() ?? "nothing", Modulate = Dim });
            // Only the first two rolls give a count between their bounds.
            Row(grid, $"{drop.Chance}%", i < 2 ? Count(drop.Min, drop.Max) : "1");
        }
        Body.AddChild(grid);
        Line("A roll at 0% never hits, and an empty roll that hits gives nothing.", Dim);
        Line($"Beaten enemies drop their armor {loot.ArmorChance}%, shield {loot.ShieldChance}% and weapons {string.Join(", ", loot.WeaponChances.Select(c => $"{c}%"))} of the time, which combat's rewards read (2.5).", Dim);

        Heading("Rolls");
        var row = new HBoxContainer();
        var once = new Button
        {
            Text = "Roll", TooltipText = "As a container opens: the core's Containers.Roll, in the loot window against the editor's game.",
        };
        once.Pressed += () => Roll();
        var many = new Button { Text = "Roll 10,000 times", TooltipText = "How often each item comes, and how much gold on average." };
        many.Pressed += RollMany;
        row.AddChild(once);
        row.AddChild(many);
        Body.AddChild(row);
        Body.AddChild(_rolls);
        Body.AddChild(_window);
    }

    /// <summary>Rolls the table, as a container that names it opens, and opens the loot window on it (<c>--roll</c>).</summary>
    public void Roll()
    {
        var roll = Containers.Roll(_loot, _items.Rules, _random);
        Show(roll);
        foreach (var child in _window.GetChildren())
            child.QueueFree();
        var pile = new ItemPile(_items);
        foreach (var (item, count) in roll.Items)
            pile.Add(item, count);
        // With no items, a container gives the gold alone: "You just picked up n gold!"
        if (_game is not null && pile.Stacks.Count > 0)
            _window.AddChild(BarterWindowPanel.ForLoot(Catalog.Plugins, _game, pile, roll.Gold, $"A roll of {_loot.Name}"));
    }

    private static string Count(int min, int max) => min == max ? $"{min}" : $"{min} to {max}";

    private void Show(LootRoll roll)
    {
        var items = roll.Items.Select(i => ItemText.Stack(i.Item, i.Count, _items)).ToList();
        _rolls.AddChild(new Label { Text = $"{roll.Gold:N0} gold" + (items.Count > 0 ? ", " + string.Join(", ", items) : ""), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        while (_rolls.GetChildCount() > 12)
        {
            var oldest = _rolls.GetChild(0);
            _rolls.RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    private void RollMany()
    {
        const int times = 10_000;
        var gold = 0L;
        var given = new Dictionary<RecordId, (int Rolls, long Count)>();
        for (var n = 0; n < times; n++)
        {
            var roll = Containers.Roll(_loot, _items.Rules, _random);
            gold += roll.Gold;
            foreach (var (item, count) in roll.Items)
            {
                var (rolls, total) = given.GetValueOrDefault(item);
                given[item] = (rolls + 1, total + count);
            }
        }
        var parts = given.OrderByDescending(g => g.Value.Rolls)
            .Select(g => $"{_items.TryGet(g.Key)?.Name ?? g.Key.ToString()} in {100.0 * g.Value.Rolls / times:0.#}% ({(double)g.Value.Count / g.Value.Rolls:0.##} each time)");
        _rolls.AddChild(new Label
        {
            Text = $"Over {times:N0} rolls: {(double)gold / times:0.#} gold on average; " + (given.Count == 0 ? "no items." : string.Join(", ", parts) + "."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = Dim,
        });
    }
}

/// <summary>
/// A recipe's page: its ingredients and product, and who in the editor's party is offered it and how
/// likely they are to make it, by the core's rules over every roll of the check.
/// </summary>
public partial class RecipePanel : PagePanel
{
    private readonly EditorGame? _game;
    private readonly CharacterRules? _rules;
    private readonly ItemCatalog _items;
    private readonly RecipeRecord _recipe;

    public RecipePanel(PluginCatalog catalog, EditorGame? game, CharacterRules? rules, ItemCatalog items, RecipeRecord recipe, Action<RecordId> open)
        : base(catalog, open)
    {
        (_game, _rules, _items, _recipe) = (game, rules, items, recipe);
        Build();
    }

    private void Build()
    {
        var recipe = _recipe;
        var skill = NameOf(recipe.Skill);
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        for (var i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            if (i > 0)
                row.AddChild(new Label { Text = "+" });
            row.AddChild(Link(ingredient.Item, ingredient.Count));
        }
        row.AddChild(new Label { Text = "→" });
        row.AddChild(Link(recipe.Product, 1));
        Body.AddChild(row);

        Body.AddChild(LinkButton(recipe.Skill, $"Made with the {skill} skill"));
        var level = recipe.Skill == Crafting.Mechanic ? "base" : "modified";
        Line($"Offered from {level} {skill} {recipe.Level}" + (recipe.Step > 0 ? $"; the check's step must reach {recipe.Step}." : "; any check that passes makes it."));
        Line(recipe.Skill == Crafting.Mechanic
            ? "The check is (Mechanic × 5 + Intelligence + Dexterity) × 2 on d100, and passing costs 5 less Mechanic in Stamina, at least 1. The hide is used up whatever happens."
            : "The check is Intelligence × 3 + Alchemist × 6 on d100. The reagents are used up whatever happens.", Dim);

        if (_rules is { } rules)
            ShowOnParty(rules);
    }

    private Control Link(RecordId id, int count) => _items.TryGet(id) is { } item
        ? ItemLink(id, item.Icon, ItemText.Stack(id, count, _items))
        : LinkButton(id, id.ToString());

    /// <summary>Makes it on a fresh copy of the party, with the ingredients, for each roll of the d100.</summary>
    private void ShowOnParty(CharacterRules rules)
    {
        Heading("Making it");
        var count = EditorCharacters.CopyParty(Plugins, rules, _game, _items).Members.Count();
        var grid = Grid(["", ""]);
        for (var i = 0; i < count; i++)
        {
            string? name = null, result = null;
            var made = 0;
            for (var roll = 1; roll <= 100 && result is null; roll++)
            {
                var party = EditorCharacters.CopyParty(Plugins, rules, _game, _items);
                var member = party.Members.ElementAt(i);
                name = EditorCharacters.Label(_game, member);
                foreach (var ingredient in _recipe.Ingredients)
                    party.Inventory!.Add(ingredient.Item, ingredient.Count);
                if (!Crafting.Offered(party, member, [_recipe]).Any())
                    result = $"not offered; their {NameOf(_recipe.Skill)} is below {_recipe.Level}";
                else if (Crafting.Make(party, member, _recipe, new Roll(roll)) == CraftResult.Made)
                    made++;
            }
            grid.AddChild(new Label { Text = name });
            grid.AddChild(new Label { Text = result ?? $"makes it {made}% of the time" });
        }
        Body.AddChild(grid);
    }

    /// <summary>A stream whose every d100 rolls the same number.</summary>
    private sealed class Roll(int roll) : IRandom
    {
        private readonly uint _next = (uint)Math.Ceiling((roll - 1) * 4294967296.0 / 100);

        public uint Next() => _next;
    }
}
