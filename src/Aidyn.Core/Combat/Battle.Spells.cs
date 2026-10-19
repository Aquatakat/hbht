using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// Spells in a turn (states 22, 23, 10 and 11): the held menus, target set B's rules, aiming a spell and placing an area, and the cast
// carried out at each target through the action's states. docs/systems/combat.md's "Spells, potions and skills in a turn" has the
// original's.
public sealed partial class Battle
{
    /// <summary>The ticks the cast's animation plays as (the caster's request 5), which state 2 waits for as it waits for a swing.</summary>
    public const int CastRequest = 5;

    /// <summary>How far a placed area's centre moves a frame at the stick's full tilt (<c>Combat.MoveAreaCursor</c>, <c>0x8008465C</c>).</summary>
    public const float AreaStep = 0.8f;

    /// <summary>How high above the floor under a placed area's centre its line of sight starts (<c>0x800D28A0</c>).</summary>
    public const float SightAboveFloor = 2.0f;

    private const int SpellRequirementLine = 0xE0;

    /// <summary>The Common strings' line of the requirement refusals (<c>Spell.CastMessages</c>, <c>0x800E2C00</c>, lines 258–270).</summary>
    public const int CastMessagesLine = 258;

    /// <summary>The held menu that's open (states 22 and 23), or null.</summary>
    public CombatMenu? Menu { get; private set; }

    // The rows the menus last ended on, by party slot (CombatMenu.SpellCursors, 0x800E3180, and ItemCursors, 0x800E3190); -1 for none.
    private readonly int[] _spellCursors = [-1, -1, -1, -1], _itemCursors = [-1, -1, -1, -1];

    /// <summary>What the spells' visuals and sounds are asked for, oldest first, for session 10's to play; the runtime drains it.</summary>
    public Queue<SpellVisual> SpellVisuals { get; } = new();

    // ---- Target set B's rules ----

    /// <summary>
    /// How many targets <paramref name="c"/>'s selected spell takes (<c>Combatant.SpellTargetCount</c>, <c>0x80060818</c>): every
    /// combatant in play for an All spell, half the cast's level rounded up for a ByLevel one, else one; none without a spell.
    /// </summary>
    public int SpellTargetCount(Combatant c)
    {
        if (c.CurrentSpell(this) is not { } source)
            return 0;
        return source.Spell.TargetCount switch
        {
            Plugins.Records.SpellTargetCount.All => InPlay,
            Plugins.Records.SpellTargetCount.ByLevel => (byte)((Casting.Level(c.Sheet, source) + 1) >> 1),
            _ => 1,
        };
    }

    /// <summary>
    /// Whether <paramref name="c"/>'s selected spell may be cast at <paramref name="t"/> (<c>Combatant.CanCast</c>, <c>0x80060C38</c>):
    /// never by or at the Shadow; the spell may take it (<see cref="Casting.CanTarget"/>, with its requirement met); the AI's own tests
    /// for an AI's; Teleportation and a spell for the caster alone only the caster; then within its reach (the level times its range,
    /// from the caster's place, or for a placed area the level from the placed point, <see cref="Focus"/>), but Sense Aura at any
    /// distance; and a clear walk over the grid, the same cell, or a line of sight from 2 above the floor under the cell it's cast from
    /// to the target's head, which only the target's body doesn't block.
    /// </summary>
    public bool CanCast(Combatant c, Combatant t)
    {
        if (_rules?.Flee is { } shadows && (shadows.Contains(t.Entity) || shadows.Contains(c.Entity)))
            return false;
        if (c.CurrentSpell(this) is not { } source)
            return false;
        var spell = source.Spell;
        if (!Casting.CanTarget(c, spell, t) || Casting.Requirement(spell, t.Sheet) != CastRefusal.None)
            return false;
        if (c.Controlled && !AiCanCast(c, source, t))
            return false;
        var effects = spell.Effects ?? [];
        if (effects.Any(e => e is TeleportEffect) || spell.TargetCount == Plugins.Records.SpellTargetCount.Self)
            return t == c;
        var level = Casting.Level(c.Sheet, source);
        var reach = (float)(level * spell.Range);
        var centre = c.Cell;
        var (cellX, cellZ) = (ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y));
        if (spell.Placed)
        {
            centre = Focus;
            (cellX, cellZ) = (Round(Focus.X), Round(Focus.Y));
            reach = level;
        }
        if (!effects.Any(e => e is SenseAuraEffect) && !InSpellRange(t, reach, centre))
            return false;
        var (tx, tz) = (ArenaGrid.CellOf(t.Cell.X), ArenaGrid.CellOf(t.Cell.Y));
        if (Grid.WalkClear(cellX, cellZ, tx, tz) || (cellX, cellZ) == (tx, tz))
            return true;
        if (t.Freed)
            return false;
        var top = Origin + new Vector3(cellX, 50, cellZ);
        var start = SpherePhysics.Sweep(World, top, top with { Y = Origin.Y - 10 }, 0.5f, skipSeeThrough: false, out var floor, out _)
            ? floor with { Y = floor.Y + SightAboveFloor }
            : top with { Y = Origin.Y + 10 };
        return HasSight(start, Head(t), t.Slot, t.Slot);

        // 0x800D2878: rounded half away from zero, as a byte.
        static int Round(float v) => (byte)(v >= 0 ? (int)(v + 0.5f) : -(int)(0.5f - v));
    }

    /// <summary>
    /// Whether <paramref name="t"/>'s body is within <paramref name="reach"/> of <paramref name="centre"/> across the ground, its
    /// scaled radius added (<c>Combatant.InSpellRange</c>, <c>0x80060B60</c>).
    /// </summary>
    private bool InSpellRange(Combatant t, float reach, Vector2 centre)
    {
        if (t.Freed)
            return false;
        var at = t.Body.Sphere.Position - Origin;
        return Vector2.Distance(centre, new Vector2(at.X, at.Z)) <= reach + t.Body.ScaledRadius;
    }

    /// <summary>
    /// The AI's own tests of a spell it might cast (<c>Combatant.AICanCast</c>): the Stamina for a known spell's cost, a charge left
    /// on an item's, and the part of the day. Session 11's casters read it.
    /// </summary>
    private bool AiCanCast(Combatant c, SpellSource source, Combatant t)
    {
        if (source.IsItem)
            return source.Item!.Charges is > 0;
        if (Casting.Cost(source.Spell, Casting.Level(c.Sheet, source)) > c.Sheet.Stats.Modified(Stat.Stamina))
            return false;
        return Casting.TimeAllows(source.Spell, PartOfDay);
    }

    // ---- The held menus ----

    /// <summary>Whether a held menu may open from the state (<c>CombatMenu.Input</c>, <c>0x80087C8C</c>).</summary>
    private bool MenuMayOpen => State is CombatState.Turn or CombatState.TargetAttack or CombatState.TargetSpell or CombatState.PlaceArea
        or CombatState.TargetThrown or CombatState.TroubadourPrompt;

    /// <summary>
    /// C-up (<paramref name="items"/>) or C-down pressed in a turn the player plays, or while it aims (<c>CombatMenu.OpenItems</c>,
    /// <c>0x80087F2C</c>, and <c>OpenSpells</c>, <c>0x80088028</c>): an aim is put away (<c>Combat.CancelToTurn</c>), the body stands,
    /// and the menu opens on the row it last ended on for this member, state 23 or 22. Whether it opened.
    /// </summary>
    public bool OpenMenu(bool items)
    {
        if (Fade.Fading || Lines.Hold is not null || Current is not { Controlled: false } c || !MenuMayOpen || Menu is not null)
            return false;
        CancelToTurn(c);
        c.Body.Request = Body.Idle;
        var slot = c.PartySlot(Game.Party);
        var cursors = items ? _itemCursors : _spellCursors;
        var remembered = slot >= 0 ? cursors[slot] : -1;
        var text = _rules?.Text;
        Menu = items
            ? new CombatMenu(true, ItemRows(c), text?.NoItems, remembered)
            : new CombatMenu(false, SpellRows(c), text?.NoSpells, remembered);
        State = items ? CombatState.MenuItems : CombatState.MenuSpells;
        return true;
    }

    /// <summary>The stick or the D-pad, up (−1) or down (+1), while a held menu is open: its cursor moves a row, round the ends.</summary>
    public void MoveMenu(int by)
    {
        if (State is CombatState.MenuSpells or CombatState.MenuItems)
            Menu?.Move(by);
    }

    /// <summary>
    /// The held button let go (<c>CombatMenu.Close</c>, <c>0x80088118</c>): back to the turn (state 1), the row it ended on remembered
    /// for the member, and that row chosen (the menu's destructor calls its callback). An empty menu chooses nothing. Whether one closed.
    /// </summary>
    public bool CloseMenu(bool items)
    {
        if (Menu is not { } menu || menu.Items != items || Current is not { } c)
            return false;
        State = CombatState.Turn;
        Menu = null;
        if (menu.Current is not { } row)
            return true;
        var slot = c.PartySlot(Game.Party);
        if (slot >= 0)
            (items ? _itemCursors : _spellCursors)[slot] = menu.Cursor;
        Choose(c, row);
        return true;
    }

    /// <summary>A tick of the open menu (<c>CombatMenu.Step</c>, states 22 and 23): its pulse and scroll.</summary>
    private void StepMenu() => Menu?.Tick();

    /// <summary>
    /// C-down's rows (<c>CombatMenu.BuildSpellsMenu</c>, <c>0x80088B58</c>): Lay On Hands and Heal With Herbs for a Healer with the
    /// Stamina (and a herb), Perform for a Troubadour, then the spells it knows by name (the C library's ordering, capitals first) whose
    /// reagent the party has, but a spell that heals only by day while the battle's part of the day is night.
    /// </summary>
    public IReadOnlyList<CombatMenuRow> SpellRows(Combatant c)
    {
        var rows = new List<CombatMenuRow>();
        var sheet = c.Sheet;
        var healer = sheet.Skills.Modified(Skill.Healer);
        var stamina = sheet.Stats.Modified(Stat.Stamina);
        var text = _rules?.Text;
        // Entity.CanLayOnHands (0x80071ED0) and CanHealWithHerbs (0x80071F38).
        if (healer != 0 && stamina >= Math.Max(1, 11 - healer))
            rows.Add(new(CombatMenuChoice.LayOnHands, text?.LayOnHands ?? "", CombatMenu.HealerIcon, CombatMenu.SkillColor));
        var herb = Game.Party.Inventory.Catalog.Rules.Herb;
        if (healer != 0 && Game.Party.Inventory.Has(herb) && stamina >= Math.Max(1, 5 - healer))
            rows.Add(new(CombatMenuChoice.HealWithHerbs, text?.HealWithHerbs ?? "", CombatMenu.HealerIcon, CombatMenu.SkillColor));
        if (sheet.Skills.Modified(Skill.Troubadour) != 0)
            rows.Add(new(CombatMenuChoice.Perform, text?.Perform ?? "", CombatMenu.PerformIcon, CombatMenu.SkillColor));
        var magic = Game.Rules.Magic;
        var known = new List<(string Name, int Index, SpellRecord Spell)>();
        for (var i = 0; i < sheet.Spells.Count; i++)
        {
            var spell = sheet.Spells[i].Spell;
            if (magic?.ReagentItem(spell.Reagent) is { } reagent && !Game.Party.Inventory.Has(reagent))
                continue;
            if (PartOfDay == Calendar.PartOfDay.Night && spell.Effects?.Any(e => e is HealPerRoundEffect { DayOnly: true }) == true)
                continue;
            known.Add((spell.Name, i, spell));
        }
        foreach (var (name, index, spell) in known.OrderBy(k => k.Name, StringComparer.Ordinal))
            rows.Add(new(CombatMenuChoice.Spell, name, CombatMenu.SchoolIcon(spell.School), CombatMenu.SpellColor, new SpellChoice(SpellSourceKind.Known, index)));
        return rows;
    }

    /// <summary>
    /// C-up's rows (<c>CombatMenu.BuildItemsMenu</c>, <c>0x800891B4</c>): a row for each kind of potion the party holds that would do
    /// something now (<see cref="Potions.CanDrink"/>), in the kinds' order, never a flask or Stealth; then the spells of the weapon, the
    /// shield, the body armor and the gear by slot that have charges left, each with its item's image.
    /// </summary>
    public IReadOnlyList<CombatMenuRow> ItemRows(Combatant c)
    {
        var rows = new List<CombatMenuRow>();
        var inventory = Game.Party.Inventory;
        foreach (var potion in Enum.GetValues<Potion>())
        {
            if (Potions.IsFlask(potion) || potion == Potion.Stealth || !inventory.Has(Potions.Id(potion)))
                continue;
            var item = inventory.Catalog[Potions.Id(potion)];
            if (Potions.CanDrink(c.Sheet, item) == DrinkResult.Drunk)
                rows.Add(new(CombatMenuChoice.Potion, item.Name, CombatMenu.PotionIcon, CombatMenu.PotionColor, Potion: potion));
        }
        var equipment = c.Sheet.Equipment;
        AddItemSpell(equipment.Weapon, new SpellChoice(SpellSourceKind.Weapon));
        AddItemSpell(equipment.Shield, new SpellChoice(SpellSourceKind.Shield));
        AddItemSpell(equipment.Armor, new SpellChoice(SpellSourceKind.Armor));
        for (var i = 0; i < equipment.Gear.Count; i++)
            AddItemSpell(equipment.Gear[i], new SpellChoice(SpellSourceKind.Gear, i));
        return rows;

        // CombatMenu.AddItemSpellRow (0x80089678): an item whose spell has charges left (CombatMenu.HasCharges, 0x800885E0).
        void AddItemSpell(ItemInstance? item, SpellChoice choice)
        {
            if (item?.Item.Bonuses.Spell is not { } spell || item.Charges is not > 0 || Game.Rules.Magic?.Spell(spell.Spell) is not { } record)
                return;
            rows.Add(new(CombatMenuChoice.ItemSpell, record.Name, item.Item.Icon, CombatMenu.ItemSpellColor, choice));
        }
    }

    /// <summary>What choosing a row does (its callback, <c>CombatMenu.PickSpell</c> and the rest).</summary>
    private void Choose(Combatant c, CombatMenuRow row)
    {
        switch (row.Choice)
        {
            case CombatMenuChoice.Spell or CombatMenuChoice.ItemSpell:
                c.Spell = row.Spell;
                BeginSpellTargeting(c);
                break;
            case CombatMenuChoice.Potion when row.Potion is { } potion:
                // CombatMenu.PickPotion (0x80089114): the potion in hand, and the drinker alone to confirm (TargetSet.BeginAttackFocused).
                c.HoldPotion(potion, this);
                State = CombatState.TargetAttack;
                TargetsA.BeginAttack(this, c);
                Focus = _slots[TargetsA.Cursor]?.Cell ?? c.Cell;
                CameraOwner = _slots[TargetsA.Cursor] ?? c;
                break;
            case CombatMenuChoice.LayOnHands or CombatMenuChoice.HealWithHerbs:
                // Combatant.SetHealingMode: the mode, a performance stopped, set A built for it; then the ally to heal is aimed at.
                c.Flags &= ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
                c.Flags |= row.Choice == CombatMenuChoice.LayOnHands ? CombatantFlags.LayOnHands : CombatantFlags.HealWithHerbs;
                if (c.Performing)
                    c.StopPerforming(this);
                State = CombatState.TargetAttack;
                TargetsA.BeginAttack(this, c);
                CameraOwner = TargetsA.Candidates > 0 ? _slots[TargetsA.Cursor] : c;
                break;
            case CombatMenuChoice.Perform:
                // The Troubadour's prompt: set A holds the caster alone, and A starts the performance.
                State = CombatState.TroubadourPrompt;
                TargetsA.Rebuild(this, c);
                break;
        }
    }

    /// <summary>
    /// Puts an aim away as a menu opens (<c>Combat.CancelToTurn</c>, <c>0x80083C2C</c>): from any state but a turn's, back to the turn,
    /// and from an attack's or a potion's aim the action put back too.
    /// </summary>
    private void CancelToTurn(Combatant c)
    {
        if (State == CombatState.Turn)
            return;
        var wasAiming = State == CombatState.TargetAttack;
        ReturnToTurn(c);
        if (wasAiming)
            c.RevertAction(Game.Party.Inventory);
    }

    /// <summary>
    /// A spell chosen (<c>Combat.BeginSpellTargeting</c>, <c>0x80083ECC</c>): a placed area's centre is placed (state 11), from where
    /// the caster stands; any other spell aims at its targets (state 10, <see cref="TargetSet.BeginSpell"/>), the camera on the first of
    /// the cursor's order. Not for one the AI plays, which casts without the menus (<see cref="AimSpell"/>).
    /// </summary>
    private void BeginSpellTargeting(Combatant c)
    {
        if (c.Controlled || c.CurrentSpell(this) is not { } source)
            return;
        if (source.Spell.Placed)
        {
            State = CombatState.PlaceArea;
            Focus = c.Cell;
            TargetsB.Rebuild(this, c);
            return;
        }
        State = CombatState.TargetSpell;
        TargetsB.BeginSpell(this, c);
        if (TargetsB.Order.Count > 0)
            CameraOwner = _slots[TargetsB.Order[0]];
    }

    // ---- States 10 and 11 ----

    /// <summary>
    /// A in state 10 (<c>Combat.ConfirmSpellTarget</c>, <c>0x80083F58</c>) or 11 (<c>ConfirmAreaTarget</c>, <c>0x80084184</c>): set A
    /// is emptied and B keeps its selection; the last selected is the first target, or with none the aim is put away; Teleportation's
    /// point must be clear of bodies, or nothing happens; the camera goes back to the caster, the cast is chosen (its margin 0) and the
    /// caster turns to its first target (state 6).
    /// </summary>
    private bool ConfirmSpell(Combatant c)
    {
        var placed = State == CombatState.PlaceArea;
        TargetsA.Clear();
        TargetsB.ClearCandidates();
        if (TargetsB.Selected == 0)
        {
            ReturnToTurn(c);
            return true;
        }
        Target = TargetsB.SelectedSlots.Last();
        if (placed && c.CurrentSpell(this)?.Spell.Effects?.Any(e => e is TeleportEffect) == true && !SpotIsClear(c, Focus))
            return false;
        CameraOwner = c;
        _aimTurn = null;
        c.CastMargin = 0;
        if (_slots[Target] is { } t && t != c)
            FaceCombatant(c, t);
        else
            State = CombatState.Facing;
        return true;
    }

    /// <summary>
    /// Whether no living body's centre is within its own scaled radius and <paramref name="c"/>'s of a point (<c>Combat.SpotIsClear</c>,
    /// <c>0x8005D474</c>), for Teleportation.
    /// </summary>
    private bool SpotIsClear(Combatant c, Vector2 point)
    {
        foreach (var other in Combatants)
        {
            if (other == c || other.Freed || other.Sheet.IsDead)
                continue;
            var at = other.Body.Sphere.Position - Origin;
            if (Vector2.Distance(point, new Vector2(at.X, at.Z)) <= other.Body.ScaledRadius + c.Look.Radius * c.Look.Scale)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A tick of state 11's stick (<c>Combat.InputAreaTarget</c>, <c>0x80084964</c>): the centre moves with it, turned by the camera,
    /// <see cref="AreaStep"/> a frame, kept on the grid and within the spell's range times its level of the caster's body; and set B is
    /// built again about it.
    /// </summary>
    private void MoveArea(PlayerInput input)
    {
        if (Current is not { } c || input.Stick == Vector2.Zero || c.CurrentSpell(this) is not { } source)
            return;
        var look = Camera.LookXZ;
        var way = Facing.Rotate(new Vector2(-input.Stick.X, input.Stick.Y), look.X, look.Y);
        if (way.LengthSquared() > 0)
            way = Vector2.Normalize(way);
        var centre = Focus + way * Cadence.PerTick(AreaStep);
        centre = Vector2.Clamp(centre, Vector2.Zero, new Vector2(Grid.Width, Grid.Depth));
        if (centre == Focus)
            return;
        var at = c.Body.Sphere.Position - Origin;
        var body = new Vector2(at.X, at.Z);
        var off = centre - body;
        var most = source.Spell.Range * Casting.Level(c.Sheet, source);
        if (off.LengthSquared() > 0)
            centre = body + Vector2.Normalize(off) * MathF.Min(off.Length(), most);
        Focus = centre;
        TargetsB.Rebuild(this, c);
    }

    /// <summary>
    /// What marks an aim on the ground (<c>Combat.DrawTargetCounts</c>, <c>0x80021448</c>, every frame): a translucent cylinder at the
    /// focus, red with the spell's reach about the caster in state 10, red with the level about the placed point in 11 (1 for
    /// Teleportation), green of a unit at a flask's aim in 18; none otherwise. The runtime draws scene <c>0x0797</c> there.
    /// </summary>
    public AimMarker? Aim
    {
        get
        {
            if (Current is not { } c)
                return null;
            switch (State)
            {
                case CombatState.TargetThrown:
                    return new AimMarker(Focus, 1, Spell: false);
                case CombatState.TargetSpell or CombatState.PlaceArea when c.CurrentSpell(this) is { } source:
                    var level = Casting.Level(c.Sheet, source);
                    if (State == CombatState.TargetSpell)
                        return new AimMarker(Focus, source.Spell.Range * level, Spell: true);
                    return new AimMarker(Focus, source.Spell.Effects?.Any(e => e is TeleportEffect) == true ? 1 : level, Spell: true);
                default:
                    return null;
            }
        }
    }

    // ---- The cast ----

    /// <summary>
    /// The AI's cast, without the menus (session 11's): <paramref name="c"/>'s selected spell at <paramref name="target"/>, set B built
    /// for it with the target selected first, and the caster turned to it (state 6), from which the action carries it out as the
    /// player's does. A placed area's centre is <paramref name="placed"/>. Whether it may.
    /// </summary>
    public bool AimSpell(Combatant c, Combatant target, Vector2? placed = null)
    {
        if (placed is { } point)
            Focus = point;
        TargetsB.Rebuild(this, c);
        if (!TargetsB.IsCandidate(target.Slot))
            return false;
        if (!TargetsB.IsSelected(target.Slot))
        {
            if (TargetsB.Selected >= TargetsB.Most && TargetsB.SelectedSlots.FirstOrDefault(s => s != target.Slot) is var other)
                TargetsB.Deselect(other);
            TargetsB.Select(target.Slot);
        }
        TargetsA.Clear();
        TargetsB.ClearCandidates();
        Target = target.Slot;
        c.CastMargin = 0;
        if (target != c)
            FaceCombatant(c, target);
        else
            State = NextState = CombatState.Facing;
        return true;
    }

    /// <summary>
    /// The cast at one target (<c>Combatant.CastCurrentSpell</c>, <c>0x80065C08</c>): the selected spell through
    /// <see cref="Cast"/>, the first target preparing it and the rest sharing its roll; a damage result of 0 says the target resisted;
    /// the caster's body plays the cast (request 5) and the visual is asked for (<see cref="SpellVisuals"/>), even for a refused cast.
    /// The damage, for the action to store on the target.
    /// </summary>
    private int CastCurrentSpell(Combatant a, Combatant t)
    {
        if (a.CurrentSpell(this) is not { } source)
            return 0;
        ReachMagic();
        var prepared = a.Flags.HasFlag(CombatantFlags.Acted);
        var castMargin = prepared ? a.CastMargin : -1;
        var outcome = CastAt(a, source, t, ref prepared, ref castMargin);
        a.CastMargin = outcome.Result == CastResult.NotCast && outcome.Refusal is not (CastRefusal.Failed or CastRefusal.NeedsTrueName
            or CastRefusal.TooLarge or CastRefusal.NeedsSolar or CastRefusal.NeedsLunar or CastRefusal.WrongTime) ? -2 : castMargin;
        if (outcome.Refusal is CastRefusal.NoReagent or CastRefusal.NoStamina or CastRefusal.NoCharges)
        {
            // Combatant.FailSpell (0x800636A4): the spell is no longer selected, and set B is emptied.
            a.Spell = SpellChoice.None;
            TargetsB.Clear();
        }
        var damage = outcome.Result == CastResult.Damage ? outcome.Damage : 0;
        if (outcome.Result == CastResult.Damage && damage == 0)
        {
            // Combatant.LogResisted (0x8006382C).
            Say(CastsLine, a.Sheet.Name, source.Spell.Name);
            SayQueued(ResistsLine, t.Sheet.Name);
        }
        a.Body.Request = CastRequest;
        SpellVisuals.Enqueue(new SpellVisual(a, t, source.Spell, outcome.Result switch
        {
            CastResult.NotCast => SpellVisualKind.Failed,
            CastResult.Resisted => SpellVisualKind.Resisted,
            _ => SpellVisualKind.Cast,
        }));
        return damage;
    }

    /// <summary>
    /// The next of a spell's targets, as one is done with (<c>Combat.RemoveFallen</c>'s end): the first still selected, by slot, in set A
    /// or B; the caster turns to it (state 6). Whether there was one.
    /// </summary>
    private bool NextTarget(Combatant c)
    {
        var next = TargetsA.Selected > 0 ? TargetsA.SelectedSlots.First() : TargetsB.Selected > 0 ? TargetsB.SelectedSlots.First() : -1;
        if (next < 0 || _slots[next] is not { } t)
            return false;
        Target = next;
        if (t == c)
        {
            State = CombatState.Facing;
        }
        else
        {
            // Combatant.SetFacingToward and FaceAndSettle.
            FaceCombatant(c, t);
        }
        return true;
    }

    // Shows a line and queues it as the next, as the original's failure lines do (ShowLog, then QueueLog).
    private void SayBoth(int offset, params string[] values)
    {
        Say(offset, values);
        Lines.Queue(Lines.Shown);
    }

    // Queues a line behind the one shown (QueueLog), noting it in the log.
    private void SayQueued(int offset, params string[] values)
    {
        var line = Format(CombatText(offset), values);
        _log.Add(line);
        Lines.Queue(line);
    }

    private static string Format(string line, string[] values)
    {
        foreach (var value in values)
        {
            var at = line.IndexOf("%s", StringComparison.Ordinal);
            if (at < 0)
                break;
            line = string.Concat(line.AsSpan(0, at), value, line.AsSpan(at + 2));
        }
        return line;
    }

    /// <summary>A line of the Common strings (String/Common).</summary>
    private string CommonText(int line) =>
        Game.Plugins.All<Plugins.Records.StringsRecord>(Plugins.RecordTypes.Strings).FirstOrDefault(s => s.Name == "Common")?.Lines.ElementAtOrDefault(line) ?? "";
}

/// <summary>What marks an aim on the ground: the cylinder's centre in the arena's cells, its radius, and whether it's a spell's (red) or a flask's (green).</summary>
public readonly record struct AimMarker(Vector2 Centre, float Radius, bool Spell);

/// <summary>How a spell's visual plays (<c>CombatSpellFx.Start</c>'s result): cast, resisted at the target, or failed at the caster.</summary>
public enum SpellVisualKind
{
    Cast,
    Resisted,
    Failed,
}

/// <summary>A spell's visual and sound to play (<c>CombatSpellFx.Start</c>, <c>0x8008C43C</c>, and <c>Player.SetCastSound</c>), for session 10.</summary>
public sealed record SpellVisual(Combatant Caster, Combatant Target, SpellRecord Spell, SpellVisualKind Kind);
