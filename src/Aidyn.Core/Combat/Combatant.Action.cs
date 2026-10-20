using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>What a combatant does with A (<c>+0x25</c>).</summary>
public enum CombatAction : byte
{
    /// <summary>Nothing: it has no weapon, and can't attack.</summary>
    None = 0,

    /// <summary>A melee attack, at a target within reach.</summary>
    Melee = 1,

    /// <summary>A ranged attack, at a target 3 or more cells away and in range, with no opponent within reach.</summary>
    Ranged = 2,

    /// <summary>A flask thrown or a potion drunk (session 9).</summary>
    Flask = 3,

    /// <summary>A ranged weapon that strikes in melee too: melee within reach, else ranged, out to its range.</summary>
    MeleeOrRanged = 4,
}

/// <summary>
/// A model a combatant's body holds (one of the body's three attachment records, <c>+0x730</c>, 12 bytes each, set by
/// <c>AttachItemToPlayer</c>, <c>0x80014BA0</c>): a scene drawn on a locator of its actor, turned and scaled with it as the actor's
/// animation moves the locator (<c>RenderPlayers</c>, <c>0x80013DCC</c>, through <c>SceneGetLocatorMtx</c>).
/// </summary>
/// <param name="Scene">The scene drawn.</param>
/// <param name="Locator">The locator of the body's actor it's drawn at; none on the actor draws nothing.</param>
/// <param name="Raise">How far it's raised in the world's Y after it's placed: the lute's 1.</param>
public readonly record struct HeldModel(RecordId Scene, int Locator, float Raise = 0);

public sealed partial class Combatant
{
    /// <summary>The weapon's, the arrow's or the flask's attachment slot; the shield's or the bow's; and the lute's.</summary>
    public const int HandSlot = 0, ArmSlot = 1, LuteSlot = 2;

    /// <summary>The locator each slot starts on (<c>Body.AttachmentDefaultLocators</c>, <c>0x800CDC0C</c>): the hand, the shield arm, and the head.</summary>
    private static readonly int[] DefaultLocators = [0, 2, 3];

    /// <summary>The Troubadour's lute (<c>Combatant.StartTroubadour</c>), and how far it's raised (<c>RenderPlayers</c>, slot 2 alone).</summary>
    public static readonly RecordId LuteScene = RecordId.Hex(RecordTypes.Scene, 0x1ABB);

    public const float LuteRaise = 1;

    private readonly HeldModel?[] _held = new HeldModel?[3];

    /// <summary>
    /// What its body holds in each attachment slot (<see cref="HandSlot"/>, <see cref="ArmSlot"/>, <see cref="LuteSlot"/>), which
    /// the runtime draws: in battle alone, as trek's bodies hold nothing but what a script shows.
    /// </summary>
    public IReadOnlyList<HeldModel?> Held => _held;

    /// <summary>
    /// Puts in its body's hands what its action holds (<c>Combatant.EquipVisuals</c>, <c>0x80066AEC</c>), the hand and the arm
    /// emptied first and the lute left be. The arm holds the shield (its armor's model, at locator 2), unless its entity can never
    /// learn the shield skill (<c>Combatant.CannotUseShield</c>, <c>0x80066C18</c>). With no action the hand holds nothing; a flask or
    /// a potion in hand holds the flask; a bow holds its arrow in the hand and the bow itself in the arm in place of the shield, at
    /// locators 0 and 1, or 1 and 0 for <see cref="CombatRecord.OtherHand"/>; any other weapon holds its own model, none for a bite
    /// or a claw. The hand stays empty for <see cref="CombatRecord.UndrawnWeapons"/>, whose models have their weapons.
    /// </summary>
    internal void EquipVisuals(CombatRecord? rules)
    {
        _held[HandSlot] = _held[ArmSlot] = null;
        var handLocator = DefaultLocators[HandSlot];
        var armLocator = DefaultLocators[ArmSlot];
        var arm = Sheet.Record.ShieldSkill is null ? null : Sheet.Equipment.Shield?.Item.Armor?.Model;
        RecordId? hand = null;
        if (Action == CombatAction.Flask)
        {
            hand = Battle.FlaskScene;
        }
        else if (Action != CombatAction.None && Sheet.Equipment.Weapon?.Item.Weapon is { } weapon)
        {
            // Combatant.WeaponIsBowKind (0x80066908): the weapons that fly as the arrow.
            if (rules?.Missiles?.FirstOrDefault(m => m.Weapon == weapon.Id) is { Scene: { } arrow })
            {
                hand = arrow;
                arm = weapon.Model;
                var other = rules.OtherHand?.Contains(Entity) == true;
                armLocator = other ? 0 : 1;
                handLocator = other ? 1 : 0;
            }
            else
            {
                hand = weapon.Model;
            }
        }
        if (arm is { } a)
            _held[ArmSlot] = new HeldModel(a, armLocator);
        if (hand is { } h && rules?.UndrawnWeapons?.Contains(Entity) != true)
            _held[HandSlot] = new HeldModel(h, handLocator);
    }

    /// <summary>The arrow, flask or thrown weapon leaves the hand as it flies (<c>Combat.StartCastCamera</c>, <c>0x80023434</c>).</summary>
    internal void EmptyHand() => _held[HandSlot] = null;

    /// <summary>The lute on the body as it performs (<c>Combatant.StartTroubadour</c>), or taken off (<c>StopPerforming</c>).</summary>
    internal void HoldLute(bool held) =>
        _held[LuteSlot] = held ? new HeldModel(LuteScene, DefaultLocators[LuteSlot], LuteRaise) : null;

    /// <summary>What A does (<c>+0x25</c>), which its weapon sets (<see cref="SetDefaultAction"/>).</summary>
    public CombatAction Action { get; internal set; }

    /// <summary>
    /// Whether it aims freely rather than at a target in reach (<c>+0x8A</c>, <c>Combatant.UpdateDrawnFlag</c>, <c>0x80066A2C</c>):
    /// a bow or a thrown weapon that flies (<see cref="CombatRecord.Missiles"/>), or a flask. Its A goes to state 18, and its
    /// attack flies to the target.
    /// </summary>
    public bool Drawn { get; internal set; }

    /// <summary>Whether it's healing, by Lay On Hands or Heal With Herbs (flags 8 and 9), which session 9 sets.</summary>
    public bool Healing => (Flags & (CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs)) != 0;

    /// <summary>
    /// The damage an attack on it has done and is yet to show (<c>+0x29</c>, a byte): the action stores it, and the hit takes it
    /// as the attacker's swing ends (<c>Combat.ApplyHit</c>).
    /// </summary>
    public int Damage { get; internal set; }

    /// <summary>The healing done to it and yet to show (<c>+0x2A</c>), session 9's.</summary>
    public int Healed { get; internal set; }

    /// <summary>
    /// Whether its body has gone from the field (<c>Combat.FreeBody</c>, <c>0x80021C6C</c>, which empties its place in
    /// <c>Combat.Bodies</c>): dead, once its fall has played, or fled. The combatant stays in its slot until a reserve takes it.
    /// </summary>
    public bool Freed { get; internal set; }

    /// <summary>
    /// The target mark's pulse on its body (<c>CombatTargetMarks.Draw</c>, <c>0x80089830</c>), 0–1, while it's selected in the
    /// target set the state shows; null when it isn't. Its light goes toward this grey, once a second (<c>Body.SetTint</c>).
    /// </summary>
    public float? Mark { get; internal set; }

    /// <summary>Ticks it has been marked, for <see cref="Mark"/>'s pulse.</summary>
    internal int MarkTicks { get; set; }

    /// <summary>Its body's tint (<c>+0x75C</c>), which the target mark pulses and a cast glows: its ambient light, and the moon's on it.</summary>
    public BodyTint Tint { get; } = new();

    /// <summary>A cast's glow has turned its body's fog off (its scene's flag <c>0x100</c> cleared, <c>CombatSpellFx.GlowBody</c>).</summary>
    public bool Unfogged { get; internal set; }

    /// <summary>
    /// A web or hold keeps its body's idle pose (body <c>+0x70E</c>, <c>CombatSpellFx.HoldBody</c>): while it's asked for nothing, its
    /// animation doesn't step (<c>Body.IsHeld</c>, <c>0x80012754</c>).
    /// </summary>
    public bool PoseHeld { get; internal set; }

    /// <summary>
    /// The ticks left of a fled body's fade (<c>Body.FadeOut</c>, <c>0x80014AD4</c>, its <c>+0x708</c>): it's gone from the battle,
    /// but its body is drawn fading where it stood until this reaches 0.
    /// </summary>
    public int Fading { get; internal set; }

    /// <summary>How opaque the body is drawn, 1 until it's freed, then fading out over <see cref="Battle.FadeTicks"/>.</summary>
    public float Opacity => Freed ? (float)Fading / Battle.FadeTicks : 1;

    /// <summary>
    /// Whether its body has played its fall to the end and holds its last frame (the body's <c>+0x77C</c>): its animation no
    /// longer steps.
    /// </summary>
    internal bool Fallen { get; set; }

    /// <summary>
    /// The one-shot request its body has been asked for, and for how many ticks (<see cref="Battle.AfterBodies"/>): the original asks
    /// for one through a whole frame's ticks and lets it go as the frame ends (<c>Combat.AfterFrame</c>).
    /// </summary>
    internal (int Request, int Ticks) OneShot { get; set; }

    /// <summary>
    /// What A does with its weapon (<c>Combatant.SetDefaultAction</c>, <c>0x800672D4</c>): nothing without one; melee with no range;
    /// with a range, ranged, or melee or ranged for a weapon that may strike in melee too (<c>Weapon.AllowsMelee</c>,
    /// <c>0x8007B730</c>: a Breath or a Spit, or one of <see cref="CombatRecord.CloseRange"/>). Whether it aims freely too.
    /// </summary>
    internal void SetDefaultAction(CombatRecord? rules)
    {
        var item = Sheet.Equipment.Weapon;
        Drawn = false;
        if (item?.Item.Weapon is not { } weapon)
        {
            Action = CombatAction.None;
            EquipVisuals(rules);
            return;
        }
        if (weapon.Range == 0)
            Action = CombatAction.Melee;
        else if (weapon.Class is WeaponClass.Breath or WeaponClass.Spit || rules?.CloseRange?.Contains(weapon.Id) == true)
            Action = CombatAction.MeleeOrRanged;
        else
            Action = CombatAction.Ranged;
        Drawn = rules?.Missiles?.Any(m => m.Weapon == weapon.Id) == true;
        EquipVisuals(rules);
    }
}
