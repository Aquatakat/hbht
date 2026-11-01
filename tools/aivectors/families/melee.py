"""Melee's formulas: Combatant.IsBehind (0x8005FB54), MeleeChance (0x800622C4) as ResolveMelee calls it, then ResolveMelee (0x800628A4)
with MeleeDamage (0x80062AE4), ApplyResistance (0x80062704) and WeaponSpellProc (0x800627D0), on the Combat checkpoints and the parity
logs' states with one input changed at a time. Args are the attacker's and the target's slots. Each vector gives: behind, the chance, the
d100 and the roll as AdjustRoll turned it, the damage ResolveMelee returns, the main stream's draws, the attacker's weapon skill's base
after (GainSkillExperience), the target's AI flags after (a weakness makes its morale check due) and the attacker's cast margin after (a
weapon's spell keeps its own)."""
import state
from draws import d100

FAMILY = 'Melee'

IS_BEHIND = 0x8005FB54
MELEE_CHANCE = 0x800622C4
RESOLVE_MELEE = 0x800628A4
CELL_X, CELL_Z = 0x8005EE90, 0x8005EEE4

SEED = 0x1234


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), seed=v['seed'], script=v.get('script'))
    a, t = v['args']
    pa, pt = st.record(a), st.record(t)
    weapon = st.cpu.rd32(st.entity(a) + 0x24)
    skill = state.SKILLS[12 + st.cpu.rd8(weapon + 0x24)]

    # The chance as ResolveMelee asks for it: the target's cell, and whether the blow is from behind.
    behind = st.call(IS_BEHIND, [pa, pt]) & 0xFF
    cx, cz = st.call(CELL_X, [pt]) & 0xFF, st.call(CELL_Z, [pt]) & 0xFF
    chance = st.call(MELEE_CHANCE, [pa, pt, cx, cz], sp_args=[behind])
    assert not st.draws.drawn, 'MeleeChance drew'

    damage = st.call(RESOLVE_MELEE, [pa, pt]) & 0xFFFF
    drawn = st.draws.drawn
    roll = (100 * drawn[0][2] >> 32) + 1
    adjusted = roll
    if len(drawn) > 1 and drawn[1][3] == 'Combatant.AdjustRoll':
        adjusted = roll - ((100 * drawn[1][2] >> 32) + 1)
    ai = st.cpu.rd32(pt + 0x44)
    margin = st.cpu.rd8(st.entity(a) + 0x3C)
    expect = [('behind', behind), ('chance', chance), ('roll', roll), ('adjusted', adjusted), ('damage', damage), ('draws', len(drawn)),
              ('skill', st.skill(a, skill, 'base')), ('aiflags', st.cpu.rd8(ai + 9) if ai else 0),
              ('margin', margin - 256 if margin & 0x80 else margin)]
    return expect, st.draws.callers()


# ---- the vectors

# Checkpoint 05: Alaron (0, Solar, Short Sword of Cutting, Warrior 6, a shield, Troubadour 5) and rat 4 (Lunar, Rat Bite), Alaron west of
# it, both facing east, so his blow is from behind. A: the rat turned to face him, its Dexterity and Level raised, for a chance of 68 from
# the front. R: the rat made strong and Alaron clumsy, for its chance of 57 at him from the front, his shield and Warrior against it.
A = 'face:4,6|mod:4,Dexterity,40|base:4,Level,20'
R = 'face:4,6|mod:4,Strength,40|mod:4,Endurance,40|base:4,Level,10|mod:0,Dexterity,10'
CUTTING, SMASHING, PHYSICAL, MAGIC, HOLY = 14, 15, 3, 9, 16
# Weapons whose record carries a spell (+0x1C: the spell's place in the list at 0x800E2B40, and its level).
SPELL_WEAPONS = {'ChaosFlameblade': 0x731, 'ElishevasScythe': 0x737, 'Warfang': 0x738, 'IceStiletto': 0x73C, 'JestersMace': 0x73F,
                 'ChaosDeathwing': 0x740, 'SheridansSword': 0x752, 'ChaosScythe': 0x753, 'MaceOfGlory': 0x761, 'BreklorsFirestaff': 0x764,
                 'Lightreaver': 0x767, 'HockeyStick': 0x768}
# Plain weapons by their element: a Longsword (Cutting), a War Hammer (Smashing), a Battle Axe (Physical), Firedrake Fang (Fire, with a
# resistance of its own) and Alaron's own Lodin's Sword (Holy, with one too).
ELEMENT_WEAPONS = {'Longsword': 0x729, 'WarHammer': 0x717, 'BattleAxe': 0x70F, 'FiredrakeFang': 0x757}
HIT = {0: d100(30)}


def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None, steer=None):
        v = dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=list(args), seed=seed,
                 script=dict(script or {}))
        if steer:
            # Fix the first draw a function makes once the rest are known: (the function, the raw value).
            _, callers = run(v)
            v['script'][callers.index(steer[0])] = steer[1]
        out.append(v)

    # The rolls on the plain state, both ways: always a hit from behind (173), and the least chance (5) at Alaron.
    for r in (1, 6, 7, 50, 100):
        add(f'alaron-rat4-roll{r}', (0, 4), script={0: d100(r)})
    for r in (1, 20, 40, 60, 100):
        add(f'rat4-alaron-roll{r}', (4, 0), script={0: d100(r)})

    # Warrior's adjusted roll: at or under min(Warrior, a fifth of the chance), a second d100 off it. Chance 68: min(6, 13.6).
    for r in (1, 6, 7, 67, 68):
        add(f'A-roll{r}', (0, 4), A, script={0: d100(r)})
    add('A-warrior2-roll3', (0, 4), A + '|skill:0,Warrior,2', script={0: d100(3)})
    add('A-warrior15-roll13', (0, 4), A + '|skill:0,Warrior,15', script={0: d100(13)})
    add('A-warrior15-roll14', (0, 4), A + '|skill:0,Warrior,15', script={0: d100(14)})

    # The Strength tiers (4, 9, 17, 24, 28, 32, 36, 39): a d6 more past each.
    for s in (0, 4, 5, 9, 10, 17, 18, 24, 25, 28, 29, 32, 33, 36, 37, 39, 40, 60):
        add(f'A-strength{s}', (0, 4), A + f'|mod:0,Strength,{s}', script=HIT)

    # From behind: Brenna (2, Thief 12) put behind rat 4, with Thief and without; and the rat turned to face her.
    B = 'place:2,65,56|face:2,2'
    add('brenna-behind-thief12', (2, 4), B, script=HIT)
    add('brenna-behind-thief0', (2, 4), B + '|skill:2,Thief,0', script=HIT)
    add('brenna-front-thief12', (2, 4), B + '|face:4,6', script=HIT)
    add('brenna-behind-low', (2, 4), B + '|mod:4,Dexterity,40|base:4,Level,40|mod:2,Strength,1', script=HIT)
    # The edges of from behind: 45° off the target's facing (the dot 0.7071068 rounds to 0.707107), 90° off, and aimed past it.
    add('alaron-diagonal-45', (0, 4), 'place:0,65,57|face:0,1', script=HIT)
    add('alaron-diagonal-90', (0, 4), 'place:0,65,57|face:0,1|face:4,3', script=HIT)
    add('alaron-aimed-off', (0, 4), 'place:0,64,57|face:0,1', script=HIT)

    # The height: the attacker's cell against the target's, 5% of the chance a half unit.
    for h in (-6, -2, 2, 6):
        add(f'A-height{h:+d}', (0, 4), A + f'|height:60,56,{20 + h}|height:66,56,20', script=HIT)
        add(f'R-height{h:+d}', (4, 0), R + f'|height:66,56,{20 + h}|height:60,56,20', script=HIT)

    # Each part of the day, for a Solar attacker (Alaron) and a Lunar one (Alaron made Lunar, and the rat), and a Solar rat at night.
    for part in range(5):
        add(f'A-daypart{part}-solar', (0, 4), A + f'|daypart:{part}', script=HIT)
        add(f'A-daypart{part}-lunar', (0, 4), A + f'|daypart:{part}|aspect:0,1', script=HIT)
        add(f'R-daypart{part}-lunar', (4, 0), R + f'|daypart:{part}', script=HIT)
    add('R-daypart4-solar', (4, 0), R + '|daypart:4|aspect:4,2', script=HIT)

    # No Stamina, the target's and the attacker's own.
    add('A-target-stamina0', (0, 4), A + '|mod:4,Stamina,0', script=HIT)
    add('A-own-stamina0', (0, 4), A + '|mod:0,Stamina,0', script=HIT)
    add('A-both-stamina0-night', (0, 4), A + '|mod:0,Stamina,0|mod:4,Stamina,0|daypart:4', script=HIT)

    # A held target (IsImmobilised: an effect of kind 34, 53 or 57): no Dexterity or shield against the blow.
    for kind in (34, 53, 57):
        add(f'A-held{kind}', (0, 4), A + f'|effect:4,{kind},3,3,5', script=HIT)
    add('R-alaron-held34', (4, 0), R + '|effect:0,34,3,3,5', script=HIT)

    # A shield, from the front and from behind: its defence in the chance, its protection in the damage unless from behind.
    add('R-shield-front', (4, 0), R, script=HIT)
    add('R-shield-behind', (4, 0), R + '|place:4,59,56|face:4,2', script=HIT)
    add('R-noshield-front', (4, 0), R + '|noshield:0', script=HIT)
    add('R-noshield-behind', (4, 0), R + '|noshield:0|place:4,59,56|face:4,2', script=HIT)
    add('R-shield0-warrior0', (4, 0), R + '|skill:0,Shield,0|skill:0,Warrior,0', script=HIT)

    # The Troubadour's bonus, for the party's side and against the enemies'.
    for b in (1, 5, 21):
        add(f'A-troubadour{b}', (0, 4), A + f'|troubadour:{b}', script=HIT)
        add(f'R-troubadour{b}', (4, 0), R + f'|troubadour:{b}', script=HIT)

    # The resistances: the weapon's element against the rat's entries. Its AI plays (flags 8), so a weakness makes its morale check due
    # (flags 0x12).
    entries = (('weak1.5', [(None, '1.5')]), ('resist0.5', [(None, '0.5')]), ('immune', [(None, '0')]), ('physical2', [(PHYSICAL, '2')]),
               ('physical0.25', [(PHYSICAL, '0.25')]), ('physical0', [(PHYSICAL, '0')]), ('magic0', [(MAGIC, '0')]), ('magic1.5', [(MAGIC, '1.5')]),
               ('two', [(PHYSICAL, '0.5'), (None, '3')]), ('tiny', [(None, '0.01')]))
    for name, weapon in list(ELEMENT_WEAPONS.items()) + [('Lodins', None)]:
        element = {'Longsword': CUTTING, 'WarHammer': SMASHING, 'BattleAxe': PHYSICAL, 'FiredrakeFang': 5, 'Lodins': HOLY}[name]
        hand = f'|weapon:0,0x{weapon:X}' if weapon else ''
        for tag, es in entries:
            pk = A + hand + ''.join(f'|resist:4,{i},{element if e is None else e},{m}' for i, (e, m) in enumerate(es))
            add(f'A-{name}-{tag}', (0, 4), pk, script=HIT)
    add('A-Longsword-weak-weakling', (0, 4), A + f'|weapon:0,0x729|resist:4,0,{CUTTING},1.5|mod:0,Strength,1|base:0,Level,0', script=HIT)
    add('R-weak-alaron', (4, 0), R + f'|resist:0,0,{PHYSICAL},1.5', script=HIT)

    # A weapon's spell, put in Alaron's hand: cast at the target as an item's on a hit, its margin his. W: the rat turned to him with
    # no Dexterity, so the blow lands from the front.
    W = 'face:4,6|mod:4,Dexterity,0'
    for name, weapon in SPELL_WEAPONS.items():
        add(f'W-{name}', (0, 4), W + f'|weapon:0,0x{weapon:X}', script={0: d100(10)})
        add(f'W-{name}-miss', (0, 4), W + f'|weapon:0,0x{weapon:X}|mod:4,Dexterity,50|base:4,Level,40', script={0: d100(100)})
    add('W-Lightreaver-acted', (0, 4), W + '|weapon:0,0x767|acted:0,7', script={0: d100(10)})

    # The parity logs' states, each at its first turn's start (Combat.StartFirstTurn's dump): real entities' resistances, weapons and
    # spells, and the night. Each pair at a roll of 10 and of 60.
    logs = {
        # Setup 8, Ugarit's Skeletons (Cutting x0.25, Smashing x1.5): Dougal's Longsword, then a War Hammer; their Maul and Battle Axe.
        'setup8': ('20261031-230020-setup8', [((2, 4), ''), ((2, 4), '|weapon:2,0x717'), ((0, 5), ''), ((5, 0), ''), ((6, 3), '')]),
        # Setup 6, Wraiths (Physical x0, Magic x0.5): Sholeh's Cutting, Dougal's Enchanted Blade (Magic), a Wraith's Necromancy.
        'setup6': ('20261031-205206-setup6', [((1, 4), ''), ((2, 4), ''), ((4, 0), '')]),
        # Setup 4, Bandit Woodsmen at night, their entries of element 0.
        'setup4': ('20261031-190448-setup4', [((0, 4), ''), ((4, 3), ''), ((1, 5), '')]),
        # Setup 11, Kitarak's Warfang and its spell, a Hobgoblin's Great Axe; Naming x1.25, which no blow meets.
        'setup11': ('20261101-083418-setup11', [((4, 0), ''), ((4, 1), ''), ((0, 4), ''), ((5, 1), '')]),
        # Setup 12b, Lugash (Physical and Magic x0) against the Lightreavers (Holy, with their spell) and Sholeh's Cutting.
        'setup12b': ('20261101-100932-setup12b', [((2, 4), ''), ((3, 4), ''), ((1, 4), ''), ((4, 0), '')]),
        # Setup 7, the Air Elemental (Physical and Air x0) and its Air blow.
        'setup7': ('20261031-223128-setup7', [((0, 5), ''), ((5, 0), '')]),
        # Setup 1, Tomb Rats (Fire x1.5): Alaron's Holy, and Firedrake Fang's Fire in Sholeh's hand.
        'setup1': ('20261031-105935-setup1', [((0, 4), ''), ((1, 4), '|weapon:1,0x757'), ((4, 3), '')]),
    }
    for tag, (folder, pairs) in logs.items():
        for (a, t), pk in pairs:
            for r in (10, 60):
                add(f'{tag}-{a}-{t}{pk.replace("|weapon:", "-w").replace(",", "-")}-roll{r}', (a, t), pk.lstrip('|'),
                    source=f'log:{folder}/battle-01:0002.rdram.gz', script={0: d100(r)})

    # The weapon skill's roll: at or under a tenth of (8 - skill)^2 (5 from 6) the base trains, at half that on a miss.
    for skill, hit in ((4, True), (0, True), (0, False), (5, True), (6, True), (9, True)):
        xp = ((8 - skill) ** 2 if skill < 6 else 5) >> (0 if hit else 1)
        threshold = int(xp * 0.1)
        for r in sorted({max(threshold, 1), threshold + 1}):
            add(f'A-sword{skill}-{"hit" if hit else "miss"}-xp{r}', (0, 4), A + f'|skill:0,Sword,{skill}',
                script={0: d100(30 if hit else 90)}, steer=('Combatant.GainSkillExperience', d100(r)))
    return out
