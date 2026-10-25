using System.Numerics;
using HBHT.Core.Characters;

namespace HBHT.Core.Combat;

// The AI's attack styles (EnemyAI.StyleTable, 0x800E0B80) and what they score with: the opponents, the cells it could attack from,
// and how good each is. docs/systems/combat.md's "The AI" has the original's.
public sealed partial class Battle
{
    /// <summary>
    /// What the AI's style chooses (<c>EnemyAI.RunStyle</c>, <c>0x80059628</c>): its scores' first entry, whose combatant is the
    /// target and whose cell the goal; a spell's entry selects its action slot's spell (<see cref="SlotChoice"/>). The Shadow walks at
    /// its merger while the party carries its item (<see cref="ShadowGoal"/>).
    /// </summary>
    private (int X, int Z) RunStyle(EnemyAI ai)
    {
        var scores = Style(ai);
        ai.Target = -1;
        if (scores.Count == 0)
            return (0, 0);
        var first = scores[0];
        if (first.Combatant is { } t)
            ai.Target = t.Slot;
        if (first.Slot != 0)
            ai.Combatant.Spell = SlotChoice(first.Slot - 1);
        return ShadowGoal(ai, (first.X, first.Z));
    }

    /// <summary>
    /// The style's scores (<c>EnemyAI.Style0</c>–<c>Style10</c>). The casters' and the bosses' (5–10) score their spells first
    /// (<see cref="ChooseSpellOrAttack"/>), and with none that scores fight with their weapons: 5 as 0, 6 and 9 their attack cells,
    /// 7 as 3, 8 as 4, 10 as 3 with the farthest cell to fall back on. A spell's choice changes neither what it fights as nor what
    /// it wants.
    /// </summary>
    private AiScores Style(EnemyAI ai) => ai.Style switch
    {
        0 => Nearest(ai),
        1 or 4 => BestAttackCells(ai),
        2 => NearestAttackCell(ai),
        3 => Archer(ai, boss: false),
        >= 5 and <= 10 when ChooseSpellOrAttack(ai) is { } spells => spells,
        5 => Nearest(ai),
        8 => BestAttackCells(ai),
        7 => Archer(ai, boss: false),
        6 => CollectedAttackCells(ai, orOpponents: false),
        9 => CollectedAttackCells(ai, orOpponents: true),
        10 => Archer(ai, boss: true),
        _ => Nearest(ai),
    };

    /// <summary>
    /// Style 0, the rats' and the dull fighters' (<c>EnemyAI.Style0</c>, <c>0x8005910C</c>): the nearest opponent by the grid's
    /// distance, truncated, so ties are common and the sort breaks them; the goal is its own cell.
    /// </summary>
    private AiScores Nearest(EnemyAI ai)
    {
        var scores = Opponents(ai);
        ScoreByDistance(ai, scores, crowded: true);
        scores.Sort(ascending: true, Game.Random.Sort);
        return scores;
    }

    /// <summary>
    /// Styles 1 and 4, the clever fighters' and those that strike near and far (<c>EnemyAI.Style1</c>, <c>0x80059144</c>;
    /// <c>Style4</c>, <c>0x8005933C</c>): each opponent's best cell to attack it from (<see cref="ScoreAttackCells"/>), the best first.
    /// </summary>
    private AiScores BestAttackCells(EnemyAI ai)
    {
        var scores = Opponents(ai);
        ScoreAttackCells(ai, scores, ai.Combatant.Action);
        scores.Sort(ascending: false, Game.Random.Sort);
        return scores;
    }

    /// <summary>
    /// Style 2, the dull archers' (<c>EnemyAI.Style2</c>, <c>0x80059178</c>): the nearest cell it could attack from; with none, the
    /// nearest it could with the other kind of weapon, which it then wants (and swaps to, spending its turn); with none either, the
    /// nearest opponent.
    /// </summary>
    private AiScores NearestAttackCell(EnemyAI ai)
    {
        var c = ai.Combatant;
        var scores = CollectAttackCells(ai, c.Action);
        if (scores.Count == 0)
        {
            scores = CollectAttackCells(ai, c.Action == CombatAction.Melee ? CombatAction.Ranged : CombatAction.Melee);
            if (scores.Count == 0)
                scores = Opponents(ai);
            else
                ai.Wanted = c.Action == CombatAction.Melee ? AiKind.Ranged : AiKind.Melee;
        }
        ScoreByDistance(ai, scores, crowded: true);
        scores.Sort(ascending: true, Game.Random.Sort);
        return scores;
    }

    /// <summary>
    /// Style 3, the clever archers' (<c>EnemyAI.Style3</c>, <c>0x8005923C</c>), and 10, the archer bosses' fallback: with an opponent
    /// in reach, the best cell to fight it from in melee, which it then wants; holding a melee weapon with none near, the cell farthest
    /// from them all, wanting its bow back; otherwise a cell to shoot from, the nearest of those no opponent's field reaches, or if
    /// every one is reached the farthest (the boss: the nearest still). With none, style 0's choice (the boss: the farthest cell).
    /// </summary>
    private AiScores Archer(EnemyAI ai, bool boss)
    {
        var c = ai.Combatant;
        AiScores scores;
        if (!FreeOfOpponents(c))
        {
            scores = CollectAttackCells(ai, CombatAction.Melee);
            ScoreAttackCells(ai, scores, CombatAction.Melee);
            scores.Sort(ascending: false, Game.Random.Sort);
            ai.Wanted = AiKind.Melee;
            return scores;
        }
        if (c.Action == CombatAction.Melee)
        {
            scores = Opponents(ai);
            FarthestCell(ai, scores);
            ai.Wanted = AiKind.Ranged;
            return scores;
        }
        scores = CollectAttackCellsByReach(ai);
        if (scores.Count == 0)
        {
            if (!boss)
                return Nearest(ai);
            scores = Opponents(ai);
            FarthestCell(ai, scores);
        }
        scores.Sort(ascending: true, Game.Random.Sort);
        var safe = scores.Count > 0 && scores[0].Score == 0;
        if (safe)
            scores.KeepBest();
        ScoreByDistance(ai, scores, crowded: safe || boss);
        scores.Sort(ascending: safe || boss, Game.Random.Sort);
        return scores;
    }

    /// <summary>
    /// Styles 6 and 9's fallback, a melee caster's and a melee boss's with no spell to cast: the cells it could attack from, each
    /// scored for its opponent (<see cref="ScoreAttackCells"/>), the best first, wanting melee; with none, style 1's (the boss: the
    /// opponents, scored the same).
    /// </summary>
    private AiScores CollectedAttackCells(EnemyAI ai, bool orOpponents)
    {
        var action = ai.Combatant.Action;
        var scores = CollectAttackCells(ai, action);
        if (scores.Count == 0)
        {
            if (!orOpponents)
            {
                ai.Wanted = AiKind.Melee;
                return BestAttackCells(ai);
            }
            scores = Opponents(ai);
        }
        ScoreAttackCells(ai, scores, action);
        scores.Sort(ascending: false, Game.Random.Sort);
        ai.Wanted = AiKind.Melee;
        return scores;
    }

    /// <summary>
    /// Whether <paramref name="p"/> is the AI's opponent (<c>EnemyAI.IsOpponent</c>, <c>0x8005737C</c>): alive, able to act, and on the
    /// other side.
    /// </summary>
    private static bool IsOpponent(EnemyAI ai, Combatant? p) =>
        p is not null && !p.Sheet.IsDead && p.Active && p.Hostile != ai.Combatant.Hostile;

    /// <summary>The scores with an entry for each opponent, in slot order, at its cell (<c>EnemyAI.ListOpponents</c>, <c>0x8005783C</c>).</summary>
    private AiScores Opponents(EnemyAI ai)
    {
        var scores = new AiScores(InPlay);
        for (var i = 0; i < InPlay; i++)
        {
            if (IsOpponent(ai, _slots[i]))
            {
                var (x, z) = Cell8(_slots[i]!);
                scores.Add(x, z, 0, 0, _slots[i]);
            }
        }
        return scores;
    }

    /// <summary>
    /// Scores each entry by its distance from the AI's cell (<c>EnemyAI.ScoreByDistance</c>, <c>0x80058B90</c>), then, while the party
    /// has an area to fear and the leader lives, by the allies round it (<see cref="AreaDanger"/>).
    /// </summary>
    private void ScoreByDistance(EnemyAI ai, AiScores scores, bool crowded)
    {
        var danger = LeaderGone ? 0 : Danger;
        var (mx, mz) = Cell8(ai.Combatant);
        foreach (var e in scores.Entries)
        {
            e.Score = ArenaGrid.Distance(mx, mz, e.X, e.Z) & 0xFF;
            if (danger != 0)
                e.Score = AreaDanger(ai, e.X, e.Z, e.Score, crowded, danger) & 0xFF;
        }
    }

    /// <summary>
    /// A score moved by the AI's allies within <paramref name="radius"/> of a cell (<c>EnemyAI.AreaDanger</c>, <c>0x80058A48</c>):
    /// <paramref name="crowded"/>, 5 more each, so an opponent among them is the less sought; otherwise 5 less each, and 0 once it's
    /// under 5, so a cell among them is the less good.
    /// </summary>
    private int AreaDanger(EnemyAI ai, int x, int z, int score, bool crowded, int radius)
    {
        var me = ai.Combatant;
        for (var k = 0; k < InPlay; k++)
        {
            if (k == me.Slot || _slots[k] is not { } p || p.Sheet.IsDead || p.Hostile != me.Hostile)
                continue;
            var (px, pz) = Cell8(p);
            if (ArenaGrid.Distance(x, z, px, pz) > radius)
                continue;
            if (crowded)
            {
                score = (score + 5) & 0xFF;
            }
            else
            {
                if (score < 5)
                    return 0;
                score -= 5;
            }
        }
        return score;
    }

    /// <summary>
    /// The cells within the combatant's field, free for its footprint, from which it could attack someone doing
    /// <paramref name="action"/>, each with the nearest of those (<c>EnemyAI.CollectAttackCells</c>, <c>0x80057908</c>). It tries each
    /// as though it stood there (<see cref="TryAttack"/>); but the reach of a melee attack reads its body again, as the original's
    /// does, so for melee only where its body stands counts.
    /// </summary>
    private AiScores CollectAttackCells(EnemyAI ai, CombatAction action)
    {
        var c = ai.Combatant;
        var r = c.Radius;
        var scores = new AiScores((r * r * 4) & 0xFFFC);
        var saved = c.Cell;
        var (cx, cz) = Cell8(c);
        Grid.Vacate(cx, cz, c.Footprint);
        foreach (var (x, z) in Square(cx, cz, r))
        {
            if (ArenaGrid.Distance(x & 0xFF, z & 0xFF, cx, cz) > r || !Grid.IsFree(x, z, c.Footprint))
                continue;
            c.Cell = new Vector2(x, z);
            if (TryAttack(c, action).Selected == 0)
                continue;
            var (best, nearest) = (0xFF, 0);
            for (var k = 0; k < InPlay; k++)
            {
                if (!_triesA.IsCandidate(k))
                    continue;
                var (tx, tz) = Cell8(_slots[k]!);
                var d = ArenaGrid.Distance(x & 0xFF, z & 0xFF, tx, tz) & 0xFF;
                if (d < best)
                    (best, nearest) = (d, k);
            }
            scores.Add(x, z, 0, 0, _slots[nearest]);
        }
        c.Cell = saved;
        Grid.Occupy(cx, cz, c.Footprint);
        // The original tries the cells in set A itself, which it leaves empty.
        TargetsA.Clear();
        return scores;
    }

    /// <summary>
    /// The AI's set A (<see cref="_triesA"/>) built for <paramref name="c"/> doing <paramref name="action"/> where it's taken to stand,
    /// as the battle's would be there. The tries move its place to the cell and put it back after, as the original's do, and the port's
    /// must: the reach reads the bodies (<see cref="Combatant.IsWithinReach"/>), and an opponent's field worked out again reads its
    /// reach and the Shadow's (<see cref="Combatant.UpdateRadius"/>), which go by the place and put it back where the body stands.
    /// </summary>
    private TargetSet TryAttack(Combatant c, CombatAction action)
    {
        _triesA.RebuildAttack(this, c, action);
        return _triesA;
    }

    /// <summary>
    /// The cells within the combatant's field it could attack each opponent from, one for each, preferring one no scored opponent's
    /// field reaches, then the nearer the opponent (<c>EnemyAI.CollectAttackCellsByReach</c>, <c>0x80057C30</c>); the score is whether
    /// it's reached (<see cref="AnyScoredWithinRadius"/>).
    /// </summary>
    private AiScores CollectAttackCellsByReach(EnemyAI ai)
    {
        var c = ai.Combatant;
        var r = c.Radius;
        var scores = new AiScores((r * r * 4) & 0xFFFC);
        var saved = c.Cell;
        var (cx, cz) = Cell8(c);
        Grid.Vacate(cx, cz, c.Footprint);
        foreach (var (x, z) in Square(cx, cz, r))
        {
            if (ArenaGrid.Distance(x & 0xFF, z & 0xFF, cx, cz) > r || !Grid.IsFree(x, z, c.Footprint))
                continue;
            c.Cell = new Vector2(x, z);
            if (TryAttack(c, c.Action).Selected == 0)
                continue;
            for (var k = 0; k < InPlay; k++)
            {
                if (!_triesA.IsCandidate(k) || _slots[k] is not { } t)
                    continue;
                var found = scores.Entries.FirstOrDefault(e => e.Combatant == t);
                var within = AnyScoredWithinRadius(ai, scores);
                var (tx, tz) = Cell8(t);
                if (found is not null)
                {
                    if (within == 0 && found.Score != 0)
                    {
                        found.X = x & 0xFF;
                    }
                    else
                    {
                        if (ArenaGrid.Distance(found.X, found.Z, tx, tz) <= ArenaGrid.Distance(x & 0xFF, z & 0xFF, tx, tz))
                            continue;
                        found.X = x & 0xFF;
                    }
                    found.Z = z & 0xFF;
                    found.Score = within;
                }
                else
                {
                    scores.Add(x, z, 0, within, t);
                }
            }
        }
        Grid.Occupy(cx, cz, c.Footprint);
        c.Cell = saved;
        // The original tries the cells in set A itself, which it leaves empty.
        TargetsA.Clear();
        return scores;
    }

    /// <summary>
    /// Whether the combatant, where it's taken to stand, is within the field of any opponent its scores list
    /// (<c>EnemyAI.AnyScoredWithinRadius</c>, <c>0x800573F0</c>), each opponent's radius worked out again first.
    /// </summary>
    private int AnyScoredWithinRadius(EnemyAI ai, AiScores scores)
    {
        var c = ai.Combatant;
        foreach (var e in scores.Entries)
        {
            if (e.Combatant is not { } p)
                continue;
            p.UpdateRadius(this);
            if (Vector2.Distance(c.Cell, p.Cell) <= p.Radius)
                return 1;
        }
        return 0;
    }

    /// <summary>
    /// Each entry's best cell to attack its opponent from (<c>EnemyAI.ScoreAttackCells</c>, <c>0x80058C7C</c>): of the free cells
    /// round the opponent from which set A has it, as though the combatant stood there facing it, 25 for its own cell, 20 from the
    /// opponent's flank or back (<see cref="IsFlanking"/>), 50 above it, up to 15 more the more it's hurt, 10 more if it holds a
    /// ranged weapon, and less for allies near (<see cref="AreaDanger"/>); the first of the best, if any beats the entry's score. Each
    /// try turns the combatant's direction toward the opponent, as the original's does, and it's left as the last turned it.
    /// </summary>
    private void ScoreAttackCells(EnemyAI ai, AiScores scores, CombatAction action)
    {
        var c = ai.Combatant;
        var danger = LeaderGone ? 0 : Danger;
        var saved = c.Cell;
        var (cx, cz) = Cell8(c);
        var footprint = c.Footprint;
        Grid.Vacate(cx, cz, footprint);
        foreach (var e in scores.Entries)
        {
            if (e.Combatant is not { } t)
                continue;
            var (tx, tz) = Cell8(t);
            var ranged = WeaponOf(t) is { Range: not 0 };
            var (bx, bz) = (e.X, e.Z);
            for (var z = (int)(sbyte)(tz - footprint); z <= tz + t.Footprint; z = (sbyte)(z + 1))
            {
                for (var x = (int)(sbyte)(tx - footprint); x <= tx + t.Footprint; x = (sbyte)(x + 1))
                {
                    if (!Grid.IsFree(x, z, footprint))
                        continue;
                    c.Cell = new Vector2(x, z);
                    DirectionToward(c, tx, tz);
                    if (!TryAttack(c, action).IsCandidate(t.Slot))
                        continue;
                    var s = 0;
                    if ((x & 0xFF) == cx && (z & 0xFF) == cz)
                        s += 25;
                    if (IsFlanking(c, t, tx, tz))
                        s += 20;
                    if ((sbyte)Grid.HeightDifference(x & 0xFF, z & 0xFF, tx, tz) < 0)
                        s += 50;
                    var health = MathF.Min((float)t.Sheet.Hp / t.Sheet.MaxHp, 1f);
                    s += (int)((1f - health) * 15f);
                    s &= 0xFF;
                    if (ranged)
                        s = (s + 10) & 0xFF;
                    if (danger != 0)
                        s = AreaDanger(ai, x & 0xFF, z & 0xFF, s, crowded: false, danger);
                    if (e.Score < s)
                        (e.Score, bx, bz) = (s, x & 0xFF, z & 0xFF);
                }
            }
            (e.X, e.Z) = (bx, bz);
        }
        Grid.Occupy(cx, cz, footprint);
        c.Cell = saved;
        // The original tries the cells in set A itself, which it leaves empty.
        TargetsA.Clear();
    }

    /// <summary>
    /// The cell within the combatant's field, free for it, farthest from every listed opponent summed (<c>EnemyAI.FarthestCell</c>,
    /// <c>0x800587BC</c>), put in the scores' first entry with no one in it: where it retreats to.
    /// </summary>
    private void FarthestCell(EnemyAI ai, AiScores scores)
    {
        var c = ai.Combatant;
        var r = c.Radius;
        var (cx, cz) = Cell8(c);
        Grid.Vacate(cx, cz, c.Footprint);
        var (best, sum) = ((0, 0), 0);
        foreach (var (x, z) in Square(cx, cz, r))
        {
            if (ArenaGrid.Distance(x & 0xFF, z & 0xFF, cx, cz) > r || !Grid.IsFree(x, z, c.Footprint))
                continue;
            var total = 0;
            foreach (var e in scores.Entries)
            {
                if (e.Combatant is not { } p)
                    continue;
                var (px, pz) = Cell8(p);
                total = (ushort)(total + ArenaGrid.Distance(x & 0xFF, z & 0xFF, px, pz));
            }
            if (sum < total)
                (best, sum) = ((x & 0xFF, z & 0xFF), total);
        }
        if (scores.Count > 0)
        {
            var first = scores[0];
            (first.X, first.Z, first.Combatant) = (best.Item1, best.Item2, null);
        }
        Grid.Occupy(cx, cz, c.Footprint);
        // The original's leaves set A empty too.
        TargetsA.Clear();
    }

    /// <summary>The cells of the square about (cx, cz) out to <paramref name="r"/>, Z outer and X inner, as signed bytes.</summary>
    private static IEnumerable<(int X, int Z)> Square(int cx, int cz, int r)
    {
        for (var z = (int)(sbyte)(cz - r); z <= cz + r; z = (sbyte)(z + 1))
        {
            for (var x = (int)(sbyte)(cx - r); x <= cx + r; x = (sbyte)(x + 1))
                yield return (x, z);
        }
    }

    /// <summary>
    /// Points the combatant's direction at a cell (<c>Combatant.DirectionToward</c>, <c>0x800611E0</c>), by the signs of the way there
    /// from its cell; from the cell itself, as it was.
    /// </summary>
    private static void DirectionToward(Combatant c, int tx, int tz)
    {
        var (x, z) = Cell8(c);
        if (x == (tx & 0xFF) && z == (tz & 0xFF))
            return;
        c.Direction = ArenaGrid.DirectionTo(x, z, tx, tz);
    }

    /// <summary>
    /// Whether <paramref name="c"/> stands on <paramref name="t"/>'s flank or behind it, facing it (<c>Combatant.IsFlanking</c>,
    /// <c>0x8006167C</c>): its cell in the half behind the way the target's direction faces (the half toward the back for a straight
    /// direction, the quarter for a diagonal), not the target's own, and its direction the one toward the target.
    /// </summary>
    internal static bool IsFlanking(Combatant c, Combatant t, int tx, int tz)
    {
        var (mx, mz) = Cell8(c);
        return IsFlanking(mx, mz, c.Direction, tx, tz, t.Direction);
    }

    /// <summary><see cref="IsFlanking(Combatant, Combatant, int, int)"/> by cells and directions.</summary>
    internal static bool IsFlanking(int mx, int mz, int direction, int tx, int tz, int targetDirection)
    {
        var (ox, oz) = ((int)(sbyte)(mx - tx), (int)(sbyte)(mz - tz));
        if (ox == 0 && oz == 0)
            return false;
        var behind = targetDirection switch
        {
            0 => oz > 0,
            1 => ox <= 0 && oz >= 0,
            2 => ox < 0,
            3 => ox <= 0 && oz <= 0,
            4 => oz < 0,
            5 => ox >= 0 && oz <= 0,
            6 => ox > 0,
            7 => ox >= 0 && oz >= 0,
            _ => false,
        };
        return behind && direction == ArenaGrid.DirectionTo(mx, mz, tx, tz);
    }
}
