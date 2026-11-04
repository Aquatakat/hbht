using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

/// <summary>The schools, elements and animation requests (ADR 0033), keyed by the original's numbers.</summary>
internal static class KindImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Schools, elements and requests");
        ctx.WriteRecords("schools.json", BuildSchools(ctx.Items));
        ctx.WriteRecords("elements.json", BuildElements(ctx.Db));
        ctx.WriteRecords("requests.json", BuildRequests());
    }

    /// <summary>
    /// The schools' names by number, the port's: the game never shows one, so no string table holds them.
    /// </summary>
    private static readonly string[] SchoolNames = ["Chaos", "Elemental", "Naming", "Necromancy", "Unschooled", "Star"];

    /// <summary>
    /// The six schools, with their icons (<c>Spell.SchoolIcons</c>, <c>0x800DEFC0</c>, by school; 0 for none). A Chaos character learns
    /// any school's spells (<c>Entity::CanLearnSpell</c>), and the unschooled's effects can't be dispelled (<c>Entity.Dispel</c>,
    /// <c>0x80071CD8</c>, returns at once for school 4).
    /// </summary>
    public static List<SchoolRecord> BuildSchools(ItemTables tables) =>
    [
        .. Enum.GetValues<MagicSchool>().Select(school => new SchoolRecord(Numbering.School(school), SchoolNames[(int)school],
            (int)school < tables.SchoolIcons.Count && tables.SchoolIcons[(int)school] != 0
                ? RecordId.Hex(RecordTypes.Image, tables.SchoolIcons[(int)school]) : null,
            LearnsEverySchool: school == MagicSchool.Chaos, Dispellable: school != MagicSchool.None)),
    ];

    /// <summary>
    /// Where the element names are in <c>String/Common</c>: lines 241 to 257, one per <see cref="Element"/> in its order, in all three
    /// versions. The code reaches them by a pointer at <c>0x800E0C00</c> (<c>0x80083620</c> sets it to the table's line 241, and
    /// <c>Entity.AppendResistanceLine</c>, <c>0x8006FFB0</c>, indexes it by a resistance's element byte).
    /// </summary>
    internal const int ElementNameLine = 241;

    /// <summary>
    /// The elements but none, with their names and what a resistance to each covers on a blow (<c>Combatant.ApplyResistance</c>,
    /// <c>0x80062704</c>): Physical Cutting and Smashing, and Magic every element but none, Physical, Cutting, Smashing and Holy.
    /// </summary>
    public static List<ElementRecord> BuildElements(GameDb db)
    {
        var common = db.Strings[RomStringTable.Common];
        var magical = Enum.GetValues<Element>()
            .Where(e => e is not (Element.None or Element.Physical or Element.Cutting or Element.Smashing or Element.Holy or Element.Magic))
            .Select(e => Numbering.Element(e)!.Value).ToList();
        return
        [
            .. Enum.GetValues<Element>().Where(e => e != Element.None).Select(e => new ElementRecord(Numbering.Element(e)!.Value,
                (int)e + ElementNameLine < common.Count ? DialogueText.Unescape(common[ElementNameLine + (int)e])
                    : throw new InvalidDataException($"The Common strings have no line {ElementNameLine + (int)e} for the element {e}."),
                e switch
                {
                    Element.Physical => [Numbering.Element(Element.Cutting)!.Value, Numbering.Element(Element.Smashing)!.Value],
                    Element.Magic => magical,
                    _ => null,
                })),
        ];
    }

    /// <summary>
    /// The requests 0 to 25, named by what the engine asks each for in 1.0 (docs/formats/animation.md has the code that asks). The
    /// numbers are the actors' own: a door's or a lever's actor (a map object's) reads 1 and 2 as already used and use, and the pause
    /// menu's scroll 10, 11, 19 and 22 as its opening and closing.
    /// </summary>
    internal static readonly IReadOnlyList<(string Name, string Description)> RequestNames =
    [
        ("Idle", "Standing: what every body is asked for when it's asked for nothing else, and what a talker and a cut-scene's actors are asked for each frame."),
        ("Used", "A map object's actor made while its flag shows it already used, such as a door that's open (RenderVoxelScenes)."),
        ("Walk", "A steered body whose stick is short of running; a tripwire's animate asks a map object's actor for it."),
        ("Run", "A steered body whose stick is past the run tilt."),
        ("Talk", "The speaker, at each press that moves a conversation or a cut-scene on."),
        ("Cast", "A spell cast, a potion drunk, a healing, or a performance begun; and a spell's visual."),
        ("Gesture", "Nothing in the engine; scripts' PlayAnimation."),
        ("Hit", "A blow or spell that does damage."),
        ("Dodge", "An opponent's blow or spell that does none."),
        ("Dying", "The fall. Held: the actor plays to the end of its state and stops there."),
        ("Helped", "A spell cast at the target's own side; the pause menu's scroll opening on the Party tab."),
        ("Open Container", "The player opening a container; the pause menu's scroll closing from the System or World tab."),
        ("Swing", "An attack with a weapon whose animation is a swing: axes, maces, most swords and staves."),
        ("Thrust", "An attack with a weapon whose animation is a thrust: claws, daggers and short swords, spears, stings."),
        ("Shoot", "An attack with a bow."),
        ("Throw", "An attack with a thrown weapon, or a flask thrown."),
        ("Bite", "An attack with a bite, tusk or spit."),
        ("Breath", "An attack with a breath or a gaze."),
        ("Other Attack", "An attack with a weapon animation the table doesn't list; no base weapon has one."),
        ("Use", "The player using a door or another teleporter; a give or take in scripts; the pause menu's scroll closing from the Party tab."),
        ("Gesture 20", "Nothing in the engine; scripts' PlayAnimation."),
        ("Gesture 21", "Nothing in the engine; scripts' PlayAnimation."),
        ("Gesture 22", "Nothing in the engine; the pause menu's scroll opening on the System or World tab."),
        ("Gesture 23", "Nothing in the engine, and no actor has a branch for it; scripts' PlayAnimation asks for it, which never plays."),
        ("Unused 24", "Nothing asks for it, and no actor has a branch for it."),
        ("Sneak", "A steered body moving while sneaking."),
    ];

    public static List<RequestRecord> BuildRequests() =>
        [.. RequestNames.Select((r, n) => new RequestRecord(Requests.Id(n), r.Name, r.Description))];
}
