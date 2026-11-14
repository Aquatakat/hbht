using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Why a cover target no play reaches isn't the bot's to cover.</summary>
public enum NotTheBotsKind
{
    /// <summary>The relaxation's overestimate: it keeps no geometry, order or correlation, and sets what no play can.</summary>
    Overestimate,
    /// <summary>1.0's: cut content, or placed where play never reaches, so 1.0 never sets it either.</summary>
    Original,
    /// <summary>Reachable, but left aside by the user's judgement: no player is likely to meet it.</summary>
    SetAside,
}

/// <summary>A cover target settled as not the bot's: its kind, and the deciding facts in a line.</summary>
public sealed record NotTheBots(RecordId Flag, NotTheBotsKind Kind, string Why)
{
    /// <summary>
    /// NA 1.0's targets no play reaches that aren't the bot's (docs/systems/playthrough.md, "The user's review of what's left" and the
    /// stages' findings before it). They stay targets, so a mod or a fix that makes one reachable counts it as covered; this only says how
    /// an uncovered one is reported (<c>cover status</c>).
    /// </summary>
    public static IReadOnlyList<NotTheBots> All { get; } =
    [
        .. Range(5509, 5513, NotTheBotsKind.Overestimate,
            "a Barrows dragon's cut-scene done with its tripwire not walked into: the script object's radius lies inside the tripwire's (the data; what's left uncovered)"),
        new(F(1349), NotTheBotsKind.Overestimate, "Edur talked to with his cut-scene's 45 clear: every way to him crosses Overworld I08/4's radius (the data; what's left uncovered)"),
        new(F(1513), NotTheBotsKind.Overestimate, "Abrecan and Sheridan outside the castle stand inside the questioning scene's radius, M06/3 (the data; after stage 8)"),
        new(F(3728), NotTheBotsKind.Overestimate, "the Shadow at K08/10 wanders inside K08/2's radius, whose scene shuts it (the user's review)"),
        new(F(1221), NotTheBotsKind.Overestimate, "the Chaos Mauler at A22/11 wanders inside cradawgh's radius, A22/8, whose scene sets 319 (the user's review)"),
        new(F(1011), NotTheBotsKind.Overestimate, "Lugash wanders inside G03/9 and G03/10's radii, whose scenes start his battle without 1011 (the user's review)"),
        new(F(3209), NotTheBotsKind.Overestimate, "Sheridan in Talewok wanders inside Niesen's scene's radius, H13/11, and his script is a placeholder (the user's review)"),
        new(F(5409), NotTheBotsKind.Overestimate, "Abrecan left and in the party at once: true only between a rejoin's two writes (the user's review)"),
        new(F(5410), NotTheBotsKind.Overestimate, "Becan left and in the party at once: true only between a rejoin's two writes (the user's review)"),

        .. new[] { 600, 602, 603, 606, 609 }.Select(f => new NotTheBots(F(f), NotTheBotsKind.Original,
            "Gwernia castle's farewells, gated on 599, which only the coronation's scene sets as the credits roll (the user; what's left uncovered)")),
        new(F(987), NotTheBotsKind.Original, "Amann on his ship: no teleporter leads into MiscInteriors E01 (the data; after stage 7)"),
        .. new[] { 422, 423, 424, 5134, 5135 }.Select(f => new NotTheBots(F(f), NotTheBotsKind.Original,
            "StageH's cut-scenes at P17: the tower's tripwire P17/3 is listed first in every cell and shuts them; cut content (the user; after stage 8)")),
        new(F(565), NotTheBotsKind.Original, "Farris's staff: only a sequence break reaches it, taken as cut (the user's review)"),
        new(F(3626), NotTheBotsKind.Original, "the Dust Devil at O26/0 is placed off the ground that has collision (the user's review)"),

        new(F(3143), NotTheBotsKind.SetAside, "Tamberlain's way that wants 9 gold or less, which no party holds by then (the user's review)"),
        new(F(3150), NotTheBotsKind.SetAside, "Tamberlain's way that wants 9 gold or less, which no party holds by then (the user's review)"),
    ];

    private static readonly Dictionary<RecordId, NotTheBots> ByFlag = All.ToDictionary(n => n.Flag);

    /// <summary>The flag's entry, or null for a target that's the bot's to cover.</summary>
    public static NotTheBots? Of(RecordId flag) => ByFlag.GetValueOrDefault(flag);

    private static RecordId F(int n) => RecordId.Parse($"aidyn:flag/{n}");

    private static IEnumerable<NotTheBots> Range(int first, int last, NotTheBotsKind kind, string why) =>
        Enumerable.Range(first, last - first + 1).Select(f => new NotTheBots(F(f), kind, why));
}
