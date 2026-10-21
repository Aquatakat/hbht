"""Puts the vectors together (the vectors.json) and writes tests/Aidyn.Rom.Tests/CasterVectors.cs from them: each case's overlay and mods,
its raw ScoreSpellCells and ChooseSpellOrAttack lists as FNV-1a hashes, RunStyle's choice, and Step's decision and the action's start."""
README = {
    "what": "Differential vectors for the enemy casters' and bosses' AI, produced by running 1.0's own functions in the MIPS interpreter (tools/aivectors/) on the combat checkpoints' RAM with an enemy caster overlaid on a rat. All addresses 1.0's.",
    "overlay": {
        "how": "For case.slot (an enemy slot holding a living Giant Rat in case.checkpoint): ArenaGrid.Vacate the rat's footprint; allocate a 0x48 entity; FUN_8006E434(entity, case.entity, 1) (the enemy builder: stats, skills, spells, equipment, effects); Combatant.Init(c, entity, -1, cellX, cellZ, direction, footprint, 1, slot) (memsets the 0x8C combatant, copies the entity's resistances, EnemyAI.Init builds the AI record); the combatant's float place and old place put back to the rat's; the footprint Occupied again; Combatant.UpdateRadius (CreateBody's first act); the action byte (+0x25) from the wielded weapon as Combatant.SetDefaultAction (0 no weapon, 1 melee, 2 ranged, 4 ranged that strikes in melee), without EquipVisuals.",
        "kept": "the rat's body (position, radius, height, queue), cell, direction, footprint, slot, the combat struct (counts, leader slot and morale, grid, target sets, danger, focus), the party, the main and sort streams, the world clock. Nothing in the combat struct or the body changes (checked byte for byte).",
        "changes": "only the combatant record's bytes the entity decides: +0x21 radius (UpdateRadius from the new Dexterity, Pochanargat 0), +0x25 the action, +0x30/+0x34/+0x38/+0x3C the two resistance entries (the DB row's), +0x40 the entity pointer, +0x44 the AI pointer; the heap gets the entity (0x48 and its parts) and the AI (0x1C). Neither stream is drawn from. The port mirrors it by replacing the combatant's sheet with Game.Recruit(entity), arming it as Battle.ArmFromList does and a fresh EnemyAI.Init, keeping the checkpoint's bodies (the rat's body radius, height and place are the caster's too).",
        "radius": "case.pre.radius is the field radius after the overlay (Combatant.UpdateRadius), engaged or not as the checkpoint's bodies stand; the port recomputes it from the entity's modified Dexterity at the turn's start, and should agree.",
    },
    "turnStart": "Before every run (case.pre.afterBeginTurn): Combat.Current's +0x2C = the combatant; Combatant.SyncFromBody; the focus (Combat+0x18/+0x1C) := the combatant's x and z (TurnOrder.Advance); state = next state = 8; the stuck timer 0; TargetSet.Rebuild of A and of B for the combatant (TurnOrder.BeginTurn). CheckFlee and the camera/ring are skipped. The sort stream is set to the seed of each run; the main stream is the checkpoint's (case.pre.rng).",
    "mods": {
        "move": ["move", "slot", "x", "z"],
        "stamina": ["stamina", "slot", "value"],
        "danger": ["danger", "value"],
        "effect": ["effect", "slot", "kind", "power", "timer", "margin"],
        "radius": ["radius", "value"],
        "aibytes": ["aibytes", {"offset": "value"}],
        "spellsel": ["spellsel", "source", "place"],
        "daypart": ["daypart", "value"],
        "itemspell": ["itemspell", "armor|shield", "weapon record id"],
        "meaning": {
            "move": "the combatant and its body to float place (x, z) (old place too); the body's height unchanged; the grid follows; the caster's radius is worked out again afterwards",
            "stamina": "the combatant's current and modified Stamina := value (the base unchanged)",
            "danger": "Combat+0x12 := value",
            "effect": "Character.AddEffect(entity of slot, kind, power, timer, margin, combatant of slot) (0x800702F0), e.g. kind 48 Starlight Shield, 35 Spirit Shield, 52 Clumsiness",
            "radius": "the combatant's +0x21 := value after the overlay (UpdateRadius is not run again); 0 makes ScoreSpellsInPlace run",
            "aibytes": "AI record bytes: key = offset (decimal string), +4 type, +7 wanted, +0x0A+i the action slot i (0xFF switches it off)",
            "spellsel": "the entity's +0x3D := source, +0x3E := place (0,0 = no spell selected)",
            "daypart": "World+2 := value (1..3 by day, 4 night); World is *0x800E303C",
            "itemspell": "SYNTHETIC (no enemy in the data has one): the weapon DB record built as an item (FUN_8007B670) and worn as the armor (source 2, AI action slot 5) or the shield (source 3, slot 6); the AI slot byte := the kind of its first spell; the item's spell struct keeps its level and charges"
        }
    },
    "cases": "list. Each: id; pre (checkpoint, slot, entity, mods, counts, combatants (every combatant before the turn begins), caster (the built entity: stats base/modified/current, Wizard modified, hp, spells with level/kind/cost/targets/range/unk1e, the chosen spell [ce+0x3C, 0x3D, 0x3E], armor/shield/weapon with their item spells and charges), casterAi (the 0x1C record bytes and the spare weapon id), radius, footprint, sortRng, rng, setA/setB/focus before the turn begins, afterBeginTurn {setA, setB, sortRng, rng, lastTarget, focus}); spells (per spell-holding action slot: SpellValue and set B built at the caster's own place: cand/sel/most/cursor, castLevel, castCost, targetCountScore = nsel-1, and for kind 8 AreaCentre's count and focus); raw {entries, steps}: ScoreSpellCells alone, the entries as added (unsorted: score before the distance re-score, x, z, slot byte = action slot + 1, combatant slot); choose[]: per sort seed the list after ChooseSpellOrAttack's four tail calls (Sort(0), KeepBest, ScoreByDistance(ai,1), Sort(1)) from the raw list, the sort stream after; chooseFull: the original's whole ChooseSpellOrAttack for choose[0].sortSeed with equalsTail; runStyle[]: RunStyle from the turn's start for a sort seed: goal cell, ai+5, ce+0x3D/+0x3E, +4, +7, sort stream, entries left; step[]: per sort seed EnemyAI.Step from the turn's start: afterStep, then drive (the leg's end: the body put at the last point it was sent to, StateMoving, StateFacing x up to 3) with drive.trace (state pairs), drive.arrival, drive.execute (the state when the action is to start: state 6/6), final (after the action's first frame: state 2/2 for an action, 24/8 rest) and finalCombatants.",
    "stateFields": "state_after: state, next, stuck (Combat+0x4FBC), lastTarget (Combat+0x14), focus (Combat+0x18/+0x1C), castMargin (ce+0x3C, 255 none), spell [ce+0x3D, ce+0x3E], ai {bytes[0x1C], spare}, target (ai+5), kind (ai+4), wanted (ai+7), pathIdx, pathLen, path, x z ox oz dir face flags action weapon of the combatant, setA/setB {cand, sel (14 each), order, mode, ncand, nsel, most, cursor, pos}, queue {items [ring index, x, z, tol, flag], tail, head, flags (body +0x1E), goal [x, z, tol]}, rest (0x800DB0D0), logs (the two log lines), rng (0x800D8088), sortRng (0x800E6B60), lastTargetOfCombatant (c.+0x10).",
    "single": "ScoreSpellCells for one action slot at a time (the others' AI bytes set to 0xFF), full = all slots; independent is false only where the 32-entry capacity of an engaged caster (R = 1: (1*1*4*8)&0xFFE0) dropped the last entries of the full run (single-cp09-s4-Sorceror-engaged: 4 slots x 9 cells = 36 > 32).",
    "spellValueByKind": "EnemyAI.SpellValue for every kind 0..58 with set B's candidate flags set for slots 0-5 of checkpoint 08 (neutral; Rheda's Endurance, a Naming helpful effect, is on in the checkpoint, so Dispel Naming is 81) and the conditional kinds in contexts that satisfy them.",
    "aiCanCast": "Combatant.AICanCast directly: Stamina, charges, part of the day, requirement (the edited spell struct rows).",
    "danger": "Combat.PartyPotionTier and Combat.CreatePartyCombatants on checkpoint 08's party with flasks, Fireballs and Wizard levels: tier is Setup's store, danger the byte after the raise.",
    "moraleSpells": "Character.AddEffect for kinds 0x19 and 0x2F on a rat, then EffectRules.Round until the record goes; and Dispel, a lethal hit, Combatant.Death, RemoveAllEffects.",
    "rerun": "python tools/aivectors/aivectors.py",
}


def fnv(s):
    h = 0xcbf29ce484222325
    for b in s.encode():
        h ^= b; h = (h * 0x100000001b3) & 0xFFFFFFFFFFFFFFFF
    return h

def entries(es):
    return ''.join(f"{e['score']},{e['x']},{e['z']},{e['slot']},{e['combatant'] if e['combatant'] is not None else -1};" for e in es)

def mods(ms):
    parts = []
    for m in ms:
        if m[0] == 'aibytes':
            parts.append('aibytes:' + ','.join(f'{k}={val}' for k, val in m[1].items()))
        else:
            parts.append(m[0] + ':' + ','.join(str(x) for x in m[1:]))
    return '|'.join(parts)

def mask(s):
    return sum(1 << i for i, x in enumerate(s['sel']) if x)

def f(x):
    return f'{x:.6g}f'

def state(s):
    path = ' '.join(f'{x},{z}' for x, z in s['path'])
    return (f"new({s['state']}, {s['next']}, {s['lastTarget']}, {f(s['focus'][0])}, {f(s['focus'][1])}, \"{path}\", {f(s['x'])}, {f(s['z'])}, "
            f"{s['target'] if s['target'] != 255 else -1}, {s['spell'][0]}, {s['spell'][1]}, {s['kind']}, {s['wanted']}, {s['castMargin'] if s['castMargin'] < 128 else s['castMargin'] - 256}, "
            f"0x{mask(s['setA']):X}, 0x{mask(s['setB']):X}, {s['rng']}u, {s['sortRng']}u)")


def cs_lines(v):
    lines = []
    for c in v['cases']:
        p = c['pre']
        raw = entries(c['raw']['entries'])
        seeds = []
        rs = {r['sortSeed']: r for r in c['runStyle']}
        st = {s['sortSeed']: s for s in c['step']}
        for ch in c['choose']:
            k = ch['sortSeed']
            r = rs.get(k); s = st.get(k)
            run = 'null' if r is None else f"new({r['goal'][0]}, {r['goal'][1]}, {r['target'] if r['target'] != 255 else -1}, {r['spell'][0]}, {r['spell'][1]}, {r['kind']}, {r['wanted']}, {r['sortRng']}u)"
            step = 'null'
            if s is not None:
                arrival = s['drive'].get('arrival')
                arr = 'null' if not arrival else f"new System.Numerics.Vector2({f(arrival[0])}, {f(arrival[-1])})"
                step = f"new({state(s['afterStep'])}, {arr}, {state(s['final'])})"
            seeds.append(f"new({k}u, {str(ch['returned']).lower()}, {len(ch['entries'])}, 0x{fnv(entries(ch['entries'])):X}ul, {ch['sortRng']}u, {run}, {step})")
        lines.append(f"        new(\"{c['id']}\", {p['checkpoint']}, {p['slot']}, 0x{p['entity']:04X}, \"{mods(p['mods'])}\", {p['radius']}, 0, "
                     f"{len(c['raw']['entries'])}, 0x{fnv(raw):X}ul, [\n            " + ',\n            '.join(seeds) + "]),")

    return lines


TEMPLATE = '''namespace Aidyn.Rom.Tests;

// Generated by tools/aivectors (python tools/aivectors/aivectors.py), do not edit: 1.0's own ChooseSpellOrAttack, RunStyle and
// EnemyAI.Step run in the MIPS interpreter on the combat checkpoints' RAM, with an enemy caster built by the original's own entity
// builder in a rat's place. Lists are kept as FNV-1a hashes of "score,x,z,slot,combatant;" for each entry in order.
internal static class CasterVectors
{
    /// <summary>A case: the checkpoint, the rat's slot, the caster put there, the changes made to the battle, and its field radius.</summary>
    public sealed record Case(string Id, int Checkpoint, int Slot, int Entity, string Mods, int Radius, int Unused, int RawCount, ulong RawHash, Seed[] Seeds)
    {
        public override string ToString() => Id;
    }

    /// <summary>For a seed of the sort's stream: ChooseSpellOrAttack's list and the stream after, RunStyle's choice, and Step's.</summary>
    public sealed record Seed(uint SortSeed, bool Returned, int ChooseCount, ulong ChooseHash, uint ChooseSortAfter, Run? Run, StepResult? Step);

    /// <summary>RunStyle's goal, the AI's target, the selected spell (+0x3D, +0x3E), what it fights as and wants, and the sort's stream after.</summary>
    public sealed record Run(int GoalX, int GoalZ, int Target, int SpellSource, int SpellPlace, int Kind, int Wanted, uint SortAfter);

    /// <summary>The battle after Step, where the leg ends (null for no walk), and after the action's first frame.</summary>
    public sealed record StepResult(Snapshot After, System.Numerics.Vector2? Arrival, Snapshot Final);

    /// <summary>
    /// The state, the next, the action's target (+0x14), the focus, the AI's path, the combatant's place, the AI's target, the selected
    /// spell, what it fights as and wants, the cast's margin, the target sets' selections by slot, and both streams.
    /// </summary>
    public sealed record Snapshot(int State, int Next, int Target, float FocusX, float FocusZ, string Path, float X, float Z, int AiTarget,
        int SpellSource, int SpellPlace, int Kind, int Wanted, int Margin, int SelectedA, int SelectedB, uint Rng, uint SortRng);

    public static readonly Case[] Cases =
    [
@LINES@
    ];
}
'''


def assemble(cases, single, kinds, aican, misc):
    """The vectors.json: the cases by id, and the one-off vectors."""
    cases = sorted(cases, key=lambda c: c['id'])
    misc = misc or {}
    return dict(readme=README, cases=cases, single=single, spellValueByKind=kinds, aiCanCast=aican,
                danger=misc.get('danger'), moraleSpells=misc.get('moraleSpells'))


def write_cs(v, path):
    """Writes the test's table with LF line ends (git normalises them for the checkout)."""
    src = TEMPLATE.replace('@LINES@', '\n'.join(cs_lines(v)))
    with open(path, 'w', encoding='utf-8', newline='\n') as out:
        out.write(src)
    return len(v['cases']), len(src.encode('utf-8'))
