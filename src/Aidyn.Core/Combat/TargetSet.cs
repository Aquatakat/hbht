using System.Numerics;

namespace Aidyn.Core.Combat;

/// <summary>
/// A target set (<c>TargetSet</c>, <c>0x80068FE0</c>–<c>0x8006A0B0</c>, a record of 0xA8 bytes): the combatants the current one
/// could act on (the candidates), those it has chosen (the selected), the candidates in the order the cursor steps through
/// them, the cursor and its place in that order, and how many may be chosen. The battle keeps two: A for attacks
/// (<c>Combat.Current</c>'s <c>+0x4E64</c>) and B for spells (<c>+0x4F0C</c>). The battle screen tints the portraits by them
/// (<see cref="Battle.Marks"/>) and the selected bodies' light pulses.
/// </summary>
public sealed class TargetSet
{
    private readonly bool[] _candidate = new bool[Battle.SlotCount];
    private readonly bool[] _selected = new bool[Battle.SlotCount];
    private readonly List<int> _order = [];

    /// <param name="spells">Whether it's set B, for spells (<c>+0xA2</c>, <c>TargetSet.SetMode</c>), or A, for attacks.</param>
    public TargetSet(bool spells) => Spells = spells;

    /// <summary>Whether it holds a spell's targets (set B) rather than an attack's (A) (<c>+0xA2</c>).</summary>
    public bool Spells { get; }

    /// <summary>How many candidates it has (<c>+0xA3</c>).</summary>
    public int Candidates { get; private set; }

    /// <summary>How many are selected (<c>+0xA4</c>).</summary>
    public int Selected { get; private set; }

    /// <summary>How many may be (<c>+0xA5</c>): 1 for an attack.</summary>
    public int Most { get; private set; }

    /// <summary>The slot the cursor is on (<c>+0xA6</c>), the first selected.</summary>
    public int Cursor { get; private set; }

    /// <summary>The cursor's place in <see cref="Order"/> (<c>+0xA7</c>).</summary>
    public int Place { get; private set; }

    /// <summary>The candidates in the order the cursor steps through them (<c>+0x90</c>), as <see cref="BeginAttack"/> left it.</summary>
    public IReadOnlyList<int> Order => _order;

    /// <summary>Whether a slot is a candidate (<c>+0x00</c>).</summary>
    public bool IsCandidate(int slot) => _candidate[slot];

    /// <summary>Whether a slot is selected (<c>+0x48</c>).</summary>
    public bool IsSelected(int slot) => _selected[slot];

    /// <summary>The selected slots, in slot order.</summary>
    public IEnumerable<int> SelectedSlots => Enumerable.Range(0, Battle.SlotCount).Where(i => _selected[i]);

    /// <summary>Empties it (<c>TargetSet.Clear</c>, <c>0x800690AC</c>).</summary>
    public void Clear()
    {
        Array.Clear(_candidate);
        Array.Clear(_selected);
        _order.Clear();
        Candidates = Selected = Most = Cursor = Place = 0;
    }

    /// <summary>Deselects every slot (<c>TargetSet.ClearSelection</c>, <c>0x80069040</c>).</summary>
    public void ClearSelection()
    {
        Array.Clear(_selected);
        Selected = 0;
    }

    /// <summary>Marks a slot as a candidate, and selected, by hand, for tests.</summary>
    internal void Mark(int slot, bool selected)
    {
        if (!_candidate[slot])
            Candidates++;
        _candidate[slot] = true;
        Most = Math.Max(Most, Selected + (selected ? 1 : 0));
        if (selected)
            Select(slot);
    }

    /// <summary>
    /// Takes its candidate flags away and leaves the selected (<c>ConfirmAttackTarget</c>), so the turn to the target sees which
    /// it is and the portraits are no longer tinted as candidates.
    /// </summary>
    internal void ClearCandidates()
    {
        Array.Clear(_candidate);
        Candidates = 0;
    }

    /// <summary>
    /// Selects a candidate while there's room, the first selected becoming the cursor (<c>TargetSet.Select</c>,
    /// <c>0x8006912C</c>). Whether it did.
    /// </summary>
    public bool Select(int slot)
    {
        if (Selected >= Most || !_candidate[slot] || _selected[slot])
            return false;
        if (Selected == 0)
            Cursor = slot;
        _selected[slot] = true;
        Selected++;
        return true;
    }

    /// <summary>Deselects a slot (<c>TargetSet.Deselect</c>, <c>0x8006918C</c>).</summary>
    public bool Deselect(int slot)
    {
        if (!_selected[slot])
            return false;
        _selected[slot] = false;
        Selected--;
        return true;
    }

    /// <summary>
    /// Selects the candidates from the first slot on while there's room, the cursor on the last (<c>TargetSet.SelectAll</c>,
    /// <c>0x800691C8</c>).
    /// </summary>
    private void SelectAll()
    {
        if (Candidates == 0)
            return;
        for (var i = 0; i < Battle.SlotCount && Selected < Most; i++)
        {
            if (!_candidate[i])
                continue;
            _selected[i] = true;
            Cursor = i;
            Selected++;
        }
    }

    /// <summary>
    /// What <paramref name="c"/> could attack (<c>TargetSet.FindAttackTargets</c>, <c>0x80069304</c>): every combatant in play that
    /// <see cref="Battle.CanAttack"/> allows; one may be chosen.
    /// </summary>
    private void FindAttackTargets(Battle battle, Combatant c)
    {
        for (var i = 0; i < battle.InPlay; i++)
        {
            if (battle.Slots[i] is { } t && battle.CanAttack(c, t))
            {
                _candidate[i] = true;
                Candidates++;
            }
        }
        Most = 1;
    }

    /// <summary>
    /// What <paramref name="c"/>'s selected spell could be cast at (<c>TargetSet.FindSpellTargets</c>, <c>0x800693C4</c>): every
    /// combatant in play that <see cref="Battle.CanCast"/> allows; as many may be chosen as the spell takes
    /// (<see cref="Battle.SpellTargetCount"/>), an All spell every candidate.
    /// </summary>
    private void FindSpellTargets(Battle battle, Combatant c)
    {
        for (var i = 0; i < battle.InPlay; i++)
        {
            if (battle.Slots[i] is { } t && battle.CanCast(c, t))
            {
                _candidate[i] = true;
                Candidates++;
            }
        }
        Most = battle.SpellTargetCount(c);
        if (Most == battle.InPlay)
            Most = Candidates;
    }

    /// <summary>
    /// Builds it again for <paramref name="c"/> (<c>TargetSet.Rebuild</c>, <c>0x80069264</c>): emptied, and set A finds what it
    /// could attack if it has an action or is healing, set B what its selected spell could reach if it has one; the first
    /// candidates selected, in slot order; and the cursor kept on its last target, or where it was, if each is still a candidate.
    /// </summary>
    public void Rebuild(Battle battle, Combatant c)
    {
        var old = Cursor;
        Clear();
        if (Spells)
        {
            if (c.Spell.IsNone || c.Controlled && !battle.AiCasts)
                return;
            FindSpellTargets(battle, c);
        }
        else
        {
            if (c.Action == CombatAction.None && !c.Healing)
                return;
            FindAttackTargets(battle, c);
        }
        SelectAll();
        // TargetSet.RestoreCursor (0x8006A05C).
        if (Most < 2 && !SetCursorIfCandidate(c.LastTarget))
            SetCursorIfCandidate(old);
    }

    // 0x8006A0B0: the cursor to a candidate, selecting it.
    private bool SetCursorIfCandidate(int slot)
    {
        if (slot < 0 || slot >= Battle.SlotCount || !_candidate[slot])
            return false;
        if (slot != Cursor)
        {
            Deselect(Cursor);
            Cursor = slot;
            Select(slot);
        }
        return true;
    }

    /// <summary>
    /// The cursor to a candidate, and its place in the order (<c>TargetSet.SetCursorTo</c>, <c>0x80069FAC</c>). Whether it was one.
    /// </summary>
    private bool SetCursorTo(int slot)
    {
        if (slot < 0 || slot >= Battle.SlotCount || !_candidate[slot])
            return false;
        if (_selected[slot])
            return true;
        Deselect(Cursor);
        var place = _order.IndexOf(slot);
        Place = place < 0 ? 0 : place;
        Cursor = slot;
        Select(slot);
        return true;
    }

    /// <summary>
    /// An attack's targets as A is pressed (<c>TargetSet.BeginAttack</c>, <c>0x800694A8</c>): emptied, the candidates found and,
    /// with any, put in the cursor's order (<see cref="OrderByAngle"/>), and the cursor on the last target if it's one, or where the
    /// cursor was. Whether there are any.
    /// </summary>
    public bool BeginAttack(Battle battle, Combatant c)
    {
        var old = Cursor;
        Clear();
        FindAttackTargets(battle, c);
        if (Candidates == 0)
            return false;
        OrderByAngle(battle, c);
        if (!SetCursorTo(c.LastTarget))
            SetCursorTo(old);
        return true;
    }

    /// <summary>
    /// A spell's targets as it's chosen (<c>TargetSet.BeginSpell</c>, <c>0x80069574</c>): emptied and the candidates found; then for a
    /// spell of one target, put in the cursor's order (<see cref="OrderByAngle"/>) with the cursor on the last target if it's one; for
    /// one of several, every candidate if there are no more than it takes, else the nearest it takes (<see cref="SelectNearest"/>),
    /// which the player doesn't choose among. Whether there are any.
    /// </summary>
    public bool BeginSpell(Battle battle, Combatant c)
    {
        Clear();
        FindSpellTargets(battle, c);
        if (Candidates == 0)
            return false;
        if (battle.SpellTargetCount(c) < 2)
        {
            OrderByAngle(battle, c);
            SetCursorTo(c.LastTarget);
        }
        else if (Candidates < Most)
        {
            SelectAll();
        }
        else
        {
            SelectNearest(battle, c);
        }
        return true;
    }

    /// <summary>
    /// Selects the candidates nearest <paramref name="c"/>'s body, as many as may be (<c>TargetSet.SelectNearest</c>,
    /// <c>0x80069D90</c>): by the distance across the ground between the bodies, the original's selection sort putting a later equal
    /// first. The cursor stays where <see cref="Clear"/> left it.
    /// </summary>
    private void SelectNearest(Battle battle, Combatant c)
    {
        var from = new Vector2(c.Body.Sphere.Position.X, c.Body.Sphere.Position.Z);
        var list = new List<(float Distance, int Slot)>();
        for (var i = 0; i < Battle.SlotCount; i++)
        {
            if (_candidate[i] && battle.Slots[i] is { } t)
                list.Add((Vector2.Distance(from, new Vector2(t.Body.Sphere.Position.X, t.Body.Sphere.Position.Z)), i));
        }
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i; j < list.Count; j++)
            {
                if (list[j].Distance <= list[i].Distance)
                    (list[i], list[j]) = (list[j], list[i]);
            }
        }
        foreach (var (_, slot) in list.Take(Most))
        {
            _selected[slot] = true;
            Selected++;
        }
    }

    /// <summary>
    /// Steps a spell's cursor (<c>TargetSet.StepSpellOne</c>, <c>0x800696D4</c>): only for a spell of one target with more than one
    /// candidate. Whether it stepped.
    /// </summary>
    public bool StepSpellOne(Battle battle, Combatant c, int by)
    {
        if (battle.SpellTargetCount(c) >= 2 || Candidates < 2)
            return false;
        MoveCursor(by);
        return true;
    }

    /// <summary>
    /// Steps the cursor <paramref name="by"/> places through the order, round from the end to the start (<c>TargetSet.MoveCursor</c>,
    /// <c>0x80069D00</c>); not with one candidate.
    /// </summary>
    public void MoveCursor(int by)
    {
        if (Candidates == 1 || _order.Count == 0)
            return;
        Deselect(Cursor);
        var place = Place + by;
        if (place < 0)
            place = Candidates - 1;
        else if (place >= Candidates)
            place = 0;
        Cursor = _order[Math.Min(place, _order.Count - 1)];
        Select(Cursor);
        Place = place;
    }

    /// <summary>
    /// Puts the candidates in the cursor's order (<c>TargetSet.OrderByAngle</c>, <c>0x8006975C</c>), by where each body stands
    /// from the attacker's, against the way its body's own field faces, which is opposite to where it looks
    /// (<see cref="Movement.Body.Heading"/>): ahead of that and to the one side, by the sine's size, largest first; then behind
    /// and to the other, smallest first; then ahead and to the other, smallest first; then behind and to the one side, largest
    /// first. The cursor is on the first. With one candidate it's selected alone.
    /// </summary>
    private void OrderByAngle(Battle battle, Combatant c)
    {
        if (Candidates < 2)
        {
            SelectAll();
            return;
        }
        var position = Flat(c.Body.Sphere.Position);
        var aim = position - c.Body.Heading;
        var groups = new List<(float Key, int Slot)>[4];
        for (var g = 0; g < 4; g++)
            groups[g] = [];
        if (_candidate[c.Slot])
            groups[0].Add((0, c.Slot));
        for (var i = 0; i < Battle.SlotCount; i++)
        {
            if (!_candidate[i] || i == c.Slot || battle.Slots[i] is not { } t)
                continue;
            var at = Flat(t.Body.Sphere.Position);
            var cos = Cosine(position, aim, at);
            var sin = SignedSine(position, aim, at);
            var group = (cos <= 0 ? 1 : 0) | (sin <= 0 ? 2 : 0);
            groups[group].Add((sin > 0 ? sin : -sin, i));
        }
        _order.Clear();
        foreach (var g in (ReadOnlySpan<int>)[0, 2, 3, 1])
        {
            var list = groups[g];
            // The original's selection sort, ties going to the later.
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i; j < list.Count; j++)
                {
                    if (g < 2 ? !(list[j].Key < list[i].Key) : !(list[i].Key < list[j].Key))
                        (list[i], list[j]) = (list[j], list[i]);
                }
            }
            _order.AddRange(list.Select(e => e.Slot));
        }
        Deselect(Cursor);
        Place = 0;
        Cursor = _order[0];
        Select(Cursor);

        static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
    }

    /// <summary>The cosine of the angle at <paramref name="p"/> between <paramref name="a"/> and <paramref name="t"/> (<c>Vec2.Cosine</c>, <c>0x800A18F8</c>).</summary>
    private static float Cosine(Vector2 p, Vector2 a, Vector2 t)
    {
        var d = Vector2.Distance(Unit(a - p), Unit(t - p));
        return (2 - d * d) * 0.5f;
    }

    /// <summary>
    /// Its sine, signed by which way round the aim, turned by it, lands nearer the target (<c>Vec2.SignedSine</c>,
    /// <c>0x800A19E0</c>).
    /// </summary>
    private static float SignedSine(Vector2 p, Vector2 a, Vector2 t)
    {
        var u = Unit(a - p);
        var v = Unit(t - p);
        var d = Vector2.Distance(u, v);
        var cos = (2 - d * d) * 0.5f;
        var square = 1 - cos * cos;
        if (!(0 < square))
            square = -square;
        var sin = MathF.Sqrt(square);
        var (u100, v100) = (u * 100, v * 100);
        var plus = Vector2.Distance(Rotate(u100, sin, cos), v100);
        var minus = Vector2.Distance(Rotate(u100, -sin, cos), v100);
        return plus < minus ? sin : -sin;

        static Vector2 Rotate(Vector2 v, float s, float c) => new(c * v.X + s * v.Y, c * v.Y - s * v.X);
    }

    // Vec3.Normalize's length is at least 1e-6.
    private static Vector2 Unit(Vector2 v) => v / MathF.Max(v.Length(), 1e-6f);
}
