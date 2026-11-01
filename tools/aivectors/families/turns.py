"""Turns: Combatant.UpdateRadius (0x8005F29C) on each living combatant in slot order, then Combatant.TurnsPerRound (0x8005F4D0) on each,
then TurnOrder.Build (0x8006A16C) over the struct's turn order (+0x30), on the Combat checkpoints and the parity logs' states with one
input changed at a time: the Dexterity thresholds of both tables, Haste and Stellar Gravity, an opponent within reach, a performance, the
toggle, and combatants gone or unable. Each vector gives every slot's field radius, flags after (the engaged bit 7 and the performing bit
2), and turns a round; the Troubadour's bonus after; the order Build drew, its counts (+0x02, +0x04, +0x06) and the current combatant's
slot; and the main stream's draws."""
import state

FAMILY = 'Turns'

UPDATE_RADIUS = 0x8005F29C
TURNS_PER_ROUND = 0x8005F4D0
BUILD = 0x8006A16C
IS_DEAD = 0x8006EA54

SEED = 0x1234
HASTE, STELLAR_GRAVITY = 0x1E, 0x38


def _toggle(st, a):
    st.cpu.wr32(st.combat + 0x34, state._int(a[0]))


def _flags(st, a):
    # flags:slot,set,clear: the record's u16 flags (+0x2C), bits set and then cleared.
    p = st.record(state._int(a[0])) + 0x2C
    st.cpu.wr16(p, (st.cpu.rd16(p) | state._int(a[1])) & ~state._int(a[2]) & 0xFFFF)


EXTRA = {'toggle': _toggle, 'flags': _flags}


def living(st):
    n = st.cpu.rd8(st.combat + 1)
    return [i for i in range(n) if st.record(i) and not st.call(IS_DEAD, [st.entity(i)]) & 0xFF
            and not st.cpu.rd16(st.record(i) + 0x2C) & 0x40]


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=EXTRA, seed=v['seed'], script=v.get('script'))
    slots = living(st)
    radius, flags, turns = [], [], []
    for i in slots:
        st.call(UPDATE_RADIUS, [st.record(i)])
    assert not st.draws.drawn, 'UpdateRadius drew'
    for i in slots:
        p = st.record(i)
        radius.append(st.cpu.rd8(p + 0x21))
        flags.append(st.cpu.rd16(p + 0x2C) & 0x84)
        turns.append(st.call(TURNS_PER_ROUND, [p]) & 0xFF)
    c = st.combat
    st.call(BUILD, [c + 0x30])
    count = st.cpu.rd8(c + 0x30)
    order = [st.cpu.rd8(st.cpu.rd32(c + 0x38) + k) for k in range(count)]
    current = st.cpu.rd32(c + 0x2C)
    current = next((i for i in range(14) if current and st.record(i) == current), -1)
    expect = [('slots', ','.join(map(str, slots))), ('radius', ','.join(map(str, radius))), ('flags', ','.join(map(str, flags))),
              ('turns', ','.join(map(str, turns))), ('troubadour', st.cpu.rd8(c + 0x13)), ('order', ','.join(map(str, order))),
              ('active', st.cpu.rd8(c + 2)), ('party', st.cpu.rd8(c + 4)), ('enemies', st.cpu.rd8(c + 6)),
              ('current', current if order else -1), ('draws', len(st.draws.drawn))]
    return expect, st.draws.callers()


# ---- the vectors

def cases():
    out = []

    def add(tag, pokes='', source='cp02', seed=SEED):
        out.append(dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=[], seed=seed, script={}))

    # The plain states.
    for cp in (2, 3, 4, 5, 6, 9, 10, 11):
        add('plain', source=f'cp{cp:02d}')

    # The field's radius by modified Dexterity (Combat.RadiusByDexterity: 0, 0, 0, 3, 7, 12, 16, 21, 26, 28, 30, 33, 36, 38): both sides
    # of each step, past the table, and a byte that reads negative.
    for d in (0, 1, 3, 4, 7, 8, 12, 13, 16, 17, 21, 22, 26, 27, 28, 29, 30, 31, 33, 34, 36, 37, 38, 39, 60, 127, 128, 200):
        add(f'mod-dex{d}', f'mod:0,Dexterity,{d}')
    # Turns a round by base Dexterity (0, 1-17, 18-24 by the toggle, 25-29, 30-34 by the toggle, 35 up), each way of the toggle.
    for d in (0, 1, 17, 18, 24, 25, 29, 30, 31, 34, 35, 40, 128):
        for t in (0, 1):
            add(f'base-dex{d}-toggle{t}', f'base:0,Dexterity,{d}|toggle:{t}')
    # Every slot given each band, to make every group of the order drawn at once.
    bands = (0, 10, 20, 27, 32, 30, 18, 24)
    for t in (0, 1):
        add(f'bands-toggle{t}', ''.join(f'|base:{i},Dexterity,{d}' for i, d in enumerate(bands)).lstrip('|') + f'|toggle:{t}')
        for seed in (1, 0xDEADBEEF, 0x80000000):
            add(f'bands-toggle{t}-seed{seed:X}', ''.join(f'|base:{i},Dexterity,{d}' for i, d in enumerate(bands)).lstrip('|') + f'|toggle:{t}',
                seed=seed)

    # Haste adds its level, Stellar Gravity takes its level away (1 if that's all or more), in their slots' order, then 13 at most.
    for lvl in (1, 3, 5, 13, 250):
        add(f'haste{lvl}', f'effect:0,{HASTE},{lvl},3,5')
        add(f'haste{lvl}-dex38', f'mod:0,Dexterity,38|effect:0,{HASTE},{lvl},3,5')
    # Alaron's Dexterity of 27 (cp02) gives 9: Stellar Gravity of 8, 9 and 10 meet the edge.
    for lvl in (1, 5, 8, 9, 10, 20):
        add(f'gravity{lvl}', f'effect:0,{STELLAR_GRAVITY},{lvl},3,5')
        add(f'gravity{lvl}-dex1', f'mod:0,Dexterity,1|effect:0,{STELLAR_GRAVITY},{lvl},3,5')
    add('haste3-gravity5', f'effect:0,{HASTE},3,3,5|effect:0,{STELLAR_GRAVITY},5,3,5')
    add('gravity5-haste3', f'effect:0,{STELLAR_GRAVITY},5,3,5|effect:0,{HASTE},3,3,5')
    add('gravity20-haste3', f'effect:0,{STELLAR_GRAVITY},20,3,5|effect:0,{HASTE},3,3,5')
    add('haste-rat4', f'effect:4,{HASTE},4,3,5')
    add('gravity-rat4', f'effect:4,{STELLAR_GRAVITY},4,3,5')

    # An opponent within reach: Alaron (52, 52) walked east up to rat 6 (65, 52) across the ground, either side of their reach.
    for x in (61, 61.5, 62, 62.4, 62.45, 62.5, 62.55, 62.6, 63, 64):
        add(f'engaged-x{x}', f'place:0,{x},52')
        add(f'engaged-x{x}-haste', f'place:0,{x},52|effect:0,{HASTE},5,3,5')
    add('engaged-gone-rat', 'place:0,63,52|flags:6,0x40,0')
    add('engaged-unable-rat', 'place:0,63,52|flags:6,0,2')
    # Performing: 1 at most, and a performance stopped by an opponent within reach, the bonus gone with it.
    add('performing', 'flags:0,4,0|troubadour:5')
    add('performing-engaged', 'flags:0,4,0|troubadour:5|place:0,63,52')
    add('performing-rat', 'flags:4,4,0|troubadour:5')
    # Gone and unable: no turns, and counted in the active counts or not.
    add('gone-alaron', 'flags:0,0x40,0')
    add('unable-alaron', 'flags:0,0,2')
    add('unable-rat4-toggle1', 'flags:4,0,2|toggle:1')
    add('gone-all-rats', 'flags:4,0x40,0|flags:5,0x40,0|flags:6,0x40,0|flags:7,0x40,0')
    add('dex0-toggle1-unable', 'base:0,Dexterity,0|toggle:1|flags:0,0,2')
    add('dex0-toggle1', 'base:0,Dexterity,0|base:4,Dexterity,0|toggle:1')

    # The parity logs' states, each at its first turn's start: real entities' Dexterity and sizes, reserves joined, and each way of the toggle.
    logs = {
        'setup1': '20261031-105935-setup1', 'setup2': '20261031-123809-setup2', 'setup6': '20261031-205206-setup6',
        'setup7': '20261031-223128-setup7', 'setup8': '20261031-230020-setup8', 'setup10': '20261031-235130-setup10',
        'setup11': '20261101-083418-setup11', 'setup12b': '20261101-100932-setup12b', 'setup13': '20261101-102351-setup13',
    }
    for tag, folder in logs.items():
        for t in (0, 1):
            add(f'{tag}-toggle{t}', f'toggle:{t}', source=f'log:{folder}/battle-01:0002.rdram.gz')
    return out
