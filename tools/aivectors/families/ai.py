"""Morale and the AI's turn: EnemyAI.Morale (0x80056FA8), MayCheckMorale (0x8005722C), CheckFlee (0x800572AC) and
ReserveFleesOnArrival (0x80056E18); a turn as TurnOrder.BeginTurn begins it, CheckFlee then EnemyAI.Step (0x8005B314) for styles 0-4 and
a flight; and EnemyAI.FleeAdvance (0x8005B434) after a flight's leg. On the Combat checkpoints and the parity logs' states, one input
changed at a time. Args are the operation (0 Morale, 1 MayCheckMorale, 2 CheckFlee, 3 ReserveFleesOnArrival, 4 a turn, 5 FleeAdvance),
the slot (the entity id for 3) and the operation's own (Morale's own check, CheckFlee's asked, a turn's asked, -1 for no check or 2 as BeginTurn asks).
Morale gives the morale, the leader's after (Combat+0x11) and the AI's flags after; CheckFlee the flags, the flight's turns and the
leader's; a turn those, then the state and the next, the AI's target, what it fights as and wants, its path, the combatant's place, gone,
the action's target (Combat+0x14), the target sets' selections and the sort's stream after; FleeAdvance gone, the turns, the states and
the place. Each gives the main stream's draws."""
import struct

import state
import sim
from draws import seed_before, value_for

FAMILY = 'Ai'

MORALE = 0x80056FA8
MAY_CHECK = 0x8005722C
CHECK_FLEE = 0x800572AC
RESERVE_FLEES = 0x80056E18
STEP = 0x8005B314
FLEE_ADVANCE = 0x8005B434
IS_FLEEING = 0x8005B9AC
SYNC_FROM_BODY = 0x80067074
SAVE_START = 0x8005F770
REBUILD = 0x80069264
SORT = 0x800E6B60                 # Quicksort.Seed
STATE, NEXT = 0x800D9454, 0x800D9455

SEED = 0x1234

# The calls a turn makes that the interpreter can't or needn't run: the OS's message queues (the audio's), the body's model, the fled
# portrait and the spell effects' entries.
STUBS = (0x800C25C0, 0x800BE860, 0x800BD910, 0x80066AEC, 0x80086230, 0x8008C880, 0x8008AA68, 0x80021564)

MORALE_OP, MAY_OP, CHECK_OP, RESERVE_OP, TURN_OP, ADVANCE_OP = range(6)


# ---- the family's pokes, each mirrored by AiVectorTests' own
def _int(s):
    return int(s, 0)


def _ai(st, slot):
    return st.cpu.rd32(st.record(_int(slot)) + 0x44)


def _f(st, address, v):
    st.S.setfl(address, float(v))


def _counts(st, a):
    for off, v in zip((2, 4, 6), a):
        st.cpu.wr8(st.combat + off, _int(v))


def _body(st, a):
    b = st.body(_int(a[0]))
    _f(st, b + 0x68, a[1])
    _f(st, b + 0x70, a[2])


POKES = {
    # Quicksort's own stream, which the AI's sorts draw on.
    'sortseed': lambda st, a: st.cpu.wr32(SORT, _int(a[0])),
    # The AI record's bytes: the morale (+6), the flags (+9), the target (+5), the style (+8), what it fights as and wants (+4, +7), and
    # a flight's turns left (+0x18).
    'morale': lambda st, a: st.cpu.wr8(_ai(st, a[0]) + 6, _int(a[1]) & 0xFF),
    'aiflags': lambda st, a: st.cpu.wr8(_ai(st, a[0]) + 9, _int(a[1])),
    'target': lambda st, a: st.cpu.wr8(_ai(st, a[0]) + 5, _int(a[1]) & 0xFF),
    'style': lambda st, a: st.cpu.wr8(_ai(st, a[0]) + 8, _int(a[1])),
    'kind': lambda st, a: (st.cpu.wr8(_ai(st, a[0]) + 4, _int(a[1])), st.cpu.wr8(_ai(st, a[0]) + 7, _int(a[2]))),
    'flight': lambda st, a: st.cpu.wr32(_ai(st, a[0]) + 0x18, _int(a[1])),
    # The combatant's flags (+0x2C: 2 able, 0x10 hostile, 0x40 gone, 0x80 engaged) and its action (+0x25: 1 melee, 2 ranged, 4 both).
    'cflags': lambda st, a: st.cpu.wr16(st.record(_int(a[0])) + 0x2C, _int(a[1])),
    'action': lambda st, a: st.cpu.wr8(st.record(_int(a[0])) + 0x25, _int(a[1])),
    # Its field's radius (+0x21): 1 for one engaged.
    'radius': lambda st, a: st.cpu.wr8(st.record(_int(a[0])) + 0x21, _int(a[1])),
    # The selected spell (entity +0x3D its source, +0x3E its place).
    'spell': lambda st, a: (st.cpu.wr8(st.entity(_int(a[0])) + 0x3D, _int(a[1])), st.cpu.wr8(st.entity(_int(a[0])) + 0x3E, _int(a[2]))),
    # The battle's counts of the able (+2 all, +4 the party's, +6 the enemies'), the leader's morale (+0x11) and whether it's gone (+0xC),
    # whether the arena has flee points (+0x5440), and where a flight's turn began (+0x5318).
    'counts': _counts,
    'leader': lambda st, a: st.cpu.wr8(st.combat + 0x11, _int(a[0])),
    'leadergone': lambda st, a: st.cpu.wr32(st.combat + 0xC, _int(a[0])),
    'nofleepoints': lambda st, a: st.cpu.wr32(st.combat + 0x5440, 0),
    'origin': lambda st, a: (_f(st, st.combat + 0x5318, a[0]), _f(st, st.combat + 0x531C, a[1])),
    # The body alone to a place (its height kept), the combatant's record not following until it reads its body.
    'body': _body,
}


def s8(v):
    v &= 0xFF
    return v - 256 if v & 0x80 else v


def mask(st, which):
    s = st.combat + which
    return sum(1 << k for k in range(14) if st.cpu.rd32(s + 0x48 + 4 * k))


def fl(v):
    return f'{v:.2f}'


def begin_turn(st, p, asked):
    """TurnOrder.BeginTurn's part the AI reads: the combatant current, its check to flee, its body read and its start kept, the focus on
    it, state 8 as the state and the next, and its target sets unless it flees."""
    cpu, m = st.cpu, st.combat
    cpu.wr32(m + 0x2C, p)
    ai = cpu.rd32(p + 0x44)
    if asked == 2:
        # As BeginTurn asks: any but the leader while it lives.
        asked = int(cpu.rd32(m + 0xC) != 0 or cpu.rd8(p + 0x20) != cpu.rd8(m + 0x10))
    if asked >= 0 and cpu.rd8(ai + 9) & 8:
        st.call(CHECK_FLEE, [p, asked])
    st.call(SYNC_FROM_BODY, [p])
    st.S.setfl(m + 0x18, st.S.fl(p))
    st.S.setfl(m + 0x1C, st.S.fl(p + 4))
    st.call(SAVE_START, [p])
    cpu.wr8(STATE, 8)
    cpu.wr8(NEXT, 8)
    cpu.wr32(m + 0x4FBC, 0)
    if not st.call(IS_FLEEING, [ai]) & 0xFF:
        st.call(REBUILD, [m + sim.A_SET, p])
        st.call(REBUILD, [m + sim.B_SET, p])


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=POKES, seed=v['seed'], script=v.get('script'))
    for a in STUBS:
        st.cpu.hooks[a] = lambda cpu: 0
    op, who, arg = v['args']
    cpu, m = st.cpu, st.combat
    if op == RESERVE_OP:
        flees = st.call(RESERVE_FLEES, [who]) & 0xFF
        return [('flees', flees), ('draws', len(st.draws.drawn))], st.draws.callers()
    p = st.record(who)
    ai = cpu.rd32(p + 0x44)
    out = []
    if op == MORALE_OP:
        out = [('morale', s8(st.call(MORALE, [p, arg])))]
    elif op == MAY_OP:
        out = [('may', st.call(MAY_CHECK, [p]) & 0xFF)]
    elif op == CHECK_OP:
        st.call(CHECK_FLEE, [p, arg])
    elif op == TURN_OP:
        # The target sets as the combatant's own to start: a fleeing one's turn keeps those it has (the overlay doesn't lay them).
        st.call(REBUILD, [m + sim.A_SET, p])
        st.call(REBUILD, [m + sim.B_SET, p])
        begin_turn(st, p, arg)
        st.call(STEP, [ai])
    elif op == ADVANCE_OP:
        # As state 6 calls it, with state 8 to go on to.
        cpu.wr32(m + 0x2C, p)
        cpu.wr8(STATE, 6)
        cpu.wr8(NEXT, 8)
        st.call(FLEE_ADVANCE, [ai])
    flags = cpu.rd8(ai + 9)
    if op in (MORALE_OP, CHECK_OP, TURN_OP):
        out += [('aiflags', flags), ('leader', cpu.rd8(m + 0x11))]
    if op in (CHECK_OP, TURN_OP, ADVANCE_OP):
        out += [('flight', cpu.rd32(ai + 0x18) if flags & 1 or op == ADVANCE_OP else 0)]
    if op in (TURN_OP, ADVANCE_OP):
        n = cpu.rd8(ai + 0x13)
        path = '/'.join(f'{cpu.rd8(m + 0x5320 + 2 * i)},{cpu.rd8(m + 0x5321 + 2 * i)}' for i in range(n if n < st.S.diag() else 0))
        out += [('state', cpu.rd8(STATE)), ('next', cpu.rd8(NEXT)), ('gone', cpu.rd16(p + 0x2C) >> 6 & 1),
                ('x', fl(st.S.fl(p))), ('z', fl(st.S.fl(p + 4)))]
    if op == TURN_OP:
        out += [('target', s8(cpu.rd8(ai + 5))), ('kind', cpu.rd8(ai + 4)), ('wanted', cpu.rd8(ai + 7)), ('path', path or '-'),
                ('acttarget', s8(cpu.rd8(m + 0x14))), ('selA', mask(st, sim.A_SET)), ('selB', mask(st, sim.B_SET)),
                ('sort', cpu.rd32(SORT))]
    out.append(('draws', len(st.draws.drawn)))
    return out, st.draws.callers()


# ---- the vectors

def d6(*rolls):
    """A script of d6s (RollD's Range(1, 6) each) coming up the rolls, from draw 0."""
    return {i: value_for(r - 1, 6) for i, r in enumerate(rolls)}


def seed_of(source):
    return struct.unpack('>I', state.ram(source)[sim.RNG & 0x7FFFFF:(sim.RNG & 0x7FFFFF) + 4])[0]


def log(folder, dump):
    return f'log:{folder}/battle-01:{dump}.rdram.gz'


S13 = '20261101-104334-setup13'
S4 = '20261031-190448-setup4'
S5 = '20261031-194638-setup5'
S11 = '20261101-083418-setup11'


def cases():
    out = []

    def add(tag, op, who, arg=0, pokes='', source='cp05', seed=SEED, script=None):
        v = dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=[op, who, arg], seed=seed,
                 script=dict(script or {}))
        out.append(v)

    # ---- Morale's terms, on checkpoint 05's rat 5 (Lunar, morale 5, by day: 4 to start), its leader rat 4 (morale 5).
    for own in (0, 1):
        o = f'own{own}'
        add(f'morale-plain-{o}', MORALE_OP, 5, own)
        for mo in (-20, -3, 0, 2, 8, 11, 12, 15, 40, 127, -128):
            add(f'morale-record{mo}-{o}', MORALE_OP, 5, own, f'morale:5,{mo}')
        for part in range(5):
            add(f'morale-daypart{part}-lunar-{o}', MORALE_OP, 5, own, f'daypart:{part}')
            add(f'morale-daypart{part}-solar-{o}', MORALE_OP, 5, own, f'daypart:{part}|aspect:5,2')
        add(f'morale-weakness-{o}', MORALE_OP, 5, own, 'aiflags:5,0x18')
        add(f'morale-checkdue-{o}', MORALE_OP, 5, own, 'aiflags:5,0x0A')
        add(f'morale-steady-{o}', MORALE_OP, 5, own, 'aiflags:5,0x0C')
        add(f'morale-all-flags-{o}', MORALE_OP, 5, own, 'aiflags:5,0x1F')
        add(f'morale-stamina0-{o}', MORALE_OP, 5, own, 'mod:5,Stamina,0')
        # The enemies able against the four that began: 2 is half (no term), 1 strictly fewer.
        for e in (3, 2, 1, 0):
            add(f'morale-enemies{e}-{o}', MORALE_OP, 5, own, f'counts:{4 + e},4,{e}')
        # The party able against the four that began: 2 is half, 1 strictly fewer.
        for pa in (3, 2, 1, 0):
            add(f'morale-party{pa}-{o}', MORALE_OP, 5, own, f'counts:{pa + 4},{pa},4')
        # Effects on it: harmful (Clumsiness, Poison, Frozen Doom, Weakness, the morale spells) and helpful (Starlight Shield, Spirit
        # Shield), and both.
        for kind in (52, 36, 53, 22, 25, 47, 48, 35):
            add(f'morale-effect{kind}-{o}', MORALE_OP, 5, own, f'effect:5,{kind},3,5,5')
        add(f'morale-effect48-52-{o}', MORALE_OP, 5, own, 'effect:5,48,3,5,5|effect:5,52,3,5,5')
        # Its target with no Stamina, and with Stamina; a target slot that's empty.
        add(f'morale-target0-stamina0-{o}', MORALE_OP, 5, own, 'target:5,0|mod:0,Stamina,0')
        add(f'morale-target0-{o}', MORALE_OP, 5, own, 'target:5,0')
        add(f'morale-target3-stamina0-{o}', MORALE_OP, 5, own, 'target:5,3|mod:3,Stamina,0')
        add(f'morale-target12-{o}', MORALE_OP, 5, own, 'target:5,12')
        # Everything at once, both ways.
        add(f'morale-worst-{o}', MORALE_OP, 5, own, 'aiflags:5,0x18|mod:5,Stamina,0|counts:5,4,1|effect:5,52,3,5,5')
        add(f'morale-best-{o}', MORALE_OP, 5, own, 'aiflags:5,0x0C|counts:5,1,4|target:5,0|mod:0,Stamina,0|morale:5,9|daypart:4')
    # The leader's say, in its own check only: gone, unable, and its morale 7 to 12.
    for lm in (0, 7, 8, 9, 10, 11, 12, 200):
        add(f'morale-leader{lm}', MORALE_OP, 5, 1, f'leader:{lm}')
    add('morale-leadergone', MORALE_OP, 5, 1, 'leadergone:1')
    add('morale-leadergone-leader11', MORALE_OP, 5, 1, 'leadergone:1|leader:11')
    add('morale-leader-unable', MORALE_OP, 5, 1, 'cflags:4,0x30')
    add('morale-leader-unable-leader9', MORALE_OP, 5, 1, 'cflags:4,0x30|leader:9')
    add('morale-leader-checks', MORALE_OP, 4, 0, 'morale:4,10|daypart:4')
    # A target with no Stamina and a spell for its own side selected: Starlight Shield, setup 11's Hobgoblin's (slot 5) spellbook.
    for sel, tag in (((1, 0), 'spell0'), ((1, 1), 'spell1'), ((0, 0), 'nospell')):
        add(f'setup11-morale-target-stamina0-{tag}', MORALE_OP, 5, 1, f'target:5,1|mod:1,Stamina,0|spell:5,{sel[0]},{sel[1]}',
            source=log(S11, '0002'))

    # ---- MayCheckMorale: a check due, no Stamina, fleeing, and the enemies able against half.
    for flags in (8, 9, 0x0A, 0x0B, 0x18):
        for e in (4, 3, 2, 1):
            add(f'may-flags{flags:X}-enemies{e}', MAY_OP, 5, 0, f'aiflags:5,{flags}|counts:{4 + e},4,{e}')
    add('may-stamina0', MAY_OP, 5, 0, 'mod:5,Stamina,0')
    add('may-stamina0-fleeing', MAY_OP, 5, 0, 'mod:5,Stamina,0|aiflags:5,9')

    # ---- CheckFlee: 2d6 against its morale (rat 5: 4 with its leader's 5, by day, when a check is due).
    due = 'aiflags:5,0x0A'
    for r in ((1, 1), (2, 2), (2, 3), (3, 3), (6, 6)):
        add(f'check-due-roll{sum(r)}', CHECK_OP, 5, 1, due, script=d6(*r))
    add('check-notasked', CHECK_OP, 5, 0, due, script=d6(6, 6))
    add('check-notdue', CHECK_OP, 5, 1, '', script=d6(6, 6))
    add('check-morale12', CHECK_OP, 5, 1, due + '|morale:5,12', script=d6(6, 6))
    add('check-morale11', CHECK_OP, 5, 1, due + '|morale:5,11', script=d6(6, 6))
    add('check-morale-negative', CHECK_OP, 5, 1, due + '|morale:5,-5', script=d6(1, 2))
    add('check-engaged', CHECK_OP, 5, 1, due + '|cflags:5,0xB2', script=d6(6, 6))
    add('check-nofleepoints', CHECK_OP, 5, 1, due + '|nofleepoints', script=d6(6, 6))
    add('check-fleeing-stays', CHECK_OP, 5, 1, 'aiflags:5,0x0B|flight:5,2', script=d6(1, 1))
    add('check-fleeing-again', CHECK_OP, 5, 1, 'aiflags:5,0x0B|flight:5,2', script=d6(6, 6))
    add('check-enemies2', CHECK_OP, 5, 1, 'counts:6,4,2', script=d6(4, 4))
    add('check-enemies1', CHECK_OP, 5, 1, 'counts:5,4,1', script=d6(4, 4))
    add('check-stamina0', CHECK_OP, 5, 1, 'mod:5,Stamina,0', script=d6(3, 3))
    add('check-leader-morale', CHECK_OP, 5, 1, due + '|morale:4,11|daypart:4', script=d6(5, 5))
    add('check-leadergone', CHECK_OP, 5, 1, due + '|leadergone:1|leader:11', script=d6(2, 3))
    add('check-leader-unable', CHECK_OP, 5, 1, due + '|cflags:4,0x30|morale:4,11', script=d6(2, 3))
    add('check-leader-asks', CHECK_OP, 4, 1, 'aiflags:4,0x0A', script=d6(6, 6))
    # The morale spells: Aura of Death (25) and Solar Wrath (47) take their level off and make the check due.
    for kind in (25, 47):
        for power in (1, 3, 8):
            add(f'check-spell{kind}-power{power}', CHECK_OP, 5, 1, f'effect:5,{kind},{power},5,5', script=d6(3, 4))
            add(f'morale-spell{kind}-power{power}', MORALE_OP, 5, 1, f'morale:5,11|effect:5,{kind},{power},5,5')
            add(f'check-spell{kind}-power{power}-morale11', CHECK_OP, 5, 1, f'morale:5,11|effect:5,{kind},{power},5,5', script=d6(4, 4))
    # A weakness: ApplyResistance sets the check due and flag 0x10 together.
    add('check-weakness', CHECK_OP, 5, 1, 'aiflags:5,0x1A', script=d6(1, 2))
    add('check-weakness-roll', CHECK_OP, 5, 1, 'aiflags:5,0x1A', script=d6(2, 1))

    # ---- ReserveFleesOnArrival: by entity, with the enemies able more than half, against 2d6.
    RAT, GORGON, WOODSMAN = 0x26E, 0x217, 0x25B
    for eid, name in ((RAT, 'rat'), (GORGON, 'gorgon'), (WOODSMAN, 'woodsman')):
        for r in ((1, 1), (2, 3), (3, 3), (6, 6)):
            add(f'reserve-{name}-roll{sum(r)}', RESERVE_OP, eid, 0, '', script=d6(*r))
    add('reserve-rat-enemies2', RESERVE_OP, RAT, 0, 'counts:6,4,2', script=d6(6, 6))
    add('reserve-rat-enemies3', RESERVE_OP, RAT, 0, 'counts:7,4,3', script=d6(3, 3))
    add('reserve-rat-nofleepoints', RESERVE_OP, RAT, 0, 'nofleepoints', script=d6(6, 6))
    for part in range(5):
        add(f'reserve-rat-daypart{part}', RESERVE_OP, RAT, 0, f'daypart:{part}', script=d6(2, 3))
        add(f'reserve-woodsman-daypart{part}', RESERVE_OP, WOODSMAN, 0, f'daypart:{part}', script=d6(3, 3))
    for lm in (7, 8, 10, 11, 12):
        add(f'reserve-rat-leader{lm}', RESERVE_OP, RAT, 0, f'leader:{lm}', script=d6(3, 3))
    add('reserve-rat-leadergone', RESERVE_OP, RAT, 0, 'leadergone:1', script=d6(1, 1))
    add('reserve-rat-leader-unable', RESERVE_OP, RAT, 0, 'cflags:4,0x30', script=d6(1, 2))
    add('reserve-gorgon-leadergone-leader11', RESERVE_OP, GORGON, 0, 'leadergone:1|leader:11', script=d6(4, 4))

    # ---- A turn: styles 0-4 on checkpoint 05's rats. Rat 4 (66, 56) chooses among the party, Alaron nearest.
    for seed in (0, 1, 2, 3, 0x1234, 0xDEADBEEF):
        add(f'turn-style0-sort{seed:X}', TURN_OP, 4, 1, f'sortseed:{seed}')
        add(f'turn-style0-rat7-sort{seed:X}', TURN_OP, 7, 1, f'sortseed:{seed}')
    # Ties: rat 4 at (64, 53), 5 from Alaron and Rheda alike by the truncated distance, so the sort's draw picks.
    # The seeds make the sort's draw come up values spread in its high bits and its low.
    for value in (0x00000003, 0x40000001, 0x80000002, 0xC0000000, 0x12345678, 0x9ABCDEF1, 0x55555555, 0xFFFFFFFF):
        seed = seed_before(value)
        add(f'turn-style0-tie-sort{value:X}', TURN_OP, 4, 1, f'place:4,64,53|sortseed:{seed}')
        add(f'turn-style1-tie-sort{value:X}', TURN_OP, 4, 1, f'style:4,1|place:4,64,53|sortseed:{seed}')
    # Style 1, clever in melee: far off (only opponents' own cells), then beside Alaron (its attack cells scored).
    S1 = 'style:4,1'
    near = '|place:4,62,56'
    for seed in (1, 2, 3):
        add(f'turn-style1-far-sort{seed}', TURN_OP, 4, 1, S1 + f'|sortseed:{seed}')
        add(f'turn-style1-near-sort{seed}', TURN_OP, 4, 1, S1 + near + f'|sortseed:{seed}')
    add('turn-style1-near-above', TURN_OP, 4, 1, S1 + near + '|height:62,56,30|height:61,55,30|height:61,57,30')
    add('turn-style1-near-hurt', TURN_OP, 4, 1, S1 + near + '|cur:0,Endurance,3')
    add('turn-style1-near-bow', TURN_OP, 4, 1, S1 + near + '|weapon:0,0x71A')
    add('turn-style1-rheda', TURN_OP, 4, 1, S1 + '|place:4,58,51')
    # Styles 2-4 with a bow (0x71A) in a rat's mouth: what it fights as ranged.
    BOW = '|weapon:4,0x71A|action:4,2|kind:4,1,1'
    for style in (2, 3, 4):
        s = f'style:4,{style}' + BOW
        add(f'turn-style{style}-bow', TURN_OP, 4, 1, s)
        add(f'turn-style{style}-bow-near', TURN_OP, 4, 1, s + near)
        add(f'turn-style{style}-bow-far', TURN_OP, 4, 1, s + '|place:4,90,80')
        for seed in (1, 2):
            add(f'turn-style{style}-bow-sort{seed}', TURN_OP, 4, 1, s + f'|place:4,75,62|sortseed:{seed}')
    # Style 3 holding a melee weapon with no one near: the farthest cell, wanting its bow.
    add('turn-style3-melee-far', TURN_OP, 4, 1, 'style:4,3|kind:4,0,0|place:4,80,60')
    add('turn-style2-melee-far', TURN_OP, 4, 1, 'style:4,2|kind:4,0,0|place:4,80,60')

    # ---- A flight: rat 5 fleeing on checkpoint 05, toward the arena's flee points, and with none.
    F = 'aiflags:5,9|flight:5,4'
    add('turn-flee', TURN_OP, 5, -1, F)
    add('turn-flee-last', TURN_OP, 5, -1, 'aiflags:5,9|flight:5,1')
    add('turn-flee-nofleepoints', TURN_OP, 5, -1, F + '|nofleepoints')
    add('turn-flee-engaged', TURN_OP, 5, -1, F + '|place:5,61,57|cflags:5,0xB2|radius:5,1')
    add('turn-flee-checks', TURN_OP, 5, 1, 'aiflags:5,0x0A', script=d6(6, 6))
    add('turn-flee-checks-stays', TURN_OP, 5, 1, 'aiflags:5,0x0A', script=d6(1, 1))

    # ---- FleeAdvance after a flight's leg: the turns left, the distance from its turn's start, a flee point near.
    for left in (1, 2, 4):
        for moved, tag in ((0.5, 'still'), (1.0, 'unit'), (3.0, 'moved')):
            add(f'advance-left{left}-{tag}', ADVANCE_OP, 5, 0, f'aiflags:5,9|flight:5,{left}|origin:62,{58 - moved}')
    add('advance-notfleeing', ADVANCE_OP, 5, 0, 'origin:62,50')
    add('advance-notfleeing-near', ADVANCE_OP, 5, 0, 'place:5,61,56|origin:62,50')

    # ---- Real states: each enemy's turn as it began, with the dump's own stream.
    reals = [
        # Setup 13, the Gorgons at 0011 (slot 6) and 0012 (slot 5) after Solar Wrath, their leader (4) at 0016 with no Stamina.
        (S13, '0011', 6), (S13, '0012', 5), (S13, '0016', 4), (S13, '0028', 4), (S13, '0040', 4), (S13, '0053', 4),
        # Setup 4, the Bandit Woodsmen (style 3, a bow and a sword) at night.
        (S4, '0006', 4), (S4, '0008', 5), (S4, '0012', 7), (S4, '0014', 6), (S4, '0017', 5), (S4, '0021', 6), (S4, '0024', 4), (S4, '0036', 6),
        # Setup 5, the Goblin Poisoners (style 3).
        (S5, '0004', 7), (S5, '0006', 6), (S5, '0008', 5), (S5, '0018', 4), (S5, '0028', 5), (S5, '0046', 4), (S5, '0068', 5), (S5, '0093', 4),
    ]
    for folder, dump, slot in reals:
        src = log(folder, dump)
        tag = folder.rsplit('-', 1)[1]
        add(f'{tag}-{dump}-turn{slot}', TURN_OP, slot, 2, '', source=src, seed=seed_of(src))
        add(f'{tag}-{dump}-morale{slot}', MORALE_OP, slot, 1, '', source=src, seed=seed_of(src))
    return out
