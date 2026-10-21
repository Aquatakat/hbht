using System.Numerics;
using Aidyn.Core.Magic;

namespace Aidyn.Core.Combat;

// The AI's casters (styles 5 to 10): each action slot's spell scored from each cell of the field, the best and nearest chosen, and
// Fireball's centre. docs/systems/combat.md's "The AI" has the original's.
public sealed partial class Battle
{
    /// <summary>
    /// The casters' choice, before their weapons' (<c>EnemyAI.ChooseSpellOrAttack</c>, <c>0x800590B0</c>): every action slot's spell
    /// scored from every cell it could cast from (<see cref="ScoreSpellCells"/>); of those tied for the best, the nearest. Null when
    /// none scores, and the style falls back to its weapon.
    /// </summary>
    private AiScores? ChooseSpellOrAttack(EnemyAI ai)
    {
        var scores = ScoreSpellCells(ai);
        if (scores.Count == 0)
            return null;
        scores.Sort(ascending: false, Game.Random.Sort);
        scores.KeepBest();
        ScoreByDistance(ai, scores, crowded: true);
        scores.Sort(ascending: true, Game.Random.Sort);
        return scores;
    }

    /// <summary>The spell an action slot holds, as the entity's selected spell (<c>EnemyAI.SlotIndex</c> and <c>SlotKind</c>, <c>0x800E0B70</c>, <c>0x800E0B78</c>).</summary>
    internal static SpellChoice SlotChoice(int slot) => slot switch
    {
        < 5 => new SpellChoice(SpellSourceKind.Known, slot),
        5 => new SpellChoice(SpellSourceKind.Armor),
        6 => new SpellChoice(SpellSourceKind.Shield),
        _ => new SpellChoice(SpellSourceKind.Weapon),
    };

    /// <summary>
    /// Each action slot's spell scored from each free cell of the combatant's field, as though it stood there
    /// (<c>EnemyAI.ScoreSpellCells</c>, <c>0x800581C8</c>), slot by slot, Z outer and X inner: the AI's set B
    /// (<see cref="_triesB"/>) built for it there, and with a target selected and a value (<see cref="SpellValues.Value"/>), an entry
    /// of the value, ten for each target more than one (or for an area spell those of the other side less those of its own,
    /// <see cref="AreaCentre"/>) and the spell's known level, as a byte, with B's cursor. The combatant's place and selected spell
    /// aren't moved, its footprint only lifted off the grid while it tries the cells. With a field of 0 it scores only where it
    /// stands (<c>EnemyAI.ScoreSpellsInPlace</c>, <c>0x800585B0</c>).
    /// </summary>
    /// <remarks>
    /// The original tries each in the battle's set B, so a try's cursor carries to the next (<see cref="TargetSet.RestoreCursor"/>),
    /// the first from set B as the turn built it, and leaves set B empty after: the AI's set starts from B's cursor, and B is emptied.
    /// It also notes whether the caster can afford any slot, which nothing reads: an unaffordable spell has no candidates
    /// (<see cref="AiCanCast"/>), so no entry.
    /// </remarks>
    private AiScores ScoreSpellCells(EnemyAI ai)
    {
        var c = ai.Combatant;
        var r = c.Radius;
        var inPlace = r == 0;
        var scores = new AiScores(inPlace ? 8 : (r * r * 4 * 8) & 0xFFE0);
        var (cx, cz) = Cell8(c);
        var footprint = c.Footprint;
        _triesB.TakeCursor(TargetsB);
        if (!inPlace)
            Grid.Vacate(cx, cz, footprint);
        for (var i = 0; i < EnemyAI.ActionSlots; i++)
        {
            if (ai.Slots[i] is null || c.SpellOf(SlotChoice(i), Game?.Rules.Magic) is not { } source)
                continue;
            if (inPlace)
            {
                ScoreSpellAt(ai, source, i, cx, cz, c.Cell, scores);
                continue;
            }
            foreach (var (x, z) in Square(cx, cz, r))
            {
                if (ArenaGrid.Distance(x & 0xFF, z & 0xFF, cx, cz) > r || !Grid.IsFree(x, z, footprint))
                    continue;
                ScoreSpellAt(ai, source, i, x, z, new Vector2(x, z), scores);
            }
        }
        if (!inPlace)
            Grid.Occupy(cx, cz, footprint);
        TargetsB.Clear();
        return scores;
    }

    /// <summary>One slot's spell scored with the caster taken to stand at <paramref name="from"/>, the cell (x, z).</summary>
    private void ScoreSpellAt(EnemyAI ai, SpellSource source, int slot, int x, int z, Vector2 from, AiScores scores)
    {
        var c = ai.Combatant;
        var count = IsAreaSpell(source) ? AreaCentre(ai, source, from) : TargetCountScore(c, source, from);
        if (_triesB.Selected == 0)
            return;
        var value = SpellValues.Value(Game.Rules.Magic, source.Spell, c, SpellCandidates(), InPlayCombatants(), PartOfDay);
        if (value == 0)
            return;
        var level = source.Known?.Level ?? source.ItemLevel;
        scores.Add(x, z, slot + 1, (value + count * 10 + level) & 0xFF, _slots[_triesB.Cursor]);
    }

    /// <summary>Whether a spell is one the AI centres on the opponents and the party's area danger counts (its record's <c>area</c>): Fireball (spell kind 8).</summary>
    private static bool IsAreaSpell(SpellSource source) => source.Spell.Area;

    /// <summary>The AI's set B's candidates, by slot.</summary>
    private List<Combatant> SpellCandidates()
    {
        var list = new List<Combatant>();
        for (var k = 0; k < SlotCount; k++)
        {
            if (_triesB.IsCandidate(k) && _slots[k] is { } t)
                list.Add(t);
        }
        return list;
    }

    /// <summary>Every combatant in play.</summary>
    private IEnumerable<Combatant> InPlayCombatants()
    {
        for (var k = 0; k < InPlay; k++)
        {
            if (_slots[k] is { } p)
                yield return p;
        }
    }

    /// <summary>
    /// The AI's set B built for <paramref name="source"/> cast from <paramref name="from"/>, a placed area about the AI's centre; how
    /// many it selected, less one (<c>EnemyAI.TargetCountScore</c>, <c>0x8005818C</c>).
    /// </summary>
    private int TargetCountScore(Combatant c, SpellSource source, Vector2 from)
    {
        _triesB.RebuildSpell(this, c, source, from, AiAreaCentre);
        return _triesB.Selected - 1;
    }

    /// <summary>
    /// Fireball's count (<c>EnemyAI.AreaCentre</c>, <c>0x80058008</c>): the AI's centre put at the opponents' mean place, its set B built
    /// about it for <paramref name="source"/> cast from <paramref name="from"/>, and with one selected, +1 for each candidate of the other
    /// side and −1 for each of the caster's own, itself among them.
    /// </summary>
    private int AreaCentre(EnemyAI ai, SpellSource source, Vector2 from)
    {
        var c = ai.Combatant;
        var (sum, n) = (Vector2.Zero, 0f);
        for (var k = 0; k < InPlay; k++)
        {
            if (IsOpponent(ai, _slots[k]))
            {
                sum += _slots[k]!.Cell;
                n += 1f;
            }
        }
        AiAreaCentre = new Vector2(sum.X / n, sum.Y / n);
        _triesB.RebuildSpell(this, c, source, from, AiAreaCentre);
        if (_triesB.Selected == 0)
            return 0;
        var count = 0;
        for (var k = 0; k < SlotCount; k++)
        {
            if (_triesB.IsCandidate(k) && _slots[k] is { } p)
                count += p.Hostile == c.Hostile ? -1 : 1;
        }
        return count;
    }

    /// <summary>
    /// How far apart the party's flasks and Fireballs make the AI keep (<see cref="Danger"/>): the first of the record's flasks the
    /// party holds (<c>Combat.PartyPotionTier</c>, <c>0x8005C750</c>), raised to twice the casting level of any living member's area
    /// spell, Sholeh's too (<c>Combat.CreatePartyCombatants</c>, <c>0x8005C7C8</c>, and <c>CreateSholeh</c>, <c>0x8005CA60</c>).
    /// </summary>
    private int PartyDanger(Plugins.Records.CombatRecord rules)
    {
        var inventory = Game.Party.Inventory;
        var danger = rules.FlaskDanger?.FirstOrDefault(f => inventory.Has(f.Item))?.Radius ?? 0;
        for (var k = 0; k < PartySize; k++)
        {
            if (_slots[k] is not { } member || member.Sheet.IsDead)
                continue;
            foreach (var known in member.Sheet.Spells)
            {
                if (known.Spell.Area)
                    danger = Math.Max(danger, 2 * Casting.Level(member.Sheet, SpellSource.From(known)));
            }
        }
        return danger;
    }

    /// <summary>
    /// Fireball's centre, before the target sets (<c>EnemyAI.AimAreaSpell</c>, <c>0x8005974C</c>, from <c>EnemyAI.PrepareAction</c>):
    /// for the area spell selected, of the cells on the arena within its range times its casting level of the caster, the first, Z
    /// outer and X inner, about which the AI's set B has the most candidates of the other side less those of its own, more than none,
    /// becomes the AI's centre (<see cref="AiAreaCentre"/>); with none, the cell of the centre <see cref="AreaCentre"/> left. Any other
    /// spell is left as it is.
    /// </summary>
    /// <remarks>
    /// The original tries each centre in the battle's set B, so the last try's cursor is the one set B's rebuilding then keeps
    /// (<see cref="PrepareTargetSets"/>): set B takes the AI's set's cursor after.
    /// </remarks>
    private void AimAreaSpell(EnemyAI ai)
    {
        var c = ai.Combatant;
        if (c.CurrentSpell(this) is not { } source || !IsAreaSpell(source))
            return;
        var r = (source.Spell.Range * Casting.Level(c.Sheet, source)) & 0xFF;
        var (cx, cz) = Cell8(c);
        var best = 0;
        var (bx, bz) = ((byte)(int)AiAreaCentre.X, (byte)(int)AiAreaCentre.Y);
        _triesB.TakeCursor(TargetsB);
        foreach (var (x, z) in Square(cx, cz, r))
        {
            if (Grid.IsOutside(x, z) || ArenaGrid.Distance(cx, cz, x & 0xFF, z & 0xFF) > r)
                continue;
            _triesB.RebuildSpell(this, c, source, c.Cell, new Vector2(x, z));
            if (_triesB.Selected == 0)
                continue;
            var n = 0;
            for (var k = 0; k < SlotCount; k++)
            {
                if (_triesB.IsCandidate(k) && _slots[k] is { } p)
                    n = (sbyte)(n + (p.Hostile != c.Hostile ? 1 : -1));
            }
            if (best < n)
                (best, bx, bz) = (n, (byte)x, (byte)z);
        }
        TargetsB.TakeCursor(_triesB);
        AiAreaCentre = new Vector2(bx, bz);
    }

    /// <summary>
    /// Whether the selected spell is one cast at its own side (<c>Combatant.IsFriendlySpell</c>, <c>0x80061008</c>), whether or not it
    /// casts this turn: then its target's having no Stamina gives its morale nothing.
    /// </summary>
    private bool IsFriendlySpell(Combatant c) => c.CurrentSpell(this)?.Spell.TargetType == Plugins.Records.SpellTargetType.Party;
}
