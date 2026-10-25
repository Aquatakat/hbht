using Aidyn.Core.Ui;

namespace Aidyn.Core.Controls;

/// <summary>
/// Where a control does something: the situations the player is in, by what runs (<see cref="Situations.Of"/>). Two
/// controls conflict only if they share an input in a situation both are live in (<see cref="ControlBindings.Conflicts"/>).
/// </summary>
[Flags]
public enum Situation
{
    None = 0,

    /// <summary>Free exploration: walking, the camera, using map objects and opening the menu.</summary>
    Trek = 1,

    /// <summary>The pause menu, with barter, training and the post-battle windows.</summary>
    Menus = 2,

    /// <summary>A battle's turn: walking the member whose turn it is, and the battle's buttons and held menus.</summary>
    Combat = 4,

    /// <summary>
    /// An attack, a bow's or thrown weapon's shot or a spell aimed in a battle (combat's targeting states), where the stick and the
    /// shoulders choose the target. Listed under Combat, but checked apart from the turn's walking and camera.
    /// </summary>
    Aiming = 8,

    /// <summary>
    /// Cut-scenes and conversations, and what reads presses as they do: trek's popups, a backdrop's or the help menu's page, a
    /// cinematic and the credits. The menus' cursor, Accept, Back and Close Menu work them, so they have no controls of their own.
    /// </summary>
    Talking = 16,
}

/// <summary>Which set of bindings: the keyboard with the mouse, or a controller.</summary>
public enum ControlDevice
{
    Keyboard,
    Controller,
}

/// <summary>How a control is bound.</summary>
public enum ControlKind
{
    /// <summary>A button: one or more keys, mouse buttons, controller buttons or an axis's direction (<see cref="Binding"/>).</summary>
    Button,

    /// <summary>A direction: a set of input sources, each four directions at once (<see cref="InputSource"/>), any or none of them.</summary>
    Sources,
}

/// <summary>
/// One of the player's controls: what it's called, where it does something, and what it is to the game. Controls are listed by
/// what the player does in each situation, not by the original's buttons, so a button that does different things in different
/// situations is several controls that share a default (C-down is both the menus' Details and combat's Spell Menu).
/// </summary>
/// <param name="Id">What the bindings and Godot's input actions call it (<c>menu_accept</c>).</param>
/// <param name="Label">Its name on the Controls page, in Title Case.</param>
/// <param name="Heading">The situation it's listed under.</param>
/// <param name="LiveIn">The situations it does something in, which conflicts are checked within.</param>
/// <param name="Press">What pressing it is to the game, as the core reads presses (<see cref="GameSession.Press"/>), or null for one the runtime reads as held.</param>
/// <param name="Release">What letting it go is, for a button whose release does something (combat's held menus, the menus' Details).</param>
/// <param name="Essential">
/// Whether it always keeps a binding on each device, so the player can't lock themselves out: of the menus, of this page, or of
/// a conversation (<see cref="ControlBindings.Rebind"/>).
/// </param>
/// <param name="Keyboard">Its default bindings on the keyboard and mouse.</param>
/// <param name="Controller">Its default bindings on a controller.</param>
/// <param name="Allowed">For a set of sources, the sources it can take.</param>
public sealed record GameControl(string Id, string Label, Situation Heading, Situation LiveIn, ControlKind Kind, MenuAction? Press,
    MenuAction? Release, bool Essential, IReadOnlyList<string> Keyboard, IReadOnlyList<string> Controller, IReadOnlyList<string>? Allowed = null)
{
    /// <summary>Its default bindings on a device.</summary>
    public IReadOnlyList<string> Defaults(ControlDevice device) => device == ControlDevice.Keyboard ? Keyboard : Controller;

    /// <summary>For a set of sources, those a device has that it can take.</summary>
    public IEnumerable<string> AllowedOn(ControlDevice device) =>
        (Allowed ?? []).Where(source => InputSource.Find(source)?.Device == device);
}

/// <summary>
/// The player's controls, in the order the Controls page lists them: what the runtime reads in trek, the menus, a battle and a
/// conversation (its <c>MenuStick</c> and <c>Game.ReadInput</c>), with defaults that are the bindings the runtime always had.
/// The debug keys (the F-keys, the free camera and fast-forward) aren't among them.
/// </summary>
public static class GameControls
{
    // The sources the direction controls can take.
    private static readonly string[] Walkable = [InputSource.Wasd, InputSource.Arrows, InputSource.LeftStick, InputSource.RightStick, InputSource.DPad];
    private static readonly string[] Lookable = [.. Walkable, InputSource.Mouse];

    public const string Move = "move", Camera = "camera", CameraHold = "camera_hold", CameraLeft = "camera_left", CameraRight = "camera_right",
        Walk = "walk", Sneak = "sneak", Use = "use", Portraits = "portraits", Pause = "pause";

    public const string MenuCursor = "menu_cursor", MenuAccept = "menu_accept", MenuBack = "menu_back", MenuClose = "menu_close",
        MenuDetails = "menu_details", MenuAppraise = "menu_appraise", MenuMemberLeft = "menu_member_left", MenuMemberRight = "menu_member_right",
        MenuTabPrevious = "menu_tab_previous", MenuTabNext = "menu_tab_next";

    public const string BattleTarget = "battle_target", BattleAttack = "battle_attack", BattleCancel = "battle_cancel", BattleSkip = "battle_skip",
        BattleSpells = "battle_spells", BattleItems = "battle_items", BattleRangedLeft = "battle_ranged_left", BattleRangedRight = "battle_ranged_right",
        BattlePause = "battle_pause";

    private const Situation Walking = Situation.Trek | Situation.Combat;
    private const Situation Battle = Situation.Combat | Situation.Aiming;

    /// <summary>The menus, and cut-scenes and conversations, which the menus' cursor, Accept, Back and Close Menu work too.</summary>
    private const Situation MenusAndTalking = Situation.Menus | Situation.Talking;

    public static readonly IReadOnlyList<GameControl> All =
    [
        // Trek, whose walking and camera a battle's turn shares.
        Sources(Move, "Move", Situation.Trek, Walking | Situation.Aiming, [InputSource.Wasd, InputSource.Arrows], [InputSource.LeftStick], Walkable),
        Sources(Camera, "Camera", Situation.Trek, Walking, [], [InputSource.LeftStick, InputSource.RightStick], Lookable),
        Button(CameraHold, "Camera Hold", Situation.Trek, Walking, null, ["Mouse:Right"], ["Joy:RB"]),
        Button(CameraLeft, "Camera Left", Situation.Trek, Walking, null, ["Key:Q"], []),
        Button(CameraRight, "Camera Right", Situation.Trek, Walking, null, ["Key:E"], []),
        Button(Walk, "Walk", Situation.Trek, Walking, null, ["Key:Shift"], []),
        // Sneak acts in a battle's turn too, as it always has, but isn't checked there: the left trigger is also Skip Turn.
        Button(Sneak, "Sneak", Situation.Trek, Situation.Trek, null, ["Key:Ctrl"], ["Axis:LeftTrigger+"]),
        Button(Use, "Use", Situation.Trek, Situation.Trek, MenuAction.Accept, ["Key:Space"], ["Joy:A"]),
        Button(Portraits, "Portraits", Situation.Trek, Situation.Trek, MenuAction.Back, ["Key:Backspace"], ["Joy:B"]),
        Button(Pause, "Menu", Situation.Trek, Situation.Trek, MenuAction.Start, ["Key:Enter", "Key:Escape"], ["Joy:Start"], essential: true),

        // A conversation's choices, its next line, closing it and skipping a cut-scene are the menus' cursor, Accept, Back and Close Menu.
        Sources(MenuCursor, "Move Cursor", Situation.Menus, MenusAndTalking, [InputSource.Wasd, InputSource.Arrows],
            [InputSource.LeftStick, InputSource.DPad], Walkable, essential: true),
        Button(MenuAccept, "Accept", Situation.Menus, MenusAndTalking, MenuAction.Accept, ["Key:Space"], ["Joy:A"], essential: true),
        Button(MenuBack, "Back", Situation.Menus, MenusAndTalking, MenuAction.Back, ["Key:Backspace"], ["Joy:B"], essential: true),
        Button(MenuClose, "Close Menu", Situation.Menus, MenusAndTalking, MenuAction.Start, ["Key:Enter", "Key:Escape"], ["Joy:Start"]),
        Button(MenuDetails, "Details", Situation.Menus, Situation.Menus, MenuAction.Details, ["Key:Tab"], ["Joy:Y"], release: MenuAction.DetailsRelease),
        Button(MenuAppraise, "Appraise", Situation.Menus, Situation.Menus, MenuAction.Appraise, ["Key:R"], ["Joy:X"], release: MenuAction.AppraiseRelease),
        // C-left shows the next member.
        Button(MenuMemberLeft, "Next Member", Situation.Menus, Situation.Menus, MenuAction.MemberLeft, ["Key:Q"], ["Joy:LB"]),
        Button(MenuMemberRight, "Previous Member", Situation.Menus, Situation.Menus, MenuAction.MemberRight, ["Key:E"], ["Joy:RB"]),
        Button(MenuTabPrevious, "Previous Tab", Situation.Menus, Situation.Menus, MenuAction.TabPrevious, ["Key:Z"], ["Axis:LeftTrigger+"]),
        Button(MenuTabNext, "Next Tab", Situation.Menus, Situation.Menus, MenuAction.TabNext, ["Key:X"], ["Axis:RightTrigger+"]),

        // A battle's own: the stick, the D-pad and the keys choose a target and move a held menu's cursor.
        Sources(BattleTarget, "Target", Situation.Combat, Battle, [InputSource.Wasd, InputSource.Arrows], [InputSource.LeftStick, InputSource.DPad], Walkable),
        Button(BattleAttack, "Attack", Situation.Combat, Battle, MenuAction.Accept, ["Key:Space"], ["Joy:A"]),
        Button(BattleCancel, "Cancel", Situation.Combat, Battle, MenuAction.Back, ["Key:Backspace"], ["Joy:B"]),
        Button(BattleSkip, "Skip Turn", Situation.Combat, Battle, MenuAction.TabPrevious, ["Key:Z"], ["Axis:LeftTrigger+"]),
        Button(BattleSpells, "Spell Menu", Situation.Combat, Situation.Combat, MenuAction.Details, ["Key:Tab"], ["Joy:Y"], release: MenuAction.DetailsRelease),
        Button(BattleItems, "Item Menu", Situation.Combat, Situation.Combat, MenuAction.Appraise, ["Key:R"], ["Joy:X"], release: MenuAction.AppraiseRelease),
        Button(BattleRangedLeft, "Ranged Left", Situation.Combat, Situation.Aiming, MenuAction.MemberLeft, ["Key:Q"], ["Joy:LB"]),
        Button(BattleRangedRight, "Ranged Right", Situation.Combat, Situation.Aiming, MenuAction.MemberRight, ["Key:E"], ["Joy:RB"]),
        Button(BattlePause, "Pause", Situation.Combat, Situation.Combat, MenuAction.Start, ["Key:Enter", "Key:Escape"], ["Joy:Start"], essential: true),
    ];

    private static readonly Dictionary<string, GameControl> ById = All.ToDictionary(c => c.Id);

    /// <summary>The control with an ID, or null.</summary>
    public static GameControl? Find(string id) => ById.GetValueOrDefault(id);

    public static GameControl Get(string id) => ById[id];

    /// <summary>The controls live in a situation.</summary>
    public static IEnumerable<GameControl> In(Situation situation) => All.Where(c => (c.LiveIn & situation) != 0);

    /// <summary>A situation's heading on the Controls page, in Title Case.</summary>
    public static string HeadingLabel(Situation situation) => situation switch
    {
        Situation.Trek => "Trek",
        Situation.Menus => "Menus",
        _ => "Combat",
    };

    private static GameControl Button(string id, string label, Situation heading, Situation live, MenuAction? press, string[] keyboard,
        string[] controller, MenuAction? release = null, bool essential = false) =>
        new(id, label, heading, live, ControlKind.Button, press, release, essential, keyboard, controller);

    private static GameControl Sources(string id, string label, Situation heading, Situation live, string[] keyboard, string[] controller,
        string[] allowed, bool essential = false) =>
        new(id, label, heading, live, ControlKind.Sources, null, null, essential, keyboard, controller, allowed);
}

/// <summary>Which situation the player is in, by what runs.</summary>
public static class Situations
{
    /// <summary>
    /// The situation a mode is: trek, the menus, a battle (whose aiming is a part of it the conflicts are checked apart), or what
    /// reads presses as a conversation does, which the menus' controls work.
    /// </summary>
    public static Situation Of(GameMode mode) => mode switch
    {
        GameMode.Trek => Situation.Trek,
        GameMode.Menu or GameMode.Title => Situation.Menus,
        GameMode.Combat => Situation.Combat | Situation.Aiming,
        _ => Situation.Talking,
    };
}
