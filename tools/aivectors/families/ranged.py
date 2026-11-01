"""Ranged attacks' and flasks' formulas. A shot (args the attacker's and the target's slots): Combatant.IsBehind (0x8005FB54),
Combat.RangedChance (0x80062F50) as ResolveRanged calls it, then ResolveRanged (0x80063324) with MeleeDamage, ApplyResistance and
WeaponSpellProc; each gives behind, the chance, the d100 and the roll as AdjustRoll turned it, the damage, the draws, the weapon skill's
base after and the target's AI flags after. A throw (args the thrower's and the target's slots and a mode: 1 a throw, 2 two, 3 a miss
burst about the cell the args go on to give): a flask put in the thrower's hand, ThrowChance (0x80064EFC, with Combat.WeatherPenalty) as
Combatant.ThrowFlask (0x80065600) asks for it, then ThrowFlask with FlaskDamage (0x80065270), and on a hit the blast as Combat.EndCast
calls it, Combatant.FlaskExplosion (0x800652F8); each gives the distance, the chance, the d100 and the roll, the damage kept on the thrower,
the landing cell, Thrown's base and the throw's draws, then each combatant's hit points, Stamina, protection (TotalProtection's, no shield)
and damage byte, those able to act and the draws."""
import state
from draws import d100, value_for

FAMILY = 'Ranged'

IS_BEHIND = 0x8005FB54
RANGED_CHANCE = 0x80062F50
RESOLVE_RANGED = 0x80063324
CELL_X, CELL_Z = 0x8005EE90, 0x8005EEE4

SEED = 0x1234


# ---- the family's pokes, which tests/HBHT.Rom.Tests/Formulas/RangedVectorTests.cs mirrors
def _weather(st, a):
    # The world's class of bad weather (World+4): 0 none, 1 fog, 2 storm.
    st.cpu.wr8(st.cpu.rd32(state.WORLD) + 4, int(a[0], 0))


def _rain(st, a):
    # The rain's intensity (World+8, f32), which RangedChance reads whatever falls.
    st.S.setfl(st.cpu.rd32(state.WORLD) + 8, float(a[0]))


def _moves(st, a):
    # The moves counted this round (+0x22), which a shot's and a throw's target is read by.
    st.cpu.wr8(st.record(int(a[0], 0)) + 0x22, int(a[1], 0))


def _moved(st, a):
    # The moved flag (+0x2C bit 3), a thrower's own.
    p = st.record(int(a[0], 0))
    st.cpu.wr16(p + 0x2C, st.cpu.rd16(p + 0x2C) | 8)


def _flask(st, a):
    # A flask in hand (Combatant.SetSpellAction): the action kept (+0x24), action 3 (+0x25), the kind (+0x27, 0 Fire, 1 Inferno, 2 Sleep
    # Gas, 3 Acid: item 0x1000 + kind) and the attack flying (+0x8A).
    p = st.record(int(a[0], 0))
    if st.cpu.rd8(p + 0x25) != 3:
        st.cpu.wr8(p + 0x24, st.cpu.rd8(p + 0x25))
    st.cpu.wr8(p + 0x25, 3)
    st.cpu.wr8(p + 0x27, int(a[1], 0))
    st.cpu.wr8(p + 0x8A, 1)


def _protection(st, a):
    # The sheet's protection byte (entity+0x14, +2).
    st.cpu.wr8(st.cpu.rd32(st.entity(int(a[0], 0)) + 0x14) + 2, int(a[1], 0))


POKES = {'weather': _weather, 'rain': _rain, 'moves': _moves, 'moved': _moved, 'flask': _flask, 'protection': _protection}

THROW_FLASK = 0x80065600
THROW_CHANCE = 0x80064EFC
GET_CELL = 0x8005EDD0             # Combatant.GetCell(record, &x, &z)
DISTANCE_TO = 0x8006035C          # Combatant.DistanceTo(a, b), f32
FLASK_EXPLOSION = 0x800652F8
HP = 0x8006E93C                   # Character.Hp(entity)
POTION_VISUAL = 0x8008AA28       # CombatStatIndicator.StartPotion, AttachSpellVisual's visual on a body
TOTAL_PROTECTION = 0x8005F570     # Combatant.TotalProtection(record): the sheet's, the armor's, Warrior / 5, the potions'; no shield


def shot(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=POKES, seed=v['seed'], script=v.get('script'))
    a, t = v['args'][:2]
    pa, pt = st.record(a), st.record(t)
    weapon = st.cpu.rd32(st.entity(a) + 0x24)
    skill = state.SKILLS[12 + st.cpu.rd8(weapon + 0x24)]

    behind = st.call(IS_BEHIND, [pa, pt]) & 0xFF
    cx, cz = st.call(CELL_X, [pt]) & 0xFF, st.call(CELL_Z, [pt]) & 0xFF
    chance = st.call(RANGED_CHANCE, [pa, pt, cx, cz], sp_args=[behind])
    chance = chance - 0x10000 if chance & 0x8000 else chance & 0xFFFF
    assert not st.draws.drawn, 'RangedChance drew'

    damage = st.call(RESOLVE_RANGED, [pa, pt]) & 0xFFFF
    drawn = st.draws.drawn
    roll = (100 * drawn[0][2] >> 32) + 1
    adjusted = roll
    if len(drawn) > 1 and drawn[1][3] == 'Combatant.AdjustRoll':
        adjusted = roll - ((100 * drawn[1][2] >> 32) + 1)
    ai = st.cpu.rd32(pt + 0x44)
    expect = [('behind', behind), ('chance', chance), ('roll', roll), ('adjusted', adjusted), ('damage', damage), ('draws', len(drawn)),
              ('skill', st.skill(a, skill, 'base')), ('aiflags', st.cpu.rd8(ai + 9) if ai else 0)]
    return expect, st.draws.callers()


def sx(p):
    # A pointer as lw leaves it, sign-extended: FlaskExplosion compares records against the target's by the whole register.
    return p | 0xFFFFFFFF00000000 if p & 0x80000000 else p


def throw(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=POKES, seed=v['seed'], script=v.get('script'))
    # The potion's visual on each body the blast reaches (AttachSpellVisual's call) reaches the hardware: stubbed, its handle 0.
    st.cpu.hooks[POTION_VISUAL] = lambda cpu: 0
    a, t = v['args'][:2]
    pa, pt = st.record(a), st.record(t)
    action, kind = st.cpu.rd8(pa + 0x25), st.cpu.rd8(pa + 0x27)
    assert action == 3 and kind < 4, f'no flask in hand: action {action}, kind {kind}'
    # The chance as ThrowFlask asks for it: the target's cell (GetCell) and the distance truncated to a byte.
    cell = state.alloc(st.cpu, 4)
    st.call(GET_CELL, [pt, cell, cell + 1])
    cx, cz = st.cpu.rd8(cell), st.cpu.rd8(cell + 1)
    st.call(DISTANCE_TO, [pa, pt])
    distance = int(st.cpu.fs(0)) & 0xFF
    chance = st.call(THROW_CHANCE, [pa, pt, cx, cz], sp_args=[distance])
    chance = chance - 0x10000 if chance & 0x8000 else chance & 0xFFFF
    assert not st.draws.drawn, 'ThrowChance drew'
    combat = st.combat
    st.cpu.wr16(combat + 0x5444, 0xFFFF)
    st.cpu.wr8(pa + 0x29, 0)
    st.call(THROW_FLASK, [sx(pa), sx(pt)])
    drawn = list(st.draws.drawn)
    roll = (100 * drawn[0][2] >> 32) + 1
    adjusted = roll
    if len(drawn) > 1 and drawn[1][3] == 'Combatant.AdjustRoll':
        adjusted = roll - ((100 * drawn[1][2] >> 32) + 1)
    damage = st.cpu.rd8(pa + 0x29)
    cx, cz = st.cpu.rd8(combat + 0x5444), st.cpu.rd8(combat + 0x5445)
    hit = cx != 0xFF
    expect = [('distance', distance), ('chance', chance), ('roll', roll), ('adjusted', adjusted), ('hit', int(hit)), ('damage', damage), ('cell', f'{cx},{cz}' if hit else '-'),
              ('skill', st.skill(a, 'Thrown', 'base')), ('throwdraws', len(drawn))]
    if hit:
        # Combat.EndCast bursts it with the thrower's action and kind as they were (ConsumeFlask may have put the hand back: the last of
        # a stack, which the port bursts, the user's choice).
        st.cpu.wr8(pa + 0x25, 3)
        st.cpu.wr8(pa + 0x27, kind)
        for i in range(14):
            if st.record(i):
                st.cpu.wr8(st.record(i) + 0x29, 0)
        st.call(FLASK_EXPLOSION, [sx(pa), sx(pt), damage])
    elif v['args'][2] == 3:
        # Combat.EndCast bursts a missed flask too while the hand still holds one (the party has more): the thrower's damage byte as it
        # was (0) and the last flask's cell (Combat+0x5444), poked here; Acid's corrosion lands all the same.
        st.cpu.wr8(combat + 0x5444, v['args'][3])
        st.cpu.wr8(combat + 0x5445, v['args'][4])
        st.cpu.wr8(pa + 0x25, 3)
        st.cpu.wr8(pa + 0x27, kind)
        st.call(FLASK_EXPLOSION, [sx(pa), sx(pt), st.cpu.rd8(pa + 0x29)])
    if v['args'][2] == 2 and hit:
        # A second throw of the same, its own blast after (Acid's corrosion stacking).
        st.cpu.wr8(pa + 0x25, 3)
        st.cpu.wr8(pa + 0x27, kind)
        st.cpu.wr16(combat + 0x5444, 0xFFFF)
        st.call(THROW_FLASK, [sx(pa), sx(pt)])
        if st.cpu.rd8(combat + 0x5444) != 0xFF:
            st.cpu.wr8(pa + 0x25, 3)
            st.cpu.wr8(pa + 0x27, kind)
            st.call(FLASK_EXPLOSION, [sx(pa), sx(pt), st.cpu.rd8(pa + 0x29)])
    for i in range(14):
        p = st.record(i)
        if not p:
            continue
        e = st.entity(i)
        expect.append((f's{i}', f'{st.call(HP, [e]) & 0xFFFF}/{st.stat(i, "Stamina")}/{st.call(TOTAL_PROTECTION, [p]) & 0xFF}/'
                                f'{st.cpu.rd8(p + 0x29)}'))
    # Those able to act (Combat+2, +4, +6): a blast whose share is at least the target's hit points left kills it (CheckDeath) before
    # its blow lands.
    expect.append(('active', ','.join(str(st.cpu.rd8(combat + o)) for o in (2, 4, 6))))
    expect.append(('draws', len(st.draws.drawn)))
    return expect, st.draws.callers()


def run(v):
    return throw(v) if len(v['args']) > 2 else shot(v)


# ---- the vectors

# Checkpoint 05: Alaron (0, Solar, Warrior 6, a shield) west of rat 4 (Lunar), both facing east. B: a Short Bow in his hand (Missile, hit
# 35, 2 dice), and the rat turned to face him with its Dexterity and Level raised, for a chance near 100 from the front at distance 6.
BOW = 0x71C
B = f'weapon:0,0x{BOW:X}|face:4,6|mod:4,Dexterity,40|base:4,Level,20'
# Missile weapons and thrown ones, by their element: the Long Bow and Great Bow (Cutting), Heartseeker (Magic), the Bow of Thunder (Air),
# the Javelin and the Throwing Iron (Thrown, Cutting), the Throwing Knife (Physical), the Poison Dart, Dragon Fang (Star), the Cyclops
# Hurlstar (Physical), Spikes, and the Venom Spit (Spit, Water).
WEAPONS = {'LongBow': 0x71A, 'GreatBow': 0x718, 'HuntersBow': 0x719, 'Heartseeker': 0x755, 'BowOfThunder': 0x75E, 'BowOfShielding': 0x75F,
           'BowOfAccuracy': 0x760, 'Javelin': 0x730, 'ThrowingIron': 0x71D, 'ThrowingKnife': 0x765, 'PoisonDart': 0x75D, 'DragonFang': 0x75A,
           'Hurlstar': 0x74D, 'Spikes': 0x71B, 'DartOfDistance': 0x763, 'Hatchet': 0x72F, 'VenomSpit': 0x722}
HIT = {0: d100(30)}
FLASKS = ('Fire', 'Inferno', 'SleepGas', 'Acid')


def d6(r):
    return value_for(r - 1, 6)

# The parity logs' states at a shot's or a throw's ExecuteAction (its dump): (the session, the dump, the attacker, the target).
SHOTS = {
    # Setup 4, Bandit Woodsmen with Long Bows at night in fog: shots at targets moved and not, the party's back, and Dougal's Javelin.
    'setup4': ('20261031-190448-setup4', [('0005', 1, 4), ('0007', 4, 0), ('0009', 5, 3), ('0011', 3, 5), ('0013', 7, 1), ('0019', 7, 0),
                                          ('0025', 4, 2), ('0031', 2, 7), ('0033', 3, 4), ('0037', 6, 2)]),
    # Setup 5, the Goblin Poisoners' Poison Darts.
    'setup5': ('20261031-194638-setup5', [('0005', 7, 2), ('0009', 5, 3), ('0013', 7, 2), ('0019', 4, 1), ('0029', 5, 3), ('0047', 4, 1),
                                          ('0066', 1, 4)]),
    # Setup 8, the Skeleton Archer's Hunter's Bow in a storm.
    'setup8': ('20261031-230020-setup8', [('0013', 4, 2), ('0038', 4, 2), ('0041', 4, 2)]),
    # Setup 3, Sholeh's Long Bow at the Giant Rats (its 0005 and 0019 are her healing, with the bow's action kept).
    'setup3': ('20261031-172849-setup3', [('0036', 1, 7), ('0116', 1, 4)]),
}


def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None):
        out.append(dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=list(args), seed=seed,
                        script=dict(script or {})))

    # ---- shots
    # The rolls, both ways, and Warrior's adjusted roll.
    for r in (1, 6, 7, 30, 60, 99, 100):
        add(f'shot-B-roll{r}', (0, 4), B, script={0: d100(r)})
    add('shot-plain-roll30', (0, 4), f'weapon:0,0x{BOW:X}', script=HIT)

    # The distance past 6: along the row, and on a slant whose length truncates (8.6 to 8, 10.8 to 10).
    for d in (3, 5, 6, 7, 8, 9, 12, 20, 33, 50, 60):
        add(f'shot-B-distance{d}', (0, 4), B + f'|place:0,{66 - d},56', script=HIT)
    for x, z in ((59, 51), (57, 50), (56, 46), (60, 60)):
        add(f'shot-B-slant{x}-{z}', (0, 4), B + f'|place:0,{x},{z}', script=HIT)

    # The rain's intensity, at distance 6 and 20, and the weather's class, which a shot doesn't read.
    for rain in ('0.1', '0.25', '0.5', '0.75', '1', '0.33'):
        add(f'shot-B-rain{rain}', (0, 4), B + f'|rain:{rain}', script=HIT)
        add(f'shot-B-rain{rain}-far', (0, 4), B + f'|rain:{rain}|place:0,46,56', script=HIT)
    for w in (1, 2):
        add(f'shot-B-weather{w}', (0, 4), B + f'|weather:{w}|place:0,46,56', script=HIT)

    # Each part of the day, for a Solar archer and a Lunar one; the night with rain.
    for part in range(5):
        add(f'shot-B-daypart{part}', (0, 4), B + f'|daypart:{part}', script=HIT)
        add(f'shot-B-daypart{part}-lunar', (0, 4), B + f'|daypart:{part}|aspect:0,1', script=HIT)
    add('shot-B-night-rain', (0, 4), B + '|daypart:4|rain:0.6|place:0,50,56', script=HIT)

    # The target having moved (+0x22 1 to 3) or acted (its flag, which a shot doesn't read), and the archer having moved.
    for m in (1, 2, 3):
        add(f'shot-B-moves{m}', (0, 4), B + f'|moves:4,{m}', script=HIT)
    add('shot-B-target-acted', (0, 4), B + '|acted:4,0', script=HIT)
    add('shot-B-own-moved', (0, 4), B + '|moves:0,3|moved:0', script=HIT)
    add('shot-B-moves2-night-rain', (0, 4), B + '|moves:4,2|daypart:4|rain:0.5', script=HIT)

    # No Stamina either side, a held target, the height, from behind, the Troubadour.
    add('shot-B-target-stamina0', (0, 4), B + '|mod:4,Stamina,0', script=HIT)
    add('shot-B-own-stamina0', (0, 4), B + '|mod:0,Stamina,0', script=HIT)
    for kind in (34, 53):
        add(f'shot-B-held{kind}', (0, 4), B + f'|effect:4,{kind},3,3,5', script=HIT)
    for h in (-6, -2, 2, 6):
        add(f'shot-B-height{h:+d}', (0, 4), B + f'|height:60,56,{20 + h}|height:66,56,20', script=HIT)
    add('shot-behind', (0, 4), f'weapon:0,0x{BOW:X}|mod:4,Dexterity,40|base:4,Level,20', script=HIT)
    for b in (1, 5):
        add(f'shot-B-troubadour{b}', (0, 4), B + f'|troubadour:{b}', script=HIT)
    for s in (0, 3, 8):
        add(f'shot-B-missile{s}', (0, 4), B + f'|skill:0,Missile,{s}', script=HIT)

    # The rat's shot at Alaron: his shield always counts in the damage, from the front or behind.
    R = f'weapon:4,0x{BOW:X}|face:4,6|mod:4,Strength,40|base:4,Level,10|mod:0,Dexterity,10'
    add('shot-R-front', (4, 0), R, script=HIT)
    add('shot-R-behind', (4, 0), R + '|place:4,59,56|face:4,2', script=HIT)
    add('shot-R-noshield-behind', (4, 0), R + '|noshield:0|place:4,59,56|face:4,2', script=HIT)

    # Each missile and thrown weapon in his hand, at 6 and at 15, and the resistances to its element.
    for name, w in WEAPONS.items():
        add(f'shot-{name}', (0, 4), B.replace(f'0x{BOW:X}', f'0x{w:X}'), script=HIT)
        add(f'shot-{name}-far', (0, 4), B.replace(f'0x{BOW:X}', f'0x{w:X}') + '|place:0,51,56', script=HIT)
    add('shot-Heartseeker-magic0.5', (0, 4), B.replace(f'0x{BOW:X}', '0x755') + '|resist:4,0,9,0.5', script=HIT)
    add('shot-LongBow-physical1.5', (0, 4), B.replace(f'0x{BOW:X}', '0x71A') + '|resist:4,0,3,1.5', script=HIT)
    add('shot-ThrowingKnife-physical0', (0, 4), B.replace(f'0x{BOW:X}', '0x765') + '|resist:4,0,3,0', script=HIT)

    # The parity logs: each shot at its own ExecuteAction, at a roll of 10 and of 60.
    for tag, (folder, shots) in SHOTS.items():
        for dump, a, t in shots:
            for r in (10, 60):
                add(f'{tag}-{dump}-shot-{a}-{t}-roll{r}', (a, t), source=f'log:{folder}/battle-01:{dump}.rdram.gz', script={0: d100(r)})
    # Setup 4 and 8 again in clear weather and by day, and setup 4's bandits shooting from beyond 6.
    add('setup4-0007-shot-4-0-clear', (4, 0), 'weather:0|rain:0|daypart:2', source='log:20261031-190448-setup4/battle-01:0007.rdram.gz',
        script=HIT)
    add('setup8-0013-shot-4-2-clear', (4, 2), 'weather:0|rain:0', source='log:20261031-230020-setup8/battle-01:0013.rdram.gz', script=HIT)

    # ---- throws: args (thrower, target, 1). A hit at a roll of 30 draws the d100, every flask's dice (Fire's 4 at 1-4, Inferno's 8 at
    # 5-12, Acid's 2 at 13-14), the experience at 15, and Acid's radius at 16-17.
    def throw(tag, pokes='', a=0, t=4, source='cp05', script=None):
        add(f'throw-{tag}', (a, t, 1), pokes, source=source, script=script or HIT)

    six = d6(6)
    FIRE24 = {0: d100(30), **{i: six for i in range(1, 5)}}
    INFERNO48 = {0: d100(30), **{i: six for i in range(5, 13)}}
    ACID12 = {0: d100(30), 13: six, 14: six, 16: six, 17: six}
    for kind, name in enumerate(FLASKS):
        throw(f'{name}', f'flask:0,{kind}')
        throw(f'{name}-miss', f'flask:0,{kind}', script={0: d100(100)})
        throw(f'{name}-roll1', f'flask:0,{kind}', script={0: d100(1)})
        # Alaron beside the rat and another rat's footprint by it: the thrower's own side caught, and others left with 1.
        throw(f'{name}-close', f'flask:0,{kind}|place:0,64,56|place:7,66,58|place:5,67,55')
    throw('Fire-max', 'flask:0,0|place:0,63,56|place:7,66,58', script=FIRE24)
    throw('Inferno-max', 'flask:0,1', script=INFERNO48)
    throw('Inferno-max-close', 'flask:0,1|place:0,64,56|place:7,66,57', script=INFERNO48)
    # A rat of 31: a share of 16 leaves 15, at or under the share, so it's killed by the blast before the blow; 15 leaves 16.
    for total, dice in ((15, (6, 6, 2, 1)), (16, (6, 6, 3, 1))):
        throw(f'Fire-share{total}', 'flask:0,0', script={0: d100(30), **{1 + i: d6(r) for i, r in enumerate(dice)}})
    throw('SleepGas-weak', 'flask:0,2|place:7,66,57|mod:7,Stamina,10|mod:4,Stamina,12')
    throw('SleepGas-target-stamina5', 'flask:0,2|mod:4,Stamina,5')
    # The squared distance kept in a byte: 16 cells off is 256, so 0, so 1, the whole damage; 17 off is 289, so 33.
    for name, pk in (('wrap256', 'place:5,66,40'), ('wrap257', 'place:5,82,57'), ('wrap289', 'place:5,66,39'),
                     ('wrap250', 'place:5,81,51'), ('wrap512', 'place:5,82,72')):
        throw(f'Fire-{name}', f'flask:0,0|{pk}', script=FIRE24)
        throw(f'Inferno-{name}', f'flask:0,1|{pk}', script=INFERNO48)
    # Acid's corrosion: the radius 2d6, those within it losing radius / d protection, the total none under 1; Alaron caught beside it.
    throw('Acid-max', 'flask:0,3|place:0,64,56|place:7,66,58', script=ACID12)
    throw('Acid-max-armored', 'flask:0,3|place:0,64,56|place:7,66,58|protection:4,10|protection:7,40', script=ACID12)
    throw('Acid-max-wrap', 'flask:0,3|place:5,66,40|place:7,82,57', script=ACID12)
    add('throw-Acid-twice', (0, 4, 2), 'flask:0,3|place:0,64,56', script={0: d100(30), 13: six, 14: six, 16: six, 17: six})
    # Missed, and burst all the same about the last flask's cell, as EndCast does with more of the flask in hand: Acid corrodes there.
    for kind, name in enumerate(FLASKS):
        add(f'throw-{name}-miss-burst', (0, 4, 3, 63, 55), f'flask:0,{kind}|place:7,63,56', script={0: d100(100), 2: d6(6), 3: d6(5)})
    add('throw-Fire-twice', (0, 4, 2), 'flask:0,0|place:7,66,58', script={0: d100(30)})

    # The chance: the distance past 6, in fog, a storm and at night (the distance again in fog and storm), from a skilled thrower.
    T = 'flask:0,0|skill:0,Thrown,10|face:4,6|mod:4,Dexterity,30'
    for d in (6, 7, 8, 12, 20, 30):
        for w in (0, 1, 2):
            throw(f'T-distance{d}-weather{w}', T + f'|place:0,{66 - d},56|weather:{w}')
        throw(f'T-distance{d}-night', T + f'|place:0,{66 - d},56|daypart:4')
        throw(f'T-distance{d}-storm-night', T + f'|place:0,{66 - d},56|weather:2|daypart:4')
    throw('T-slant', T + '|place:0,57,50|weather:1')
    throw('T-rain', T + '|rain:1|place:0,50,56')
    for part in range(5):
        throw(f'T-daypart{part}', T + f'|daypart:{part}')
        throw(f'T-daypart{part}-lunar', T + f'|daypart:{part}|aspect:0,1')
    for m in (1, 3):
        throw(f'T-moves{m}', T + f'|moves:4,{m}')
    throw('T-target-acted', T + '|acted:4,0')
    throw('T-own-moved', T + '|moved:0')
    throw('T-own-moves3', T + '|moves:0,3')
    throw('T-target-stamina0', T + '|mod:4,Stamina,0')
    throw('T-own-stamina0', T + '|mod:0,Stamina,0')
    throw('T-behind', 'flask:0,0|skill:0,Thrown,10|mod:4,Dexterity,30')
    throw('T-held34', T + '|effect:4,34,3,3,5')
    throw('T-noshield', T + '|noshield:4')
    for h in (-6, 6):
        throw(f'T-height{h:+d}', T + f'|height:60,56,{20 + h}|height:66,56,20')
    for b in (1, 5):
        throw(f'T-troubadour{b}', T + f'|troubadour:{b}')
    for s in (0, 3, 6, 9):
        throw(f'T-thrown{s}', f'flask:0,0|skill:0,Thrown,{s}|face:4,6|mod:4,Dexterity,30')

    # A rat made a thrower throws at the party: Alaron the target, Rheda beside him; the AI's flask isn't used up.
    RT = 'skill:4,Thrown,10|mod:4,Intelligence,30|mod:4,Dexterity,30|place:1,61,56'
    for kind, name in enumerate(FLASKS):
        throw(f'rat-{name}', f'flask:4,{kind}|{RT}', a=4, t=0)
    throw('rat-Inferno-max', f'flask:4,1|{RT}|place:2,60,58', a=4, t=0, script=INFERNO48)

    # Thrown's training: a d100 at or under a tenth of (8 - skill)^2 (5 from 6), half that on a miss.
    for skill, hit in ((0, True), (0, False), (3, True), (6, True)):
        xp = ((8 - skill) ** 2 if skill < 6 else 5) >> (0 if hit else 1)
        for r in sorted({max(int(xp * 0.1), 1), int(xp * 0.1) + 1}):
            sc = {0: d100(30 if hit else 100), (15 if hit else 1): d100(r)}
            throw(f'xp-thrown{skill}-{"hit" if hit else "miss"}-{r}', f'flask:0,0|skill:0,Thrown,{skill}', script=sc)

    # Setup 3's four flasks, each at its own ExecuteAction: Acid catching Keelin, Fire, Inferno and Sleep Gas; a hit and a miss, and the
    # dice at their most.
    for dump, t, kind in (('0046', 7, 3), ('0064', 4, 0), ('0079', 4, 1), ('0088', 6, 2)):
        src = f'log:20261031-172849-setup3/battle-01:{dump}.rdram.gz'
        # The RAM has the flask in Dougal's hand already; the poke lays it over the port's battle, which the overlay doesn't.
        pk = f'flask:2,{kind}'
        for r in (10, 60, 100):
            throw(f'setup3-{dump}-{FLASKS[kind]}-roll{r}', pk, a=2, t=t, source=src, script={0: d100(r)})
        throw(f'setup3-{dump}-{FLASKS[kind]}-max', pk, a=2, t=t, source=src, script=(FIRE24, INFERNO48, HIT, ACID12)[kind])
    # And setup 4's bandits at night in fog, a flask in the hand of one, at the party.
    for dump, a, t in (('0007', 4, 0), ('0013', 7, 1)):
        src = f'log:20261031-190448-setup4/battle-01:{dump}.rdram.gz'
        throw(f'setup4-{dump}-{a}-{t}-Fire', f'flask:{a},0', a=a, t=t, source=src)
        for kind in (0, 1):
            throw(f'setup4-{dump}-{a}-{t}-{FLASKS[kind]}-skilled', f'flask:{a},{kind}|skill:{a},Thrown,10|mod:{a},Dexterity,40', a=a, t=t,
                  source=src, script={0: d100(10)})
    return out
