"""Setup: BattleSetup.FillEnemies (0x80009374, with SureKinds, ChooseKinds and TrimByLevel) on a monster party's kinds and sizes,
BattleSetup.ChooseArena (0x80008698) by the world's terrain, Party.BattleAmbushRoll (0x80076C60) and Combat.SpawnPosition (0x8005C294),
on the Combat checkpoints and the parity logs' states with one input changed at a time: parties of every shape the game has and the edges
past them, the party's level against the enemies' for the trim, every terrain, the Ranger's skill, Intelligence and Stamina with the roll
at its margins, and the setup block's collision and animation bytes with each roll for each side; and setup 9's eight battles at the
original's seeds. Args are the function (0 FillEnemies: least, most, then each kind's entity, least and most; 1 ChooseArena: the terrain;
2 the Ranger's roll; 3 SpawnPosition: party, reserve, letter and roll). Each vector gives what the function came to (the enemy list; the
arena, or none where nothing's written; the roll and each member's current and modified Stamina after; the spawn's cell and direction)
and the main stream's draws."""
import state
from draws import d100

FAMILY = 'Setup'

FILL_ENEMIES = 0x80009374
CHOOSE_ARENA = 0x80008698
AMBUSH_ROLL = 0x80076C60
SPAWN_POSITION = 0x8005C294
SPRINTF = 0x800C2F80              # FillEnemies' debug lines, stubbed for speed (FindSpawnPoint's makes the point's name)
PARTY = 0x800D9684                # GameState.Party: a pointer to the party, its four members first
WORLD = 0x800E303C                # World: +0x24 the terrain
SETUP = 0x800D9458                # the setup block's enemy list; +0x26 the collision byte, +0x27 the arena, +0x28 the animation byte
NONE = 0xEE                       # an arena byte no terrain writes

SEED = 0x1234
FILL, ARENA, AMBUSH, SPAWN = range(4)


def _block(st, a):
    # block:collision,animation: the setup block's bytes (0x800D947E, 0x800D9480).
    st.cpu.wr8(SETUP + 0x26, state._int(a[0]))
    st.cpu.wr8(SETUP + 0x28, state._int(a[1]))


EXTRA = {'block': _block}


def members(st):
    party = st.cpu.rd32(PARTY)
    return [st.cpu.rd32(party + 4 * i) for i in range(4)]


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=EXTRA, seed=v['seed'], script=v.get('script'))
    c = st.cpu
    kind, args = v['args'][0], v['args'][1:]
    if kind == FILL:
        c.hooks[SPRINTF] = lambda cpu: 0
        least, most, kinds = args[0], args[1], args[2:]
        obj = state.alloc(c, 0x80)
        for i in range(0x80):
            c.wr8(obj + i, 0)
        p = obj + 0x28                 # the payload: eight kinds of (u16 entity, u8 least, u8 most), the least at +0x28, the most +0x2A
        for i in range(0, len(kinds), 3):
            c.wr16(p + 4 * (i // 3), kinds[i])
            c.wr8(p + 4 * (i // 3) + 2, kinds[i + 1])
            c.wr8(p + 4 * (i // 3) + 3, kinds[i + 2])
        c.wr16(p + 0x28, least)
        c.wr16(p + 0x2A, most)
        st.call(FILL_ENEMIES, [p])
        enemies = [c.rd16(SETUP + 2 * i) for i in range(12)]
        expect = [('enemies', ','.join(f'0x{e:04X}' for e in enemies if e) or '-')]
    elif kind == ARENA:
        c.wr8(c.rd32(WORLD) + 0x24, args[0])
        c.wr8(SETUP + 0x27, NONE)
        st.call(CHOOSE_ARENA, [SETUP])
        arena = c.rd8(SETUP + 0x27)
        expect = [('arena', 'none' if arena == NONE else arena)]
    elif kind == AMBUSH:
        roll = st.call(AMBUSH_ROLL, [c.rd32(PARTY)]) & 0xFF
        stamina = [f'{st.stat(i, "Stamina", "current")}/{st.stat(i, "Stamina")}' for i, m in enumerate(members(st)) if m]
        assert all(st.entity(i) == m for i, m in enumerate(members(st)) if m), 'a member is not its slot'
        expect = [('roll', roll), ('stamina', ','.join(stamina))]
    else:
        party, reserve, letter, roll = args
        out = state.alloc(c, 0x10)
        st.call(SPAWN_POSITION, [st.combat, out, out + 1, out + 2], sp_args=[0, roll, party, reserve, letter])
        expect = [('x', c.rd8(out)), ('z', c.rd8(out + 1)), ('direction', c.rd8(out + 2))]
    return expect + [('draws', len(st.draws.drawn))], st.draws.callers()


# ---- the vectors

# Monster parties of each shape the game places (the map, the party's index: least, most, kinds), and the shapes past them: the least
# above the most, a kind whose least is above its most, the most past 12, no kinds, duplicate kinds, sure ones past the most.
PARTIES = [
    ('0x0E96-0', 1, 1, [(0x028B, 1, 1)]),
    ('0x0E96-1', 1, 12, [(0x0279, 2, 2)]),
    ('0x0EAA-9', 4, 8, [(0x0222, 3, 6), (0x0223, 1, 2)]),
    ('0x0EAA-13', 8, 8, [(0x021B, 3, 3), (0x021C, 1, 1), (0x0279, 4, 4)]),
    ('0x0ECB-11', 8, 8, [(0x0212, 1, 1), (0x024B, 1, 1), (0x0245, 3, 3), (0x0210, 3, 3)]),
    ('0x0ECB-13', 1, 12, []),
    ('0x0EE0-7', 1, 16, []),
    ('0x0EEA-0', 4, 9, [(0x0222, 4, 6), (0x0223, 0, 3)]),
    ('0x0F0B-7', 7, 9, [(0x021B, 4, 5), (0x024F, 1, 2), (0x021B, 1, 1), (0x021C, 1, 1)]),
    ('0x0F1B-3', 6, 12, [(0x024F, 1, 1), (0x021B, 3, 4), (0x0279, 2, 7)]),
    ('0x0F4D-0', 1, 12, [(0x021B, 1, 4), (0x025B, 1, 4), (0x021C, 1, 2)]),
    ('0x0F68-7', 5, 7, [(0x0216, 1, 1), (0x024E, 2, 2), (0x0215, 2, 4)]),
    ('0x0F71-3', 2, 2, [(0x0279, 2, 2), (0x0279, 2, 2)]),
    ('0x0F79-4', 9, 12, [(0x020F, 2, 4), (0x0207, 6, 8), (0x027A, 1, 1)]),
    ('0x0F79-9', 11, 12, [(0x0211, 1, 1), (0x0210, 3, 3), (0x020F, 3, 3), (0x027A, 4, 4), (0x0207, 0, 1)]),
    ('0x0F9B-5', 6, 10, [(0x0288, 2, 2), (0x0287, 1, 1), (0x0288, 1, 3), (0x0287, 1, 1), (0x0288, 1, 3)]),
    ('0x0F9E-6', 12, 12, [(0x0210, 3, 3), (0x024B, 3, 3), (0x0210, 3, 3), (0x0211, 3, 3)]),
    ('0x0FF0-5', 2, 2, [(0x020F, 2, 1)]),
    ('0x1026-4', 9, 12, [(0x0251, 2, 2), (0x026D, 5, 8), (0x0251, 2, 2)]),
    ('0x1026-5', 1, 1, [(0x0251, 1, 1), (0x0251, 1, 1)]),
    ('0x102F-9', 8, 12, [(0x0223, 1, 1), (0x0222, 4, 6), (0x025C, 3, 5)]),
    ('0x1031-12', 8, 11, [(0x0265, 3, 3), (0x0207, 4, 6), (0x027A, 1, 2)]),
    ('0x10B2-4', 3, 9, [(0x024B, 1, 3), (0x027C, 1, 3), (0x0210, 1, 3)]),
    ('0x10CF-1', 2, 12, [(0x020F, 1, 6), (0x027C, 1, 4), (0x027B, 0, 2)]),
    ('0x10D2-2', 5, 10, [(0x0211, 2, 4), (0x024B, 1, 2), (0x0210, 2, 4)]),
    ('0x10F2-3', 12, 12, [(0x027B, 1, 1), (0x0210, 12, 12)]),
    ('0x1139-1', 5, 2, [(0x021F, 1, 1), (0x0220, 1, 1)]),
    ('0x1328-2', 1, 16, [(0x02A2, 1, 1)]),
    ('0x14B7-6', 1, 1, [(0x0228, 3, 1)]),
    # Past the game's: the least never reached (none at all, the Skeleton); extras to 12 with a most of 16; eight kinds.
    ('least-unmet', 10, 12, [(0x0222, 1, 3), (0x0223, 1, 3)]),
    ('extras-to-12', 1, 16, [(0x0279, 2, 12), (0x0222, 2, 12)]),
    ('eight-kinds', 1, 12, [(0x0279 + i, 1, 2) for i in range(8)]),
    ('most-0', 0, 0, [(0x0279, 2, 2)]),
    ('sure12-most16', 1, 16, [(0x0279, 12, 12), (0x0222, 0, 3)]),
]

# The trim: ten of a level-25 kind (the Dark Knight, 0x020C) and two kinds of different levels, against the party's modified Level.
TRIM = [(0x020C, 10, 10)]
TRIM2 = [(0x020C, 5, 5), (0x0279, 5, 5)]

SETUP9 = '20261031-233707-setup9'
# Each battle: the seed as FromMonsterParty began (its ChooseArena, then FillEnemies) and as Combat.Setup began (the Ranger's roll).
SETUP9_SEEDS = {1: (0xE137DD5D, 0xCB7C21AA), 2: (0x0D49E6BA, 0x224A2C2E), 3: (0x87D8E714, 0x89EC8228), 4: (0x3D07587B, 0x499EC783),
                5: (0x87C8ADD4, 0x9E5379C6), 6: (0x47809F3C, 0xB760A9EF), 7: (0xFFFD4098, 0x5BE793EC), 8: (0xCEAA1806, 0xFEE4BC51)}
WOLVES = (4, 4, [(0x0279, 4, 4)])


def fill_args(least, most, kinds):
    return [FILL, least, most] + [x for k in kinds for x in k]


def level(n):
    return '|'.join(f'mod:{i},Level,{n}' for i in range(4))


def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None):
        out.append(dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=list(args), seed=seed,
                        script=dict(script or {})))

    # FillEnemies: every shape at three seeds, the party's level 20 (cp05's); and the extras' rolls at their least and most.
    for name, least, most, kinds in PARTIES:
        for seed in (SEED, 0xDEADBEEF, 0x80000001):
            add(f'fill-{name}-seed{seed:X}', fill_args(least, most, kinds), seed=seed)
        extras = sum(1 for _, lo, hi in kinds if lo < hi)
        if extras:
            add(f'fill-{name}-least', fill_args(least, most, kinds), script={i: 0 for i in range(extras)})
            add(f'fill-{name}-most', fill_args(least, most, kinds), script={i: 0xFFFFFFFF for i in range(extras)})
    # The trim by the party's level over the enemies' in thirds: 0.3 below a third, 0.2 below two, 0.1 below the whole, none at it.
    for n in (1, 8, 9, 16, 17, 24, 25, 30):
        add(f'fill-trim-level{n}', fill_args(10, 10, TRIM), level(n))
        add(f'fill-trim2-level{n}', fill_args(10, 10, TRIM2), level(n))
        add(f'fill-trim-extras-level{n}', fill_args(1, 12, [(0x020C, 2, 8), (0x0279, 1, 4)]), level(n), seed=0xDEADBEEF)
    add('fill-trim-mixed-levels', fill_args(10, 10, TRIM2), 'mod:0,Level,40|mod:1,Level,1|mod:2,Level,10|mod:3,Level,3')

    # ChooseArena: every terrain, past the table too (nothing written), at two seeds; the two-way terrains each way, each range at its ends.
    for t in range(30):
        for seed in (SEED, 0xDEADBEEF):
            add(f'arena-terrain{t}-seed{seed:X}', [ARENA, t], seed=seed)
        add(f'arena-terrain{t}-least', [ARENA, t], script={0: 0, 1: 0})
        add(f'arena-terrain{t}-most', [ARENA, t], script={0: 0xFFFFFFFF, 1: 0xFFFFFFFF})
    for t in (20, 21, 22):
        add(f'arena-terrain{t}-first-most', [ARENA, t], script={0: 0, 1: 0xFFFFFFFF})
        add(f'arena-terrain{t}-second-least', [ARENA, t], script={0: 0xFFFFFFFF, 1: 0})

    # The Ranger's roll on cp05: Rheda (1) the best Ranger, 6, Intelligence 3: a target of 69, the steps by the margin, 0 from 69 up.
    for r in (1, 2, 5, 23, 24, 30, 43, 44, 60, 68, 69, 70, 100):
        add(f'ambush-roll{r}', [AMBUSH], script={0: d100(r)})
    for skill in (0, 1, 2, 4, 5, 9, 12):
        add(f'ambush-ranger{skill}', [AMBUSH], f'skill:1,Ranger,{skill}', script={0: d100(20)})
    add('ambush-none', [AMBUSH], ''.join(f'|skill:{i},Ranger,0' for i in range(4)).lstrip('|'))
    add('ambush-tie', [AMBUSH], 'skill:0,Ranger,6', script={0: d100(20)})
    add('ambush-better-later', [AMBUSH], 'skill:3,Ranger,7', script={0: d100(20)})
    for stamina in (0, 1, 2, 3):
        # Ranger 3 costs 2 Stamina: the best without it is passed over for the next.
        add(f'ambush-ranger3-stamina{stamina}', [AMBUSH], f'skill:1,Ranger,3|mod:1,Stamina,{stamina}|skill:2,Ranger,2', script={0: d100(5)})
    for intel in (0, 10, 30):
        add(f'ambush-int{intel}', [AMBUSH], f'mod:1,Intelligence,{intel}', script={0: d100(40)})
    add('ambush-big-target', [AMBUSH], 'mod:1,Intelligence,60|skill:1,Ranger,15', script={0: d100(1)})

    # SpawnPosition on setup 9's first battle (arena 5): each side and letter, a reserve, the ambush's roll by the animation byte.
    src = f'log:{SETUP9}/battle-01:0002.rdram.gz'
    for collision in (0, 1, 2):
        for animation in (0, 1, 2, 3):
            block = f'block:{collision},{animation}'
            for party in (1, 0):
                rolls = (0, 1, 2, 3, 4, 5) if collision == 2 and party else (0,)
                for roll in rolls:
                    add(f'spawn-c{collision}-a{animation}-{"party" if party else "enemy"}-roll{roll}', [SPAWN, party, 0, 1, roll], block,
                        source=src)
            add(f'spawn-c{collision}-a{animation}-reserve', [SPAWN, 0, 1, 2, 0], block, source=src)
    for letter in range(4):
        add(f'spawn-letter{letter}', [SPAWN, 1, 0, letter, 0], source=src)
        add(f'spawn-letter{letter}-enemy', [SPAWN, 0, 0, letter, 0], source=src)
    # Other arenas' points: setup 11's (26), setup 13's (12), setup 1's (27), checkpoint 05's.
    for tag, folder in (('setup11', '20261101-083418-setup11'), ('setup13', '20261101-102351-setup13'),
                        ('setup1', '20261031-105935-setup1')):
        for party in (1, 0):
            add(f'spawn-{tag}-{"party" if party else "enemy"}', [SPAWN, party, 0, 0, 0], 'block:2,1',
                source=f'log:{folder}/battle-01:0002.rdram.gz')
    add('spawn-ambush-party-roll4', [SPAWN, 1, 0, 3, 4], 'block:2,1')

    # Setup 9's eight battles at the original's seeds: the arena by its terrain and the wolves' list, the Ranger's roll as Combat.Setup
    # began, and the party's and the enemies' first spawns as the setup block has them.
    for b, (seed_from, seed_setup) in SETUP9_SEEDS.items():
        src = f'log:{SETUP9}/battle-{b:02d}:0002.rdram.gz'
        add(f'setup9-{b}-arena', [ARENA, 5], source=src, seed=seed_from)
        add(f'setup9-{b}-fill', fill_args(*WOLVES), source=src, seed=seed_from)
        add(f'setup9-{b}-ambush', [AMBUSH], source=src, seed=seed_setup)
        roll = run(out[-1])[0][0][1]
        for party in (1, 0):
            add(f'setup9-{b}-spawn-{"party" if party else "enemy"}', [SPAWN, party, 0, 0, roll], source=src)
    return out
