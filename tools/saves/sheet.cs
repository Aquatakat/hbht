#:project ../../src/HBHT.Core/HBHT.Core.csproj
#:property PublishAot=false
using System.Globalization;
using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

// Edits the party's sheets, the gold and the clock of a save, for a user check (combat parity's setups):
//   dotnet run tools/saves/sheet.cs -- <in.json> <out.json> [--gold <n>] [--hour <h[:mm]>] [--days <n>]
//       [--member <name or entity hex>
//           [--stats <stat>=<n>,...] [--current <stat>=<n>,...] [--skills <skill>=<n>,...]
//           [--spells <spell>=<level>,...] [--forget <spell,...|all>]
//           [--weapon <item|none>] [--armor <item|none>] [--shield <item|none>] [--gear <item,...|none>]
//           [--exp <total>[/<spendable>]]] ...
// The options after --member change that member's sheet, until the next --member. A stat is Intelligence, Willpower,
// Dexterity, Endurance, Strength, Stamina or Level, or its first three letters; --stats sets it trained and whole, --current
// what's left of it (damage). A skill is one of the 12, a weapon class (Hafted, Missile, Pole, Sword, Thrown) or Shield. A
// spell or an item is its name or its record ID. --hour sets the time of day, keeping the day; --days moves the clock on.
// It refuses what the original's sheet in the pak can't hold, or the original's load would drop, and prints the party
// as the save then loads.
var (input, output) = (args[0], args[1]);
var plugins = PluginSet.LoadInstalled(DataPaths.Plugins);
var save = SaveFile.Read(input);
var members = save.Party.Members.ToList();
var index = -1;
var edited = new HashSet<int>();

string[] statNames = ["Intelligence", "Willpower", "Dexterity", "Endurance", "Strength", "Stamina", "Level"];
// The pak's widths (PakSave.WriteSheet): Stamina 8 bits, Endurance and Level 6, the rest 5; a skill 4, 0xF can't be learnt.
int[] statMost = [31, 31, 31, 63, 31, 255, 63];
const int SkillMost = 14, SpellMost = 15;
string[] skillNames = ["Alchemist", "Diplomat", "Healer", "Loremaster", "Mechanic", "Merchant", "Ranger", "Stealth", "Thief",
    "Troubadour", "Warrior", "Wizard", "Bite", "Breath", "Claw", "Hafted", "Missile", "Pole", "Spit", "Sting", "Sword", "Thrown",
    "Tusk", "Shield"];
string[] savedWeaponSkills = ["Hafted", "Missile", "Pole", "Sword", "Thrown"];

int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
int Stat(string name) => Array.FindIndex(statNames, s => s.StartsWith(name, StringComparison.OrdinalIgnoreCase) && name.Length >= 3) is var i and >= 0
    ? i : throw new ArgumentException($"What stat is {name}?");
int Skill(string name) => Array.FindIndex(skillNames, s => s.Equals(name, StringComparison.OrdinalIgnoreCase)) is var i and >= 0
    ? i : throw new ArgumentException($"What skill is {name}?");
IEnumerable<(string Key, int Value)> Pairs(string list) => list.Split(',').Select(p => p.Split('=')).Select(p => (p[0].Trim(), Int(p[1])));

SpellRecord Spell(string name) =>
    (name.Contains(':') ? plugins.TryGet<SpellRecord>(RecordId.Parse(name)) : null)
    ?? plugins.All<SpellRecord>(RecordTypes.Spell).SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
    ?? throw new ArgumentException($"No spell {name}.");

(RecordId Id, string Name) Item(string name)
{
    if (name.Contains(':'))
        return (RecordId.Parse(name), name);
    var found = plugins.All<WeaponRecord>(RecordTypes.Weapon).Select(w => (w.Id, w.Name))
        .Concat(plugins.All<ArmorRecord>(RecordTypes.Armor).Select(a => (a.Id, a.Name)))
        .Concat(plugins.All<ItemRecord>(RecordTypes.Item).Select(i => (i.Id, i.Name)))
        .Where(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    return found.Count switch
    {
        1 => found[0],
        0 => throw new ArgumentException($"No item {name}."),
        _ => throw new ArgumentException($"{name} is {string.Join(", ", found.Select(f => f.Id))}: give the ID."),
    };
}
SaveItem? Wearing(string name) => name.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : new SaveItem(Item(name).Id);

EntityRecord Entity(SaveCharacter c) => plugins.Get<EntityRecord>(c.Entity);
SaveCharacter Member() => index >= 0 ? members[index] : throw new ArgumentException("Say which --member first.");
void Set(SaveCharacter c)
{
    members[index] = c;
    edited.Add(index);
}

for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--gold": save = save with { Party = save.Party with { Gold = (uint)Int(args[++i]) } }; break;
        case "--hour":
            var hm = args[++i].Split(':');
            var of = (uint)(Int(hm[0]) * GameClock.Hour + (hm.Length > 1 ? Int(hm[1]) * GameClock.Hour / 60 : 0));
            save = save with { Time = save.Time / GameClock.Day * GameClock.Day + of };
            break;
        case "--days": save = save with { Time = (uint)(save.Time + Int(args[++i]) * GameClock.Day) }; break;
        case "--member":
            var who = args[++i];
            RecordId? byHex = int.TryParse(who.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) && hex is > 0 and < 0x10000
                ? RecordId.Hex(RecordTypes.Entity, hex) : null;
            index = members.FindIndex(m => Entity(m).Name.Equals(who, StringComparison.OrdinalIgnoreCase)
                || (m.Name ?? "").Equals(who, StringComparison.OrdinalIgnoreCase) || m.Entity == byHex);
            if (index < 0)
                throw new ArgumentException($"{who} isn't in the party: {string.Join(", ", members.Select(m => Entity(m).Name))}.");
            break;
        case "--stats" or "--current":
            var trained = Member().Trained.ToArray();
            var current = Member().Current.ToArray();
            foreach (var (key, value) in Pairs(args[i + 1]))
            {
                var s = Stat(key);
                if (value < 0 || value > statMost[s])
                    throw new ArgumentException($"{statNames[s]} {value}: the pak holds 0 to {statMost[s]}.");
                if (args[i] == "--stats")
                    trained[s] = current[s] = value;
                else if (value > trained[s])
                    throw new ArgumentException($"{statNames[s]} {value} is past the trained {trained[s]}.");
                else
                    current[s] = value;
            }
            i++;
            Set(Member() with { Trained = SaveStats.From(trained), Current = SaveStats.From(current) });
            break;
        case "--skills":
            var levels = SkillSlots.ToArray(Member().Skills, Member().WeaponSkills, Member().ShieldSkill);
            var learnable = SkillSlots.ToArray(Entity(Member()).Skills, Entity(Member()).WeaponSkills, Entity(Member()).ShieldSkill);
            foreach (var (key, value) in Pairs(args[++i]))
            {
                var k = Skill(key);
                if (k is >= 12 and < 23 && !savedWeaponSkills.Contains(skillNames[k]))
                    throw new ArgumentException($"{skillNames[k]} isn't a party member's: the pak doesn't hold it.");
                if (learnable[k] is null)
                    throw new ArgumentException($"{Entity(Member()).Name} can't learn {skillNames[k]}.");
                if (value is < 0 or > SkillMost)
                    throw new ArgumentException($"{skillNames[k]} {value}: the pak holds 0 to {SkillMost}.");
                levels[k] = value;
            }
            var (skills, weapons, shield) = SkillSlots.Split(levels);
            Set(Member() with { Skills = skills, WeaponSkills = weapons, ShieldSkill = shield });
            break;
        case "--spells":
            var book = Member().Spells.ToList();
            var entity = Entity(Member());
            var alaron = index == 0;
            foreach (var (key, value) in Pairs(args[++i]))
            {
                var spell = Spell(key);
                // SaveParty::LoadSpellsFromSchool and LoadAlaron: unschooled, the school's (Star for Chaos), and Alaron's three.
                var keeps = spell.School == MagicSchool.None || spell.School == (entity.School == MagicSchool.Chaos ? MagicSchool.Star : entity.School)
                    || alaron && spell.School is MagicSchool.Elemental or MagicSchool.Naming or MagicSchool.Necromancy;
                if (!keeps)
                    throw new ArgumentException($"{entity.Name} ({entity.School}) can't keep {spell.Name} ({spell.School}): the original's load drops it.");
                if (value is < 1 or > SpellMost)
                    throw new ArgumentException($"{spell.Name} {value}: the pak holds 1 to {SpellMost}.");
                book.RemoveAll(k => k.Spell == spell.Id);
                book.Add(new KnownSpell(spell.Id, value));
            }
            Set(Member() with { Spells = book });
            break;
        case "--forget":
            var forget = args[++i];
            Set(Member() with
            {
                Spells = forget.Equals("all", StringComparison.OrdinalIgnoreCase) ? []
                    : [.. Member().Spells.Where(k => !forget.Split(',').Select(n => Spell(n).Id).Contains(k.Spell))],
            });
            break;
        case "--weapon": Set(Member() with { Weapon = Wearing(args[++i]) }); break;
        case "--armor": Set(Member() with { Armor = Wearing(args[++i]) }); break;
        case "--shield": Set(Member() with { Shield = Wearing(args[++i]) }); break;
        case "--gear":
            var gear = args[++i];
            var worn = gear.Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : gear.Split(',').Select(n => new SaveItem(Item(n).Id)).ToList();
            if (worn.Count > 12)
                throw new ArgumentException("The pak holds 12 gear.");
            Set(Member() with { Gear = worn });
            break;
        case "--exp":
            var exp = args[++i].Split('/');
            var total = uint.Parse(exp[0], CultureInfo.InvariantCulture);
            var spendable = exp.Length > 1 ? uint.Parse(exp[1], CultureInfo.InvariantCulture) : Member().SpendableExp;
            if (total >= 1 << 24 || spendable >= 1 << 24)
                throw new ArgumentException("The pak holds experience in 24 bits.");
            Set(Member() with { TotalExp = total, SpendableExp = spendable });
            break;
        default: throw new ArgumentException($"What's {args[i]}?");
    }
}
save = save with { Party = save.Party with { Members = members } };
SaveFile.Write(output, save);

// As it loads: what the rules make of it, and what they refused.
var game = GameState.Load(plugins, save);
foreach (var problem in game.Problems)
    Console.WriteLine($"problem: {problem}");
var clock = save.Time % GameClock.Day;
Console.WriteLine($"Day {save.Time / GameClock.Day}, {clock / GameClock.Hour:00}:{clock % GameClock.Hour * 60 / GameClock.Hour:00}; gold {save.Party.Gold}");
foreach (var (m, n) in game.Party.Members.Select((m, n) => (m, n)))
{
    var stats = string.Join(" ", Enum.GetValues<Stat>().Select(s => $"{s.ToString()[..3]} {m.Stats.Current(s)}/{m.Stats.Modified(s)}"));
    Console.WriteLine($"{(edited.Contains(n) ? "*" : " ")} {m.Name}: {stats}");
    if (!edited.Contains(n))
        continue;
    var saved = members[n];
    var levels = SkillSlots.ToArray(saved.Skills, saved.WeaponSkills, saved.ShieldSkill);
    Console.WriteLine($"    skills: {string.Join(", ", levels.Select((l, k) => (l, k)).Where(p => p.l > 0).Select(p => $"{skillNames[p.k]} {p.l}"))}");
    Console.WriteLine($"    spells: {string.Join(", ", saved.Spells.Select(k => $"{plugins.Get<SpellRecord>(k.Spell).Name} {k.Level}"))}");
    var wearing = new[] { saved.Weapon, saved.Armor, saved.Shield }.Concat(saved.Gear).OfType<SaveItem>().Select(w => w.Item.ToString());
    Console.WriteLine($"    wearing: {string.Join(", ", wearing)}");
}
Console.WriteLine($"Wrote {output}");
