"""Effects and potions: Character.AddEffect (0x800702F0), Character.AgeEffects (0x80071444, with Entity.AgePotions 0x8006FA28,
EffectRules.Round 0x80070A3C and EffectRules.End 0x800707EC) a round at a time, Battle.Dispel (0x80071CD8), Entity.CurePoison
(0x80071A6C), Character.TakeDamage (0x8006EB8C), Combatant.DrinkPotion (0x80064C84), Potions.CanDrink (0x8006FBE8),
Combatant.ResolveHealer (0x8006591C) and Combatant.StartTroubadour (0x800662F4), on the Combat checkpoints and the parity logs' states.
Args are the operation (0 add, 1 age, 2 dispel, 3 cure, 4 damage, 5 drink, 6 can drink, 7 heal, 8 perform) and its operands. Each
vector gives what the function returned and, for each slot it touches, the stats (current, base and modified), the protection byte, the
skills' modified levels, the spells' records slot by slot (kind, power, school, margin, harmful, timer), the potions' (kind, power,
timer), the combatant's flags, morale and Mirror's counter, with the Troubadour's bonus, the target's healing and the main stream's
draws."""
import state
import sim
from draws import d100

FAMILY = 'Effects'

ADD_EFFECT = 0x800702F0
AGE = 0x80071444
DISPEL = 0x80071CD8
CURE_POISON = 0x80071A6C
TAKE_DAMAGE = 0x8006EB8C
DRINK = 0x80064C84
CAN_DRINK = 0x8006FBE8
RESOLVE_HEALER = 0x8006591C
START_TROUBADOUR = 0x800662F4
INDICATOR_ADD = 0x8008A128
POTION_VISUAL = 0x80064B10

ADD, AGE_OP, DISPEL_OP, CURE, DAMAGE, DRINK_OP, CAN_DRINK_OP, HEAL, PERFORM = range(9)

SEED = 0x1234
POKE_SEED = 0x5678                # the stream as a family poke lays an effect on, which may draw (Wraith Touch)
TEXT = 0x80700000                 # a scratch buffer for CanDrink's refusal text, past the heap


# ---- the family's own pokes
def _fx(st, a):
    """fx:slot,kind,power,timer,margin: Character.AddEffect as a cast lays it on, the stream at POKE_SEED."""
    p = st.record(int(a[0], 0))
    st.cpu.wr32(sim.RNG, POKE_SEED)
    st.call(ADD_EFFECT, [st.cpu.rd32(p + 0x40), int(a[1], 0), int(a[2], 0), int(a[3], 0) & 0xFFFFFFFF, int(a[4], 0), p])


def _potion(st, a):
    """potion:slot,kind,power,timer: Character.ApplyPotionEffect (0x8006F84C), a potion record as drinking one lays it on."""
    st.call(0x8006F84C, [st.entity(int(a[0], 0)), int(a[1], 0), int(a[2], 0), int(a[3], 0) & 0xFFFFFFFF])


def _flags(st, a):
    """flags:slot,set,clear: the combatant's flags (+0x2C) with bits set and cleared."""
    p = st.record(int(a[0], 0))
    st.cpu.wr16(p + 0x2C, (st.cpu.rd16(p + 0x2C) | int(a[1], 0)) & ~int(a[2], 0) & 0xFFFF)


def _morale(st, a):
    """morale:slot,n: the AI record's morale (+6)."""
    ai = st.cpu.rd32(st.record(int(a[0], 0)) + 0x44)
    st.cpu.wr8(ai + 6, int(a[1], 0))


def _clock(st, a):
    """clock:n: the clock's byte at World+1, which Photosynthesis doesn't read (it reads the battle's part of the day, World+2)."""
    st.cpu.wr8(st.cpu.rd32(state.WORLD) + 1, int(a[0], 0))


def _herbs(st, a):
    """herbs:n: the party's Herbs (0x0112) made n."""
    inv = party_inventory(st)
    held = herbs(st)
    if held:
        st.call(0x80073688, [inv, HERB, held])          # Inventory.Remove
    if int(a[0], 0):
        st.call(0x80073574, [inv, HERB, int(a[0], 0)])  # Inventory.Add


EXTRA = {'fx': _fx, 'potion': _potion, 'flags': _flags, 'morale': _morale, 'clock': _clock, 'herbs': _herbs}

HERB = 0x0112


def party_inventory(st):
    return st.cpu.rd32(st.cpu.rd32(0x800D9684) + 0x10)       # GameState.Party's inventory


def count(st, item):
    """How many of an item the party's inventory holds: its 252 stacks from +4, 0x18 bytes each, the id at +0 and the count at +0x14."""
    inv = party_inventory(st)
    for i in range(252):
        stack = inv + 4 + 0x18 * i
        if st.cpu.rd16(stack) == item and st.cpu.rd32(stack + 0x14) > 0:
            return st.cpu.rd32(stack + 0x14)
    return 0


def herbs(st):
    return count(st, HERB)


# ---- what a slot holds
def snapshot(st, slot):
    cpu = st.cpu
    p = st.record(slot)
    e = cpu.rd32(p + 0x40)
    stats = st.stats(slot)
    out = [(f'stats{slot}', '/'.join('.'.join(str(cpu.rd8(stats + o + i)) for o in (7, 0xE, 0x15)) for i in range(7))),
           (f'prot{slot}', cpu.rd8(cpu.rd32(e + 0x14) + 2)),
           (f'skills{slot}', '.'.join(str(st.skill(slot, s)) for s in state.SKILLS))]
    fx = []
    for i in range(15):
        r = cpu.rd32(cpu.rd32(e + 0x34) + 4 * i)
        if r:
            fx.append(f'{i}:{cpu.rd8(r)}.{cpu.rd8(r + 1)}.{cpu.rd8(r + 2)}.{cpu.rd8(r + 3)}.{1 if cpu.rd32(r + 4) else 0}.{cpu.rd32(r + 8)}')
    out.append((f'fx{slot}', ';'.join(fx) or '-'))
    pots = []
    for i in range(7):
        r = cpu.rd32(cpu.rd32(e + 0x38) + 4 * i)
        if r:
            pots.append(f'{i}:{cpu.rd8(r)}.{cpu.rd8(r + 1)}.{cpu.rd32(r + 4)}')
    out.append((f'potions{slot}', ';'.join(pots) or '-'))
    ai = cpu.rd32(p + 0x44)
    morale = cpu.rd8(ai + 6) if ai else None
    out += [(f'flags{slot}', cpu.rd16(p + 0x2C)), (f'morale{slot}', '-' if morale is None else sim.s8(morale)),
            (f'mirror{slot}', cpu.rd8(p + 0x28))]
    return out


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=EXTRA, seed=v['seed'], script=v.get('script'))
    cpu = st.cpu
    # The numbers over a body (CombatStatIndicator.Add) are shown, not checked; its pool overflows on a margin over 127's healing.
    cpu.hooks[INDICATOR_ADD] = lambda c: 0
    # Nor is a potion's visual (Combatant.AttachSpellVisual, through Body.StartPotionVisual), which runs past what the interpreter has.
    cpu.hooks[POTION_VISUAL] = lambda c: 0
    op, *a = v['args']
    slots = [a[0]]
    extra = []
    if op == ADD:
        slot, kind, power, timer, margin = a
        p = st.record(slot)
        ret = st.call(ADD_EFFECT, [st.entity(slot), kind, power, timer & 0xFFFFFFFF, margin, p]) & 0xFFFF
    elif op == AGE_OP:
        slot, rounds = a
        p = st.record(slot)
        for _ in range(rounds):
            st.call(AGE, [st.entity(slot), p, 1])
        ret = 0
    elif op == DISPEL_OP:
        slot, kind, margin = a
        p = st.record(slot)
        ret = st.call(DISPEL, [st.entity(slot), p, kind, margin]) & 0xFF
    elif op == CURE:
        slot, margin = a
        p = st.record(slot)
        st.call(CURE_POISON, [st.entity(slot), p, margin])
        ret = 0
    elif op == DAMAGE:
        slot, amount = a
        p = st.record(slot)
        st.call(TAKE_DAMAGE, [st.entity(slot), amount & 0xFFFF, p])
        ret = 0
    elif op == DRINK_OP:
        slot, kind = a
        p = st.record(slot)
        cpu.wr8(p + 0x27, kind)
        st.call(DRINK, [p, p])                     # the drinker, and the target set A holds: the drinker
        ret = 0
    elif op == CAN_DRINK_OP:
        slot, kind = a
        ret = st.call(CAN_DRINK, [st.entity(slot), kind, TEXT]) & 0xFF
    elif op == HEAL:
        slot, target, mode = a
        p, t = st.record(slot), st.record(target)
        cpu.wr16(p + 0x2C, cpu.rd16(p + 0x2C) & ~0x300 | (0x100 if mode == 8 else 0x200))
        ret = st.call(RESOLVE_HEALER, [p, t, st.body(slot)]) & 0xFFFF     # the healer's body plays request 5
        if target != slot:
            slots.append(target)
        extra = [('herbs', herbs(st))]
    elif op == PERFORM:
        slot, = a
        st.call(START_TROUBADOUR, [st.record(slot)])
        ret = 0
    else:
        raise ValueError(op)
    expect = [('ret', ret)]
    for s in slots:
        expect += snapshot(st, s)
    expect += [('bonus', cpu.rd8(st.combat + 0x13)), ('healed', cpu.rd8(st.record(slots[-1]) + 0x2A)), ('draws', len(st.draws.drawn))]
    return expect + extra, st.draws.callers()


# ---- the vectors

# The lasting kinds, by their place in the spell list (0x800E2B40). Wind (11) is left out: AddEffect only records it, its Stamina on an
# ally being ApplySpellEffect's (Entity.AddStaminaEffect), which the port's record starts, so it's the casting family's.
LASTING = (0, 3, 4, 5, 10, 13, 14, 16, 17, 19, 22, 23, 24, 25, 26, 27, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 46, 47, 48, 51, 52,
           53, 54, 55, 56, 57)
IMMOLATION, AIR_SHIELD, STRENGTH, CHARMING, WEAKNESS, CHEAT_DEATH, ACID_BOLT, AURA_OF_DEATH, WRAITH_TOUCH = 0, 3, 10, 16, 22, 23, 24, 25, 26
EXHAUSTION, TAP_STAMINA, SPIRIT_SHIELD, POISON, PHOTOSYNTHESIS, SOLAR_WRATH, STARLIGHT_SHIELD, WEB = 31, 33, 35, 36, 46, 47, 48, 57
DEBILITATION, MIRROR = 5, 37
DISPEL_ELEMENTAL, DISPEL_NAMING, DISPEL_NECRO, DISPEL_STAR = 42, 43, 44, 45
# The potions' kinds (items 0x1000 + kind): 4 Healing, 5 Stamina, 6 Curing, 7 Antidote, 8 Restore, 9 Strength, 10 Dexterity, 13 Clarity,
# 14 Charisma, 15 Defence, 16 Stealth; 0-3 the flasks, 11 and 12 the Aspect and Jumping potions, which do nothing.
DRINKABLE = (4, 5, 6, 7, 8, 9, 10, 13, 14, 15, 16)
# Drunk in a battle: Stealth's (16) is left out of C-up's menu (CombatMenu.ListPotions skips it), and the port's record keeps it to trek.
IN_BATTLE = DRINKABLE[:-1]
HURT0 = 'cur:0,Stamina,40|mod:0,Stamina,40'        # Alaron 40 of his 79 Stamina
HURT1 = 'cur:1,Stamina,30|mod:1,Stamina,30'        # Rheda 30 of her 68
NEVER = -1                        # a timer of 0xFFFFFFFF, which never runs down, as the args' int
DEAD0 = 'cur:0,Stamina,0|cur:0,Level,0'


def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None):
        out.append(dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=list(args), seed=seed,
                        script=dict(script or {})))

    # ---- Character.AddEffect: each lasting kind on Alaron (0) and on rat 4, at level 3 and margin 4, for 3 rounds.
    for kind in LASTING:
        for slot in (0, 4):
            add(f'add-{kind}-on{slot}', (ADD, slot, kind, 3, 3 * 1800, 4))
    add('add-strength-power10', (ADD, 0, STRENGTH, 10, 18000, 4))
    add('add-weakness-floor', (ADD, 4, WEAKNESS, 10, 18000, 4))
    add('add-debilitation-floor', (ADD, 4, DEBILITATION, 10, 18000, 4))
    add('add-weakness-level-floor', (ADD, 4, WEAKNESS, 10, 18000, 4), 'mod:4,Endurance,0|cur:4,Endurance,0')
    add('add-again', (ADD, 0, STRENGTH, 3, 5400, 4), 'fx:0,10,2,3600,1')
    add('add-full', (ADD, 0, STRENGTH, 3, 5400, 4), '|'.join(f'fx:0,{k},1,5400,1' for k in (3, 13, 19, 30, 56, 35, 37, 38, 39, 40, 41, 46, 48, 51, 55)))
    add('add-dead', (ADD, 0, STRENGTH, 3, 5400, 4), DEAD0)
    add('add-aura-morale1', (ADD, 4, AURA_OF_DEATH, 3, 5400, 4), 'morale:4,1')
    add('add-aura-morale0', (ADD, 4, AURA_OF_DEATH, 3, 5400, 4), 'morale:4,0')
    add('add-solarwrath-power9', (ADD, 4, SOLAR_WRATH, 9, 5400, 4))
    add('add-tap-stamina-short', (ADD, 4, TAP_STAMINA, 6, 5400, 4), 'cur:4,Stamina,5|mod:4,Stamina,5')
    add('add-tap-stamina-ally', (ADD, 0, TAP_STAMINA, 6, 5400, 4), HURT0)
    add('add-mirror-again', (ADD, 0, MIRROR, 5, 5400, 4), 'fx:3,37,2,5400,1')
    add('add-margin200', (ADD, 0, CHEAT_DEATH, 3, 5400, 200))
    add('add-never', (ADD, 0, STRENGTH, 3, NEVER, 4))
    for seed in (1, 2, 3, 0x1234, 0xBEEF, 0xDEAD, 0xC0FFEE, 0x7777):
        add(f'add-wraith-touch-seed{seed:X}', (ADD, 0, WRAITH_TOUCH, 5, NEVER, 3), seed=seed)
        add(f'add-wraith-touch-rat-seed{seed:X}', (ADD, 4, WRAITH_TOUCH, 5, NEVER, 3), seed=seed)
    # The charms' conditions: Control Elem an elemental (setup 7's Dust Devil, 4, and Air Elemental, 5), Control Marquis and Control
    # Zombies their entities; Charming none.
    s7 = 'log:20261031-223128-setup7/battle-01:0002.rdram.gz'
    for slot in (4, 5):
        for kind in (4, 16, 17, 27):
            add(f'setup7-add-{kind}-on{slot}', (ADD, slot, kind, 3, 5400, 4), source=s7)
    add('add-wraith-touch-low', (ADD, 4, WRAITH_TOUCH, 5, NEVER, 3), 'mod:4,Intelligence,1|mod:4,Willpower,1|mod:4,Dexterity,1|mod:4,Strength,1')

    # ---- A round's ageing, for 1 to 4 rounds.
    for rounds in (1, 3, 4):
        add(f'age-poison4-rat-r{rounds}', (AGE_OP, 4, rounds), 'fx:4,36,4,1800,2')
        add(f'age-poison4-alaron-r{rounds}', (AGE_OP, 0, rounds), 'fx:0,36,4,1800,2')
    add('age-poison1', (AGE_OP, 4, 2), 'fx:4,36,1,1800,2')
    add('age-poison9-endurance3', (AGE_OP, 4, 2), 'fx:4,36,9,5400,2|cur:4,Endurance,3|mod:4,Endurance,3')
    add('age-poison9-low-hp', (AGE_OP, 4, 2), 'fx:4,36,9,5400,2|cur:4,Stamina,1')
    add('age-poison-dead', (AGE_OP, 4, 2), 'fx:4,36,4,5400,2|cur:4,Stamina,0|cur:4,Level,0')
    for kind, tag in ((IMMOLATION, 'immolation'), (ACID_BOLT, 'acidbolt'), (WEB, 'web')):
        for seed in (1, 0x1234, 0xBEEF, 0xC0FFEE):
            add(f'age-{tag}-seed{seed:X}', (AGE_OP, 4, 3), f'fx:4,{kind},2,3600,3', seed=seed)
        add(f'age-{tag}-low-hp', (AGE_OP, 4, 2), f'fx:4,{kind},2,3600,3|cur:4,Stamina,2|cur:4,Level,0', seed=0xBEEF)
        add(f'age-{tag}-alaron', (AGE_OP, 0, 2), f'fx:0,{kind},2,3600,3', seed=0xBEEF)
    # Web's escape: a d100 under twice Strength (8, so 16), its step at least 1.
    for r in (1, 14, 15, 16, 17, 100):
        add(f'age-web-escape-roll{r}', (AGE_OP, 4, 1), 'fx:4,57,2,3600,3', script={0: d100(r)})
    add('age-web-escape-strength0', (AGE_OP, 4, 1), 'fx:4,57,2,3600,3|mod:4,Strength,0', script={0: d100(1)})
    add('age-web-escape-strength50', (AGE_OP, 4, 1), 'fx:4,57,2,3600,3|mod:4,Strength,50', script={0: d100(60)})
    # Photosynthesis heals its margin each round, not at night by the clock, whatever the battle's part of the day holds.
    for part in range(5):
        add(f'age-photosynthesis-clock{part}', (AGE_OP, 0, 2), f'{HURT0}|fx:0,46,3,5400,7|clock:{part}')
    add('age-photosynthesis-daypart4-clock1', (AGE_OP, 0, 1), f'{HURT0}|fx:0,46,3,5400,7|clock:1|daypart:4')
    add('age-photosynthesis-daypart1-clock4', (AGE_OP, 0, 1), f'{HURT0}|fx:0,46,3,5400,7|clock:4|daypart:1')
    # Darkness holds the battle's part of the day at night (World+2), so Photosynthesis stops; Light holds it at day.
    for part in (1, 4):
        add(f'age-photosynthesis-darkness-daypart{part}', (AGE_OP, 0, 1), f'daypart:{part}|{HURT0}|fx:0,46,3,5400,7|fx:4,29,3,5400,5')
        add(f'age-photosynthesis-light-daypart{part}', (AGE_OP, 0, 1), f'daypart:{part}|{HURT0}|fx:0,46,3,5400,7|fx:4,54,3,5400,5')
    add('age-photosynthesis-full', (AGE_OP, 0, 1), 'fx:0,46,3,5400,7|clock:1')
    add('age-photosynthesis-margin200', (AGE_OP, 0, 1), f'{HURT0}|fx:0,46,3,5400,200|clock:1')
    # Records ending: the timer at 0 ends on the next ageing; what each gives back.
    for kind in (STRENGTH, WEAKNESS, 32, EXHAUSTION, AIR_SHIELD, SPIRIT_SHIELD, STARLIGHT_SHIELD, TAP_STAMINA, MIRROR, 30, 56, 34, 53, CHARMING, 4, 46, 23):
        add(f'age-end-{kind}', (AGE_OP, 4, 2), f'fx:4,{kind},3,600,4')
        add(f'age-end-{kind}-alaron', (AGE_OP, 0, 2), f'fx:0,{kind},3,600,4')
    for kind in (AURA_OF_DEATH, SOLAR_WRATH):
        add(f'age-morale-back-{kind}', (AGE_OP, 4, 2), f'fx:4,{kind},3,600,4')
        add(f'age-morale-back-{kind}-low', (AGE_OP, 4, 2), f'morale:4,2|fx:4,{kind},3,600,4')
        add(f'age-morale-back-{kind}-spent', (AGE_OP, 4, 2), f'fx:4,{kind},3,600,4|morale:4,0')
    add('age-wraith-touch-back', (AGE_OP, 0, 2), 'fx:0,26,5,600,3')
    add('age-wraith-touch-back-rat', (AGE_OP, 4, 2), 'fx:4,26,5,600,3')
    add('age-wraith-touch-never', (AGE_OP, 0, 3), 'fx:0,26,5,0xFFFFFFFF,3')
    add('age-timer0', (AGE_OP, 0, 1), 'fx:0,10,3,0,4')
    add('age-timer599', (AGE_OP, 0, 2), 'fx:0,10,3,599,4')
    add('age-timer601', (AGE_OP, 0, 2), 'fx:0,10,3,601,4')
    add('age-several', (AGE_OP, 4, 4), 'fx:4,36,4,1800,2|fx:4,0,2,1200,3|fx:4,22,3,600,4|fx:4,24,2,2400,3', seed=0xC0FFEE)
    add('age-dead', (AGE_OP, 4, 2), 'fx:4,36,4,1800,2|fx:4,22,3,600,4|cur:4,Stamina,0|cur:4,Level,0')
    for kind in (9, 10, 13, 15, 16):
        add(f'age-potion{kind}', (AGE_OP, 0, 2), f'potion:0,{kind},0,600')
    add('age-potion-and-spell', (AGE_OP, 0, 2), 'potion:0,9,0,600|fx:0,10,3,600,4')

    # ---- Battle.Dispel: every record of the school if the margin times their number reaches the sum of their margins.
    for margin in (3, 4, 5, 200):
        add(f'dispel-one-m{margin}', (DISPEL_OP, 0, DISPEL_ELEMENTAL, margin), 'fx:0,10,3,5400,4')
    for margin in (4, 5, 6, 0):
        add(f'dispel-two-m{margin}', (DISPEL_OP, 0, DISPEL_ELEMENTAL, margin), 'fx:0,10,3,5400,4|fx:0,3,3,5400,6')
    add('dispel-other-school', (DISPEL_OP, 0, DISPEL_NAMING, 50), 'fx:0,10,3,5400,4')
    add('dispel-none', (DISPEL_OP, 0, DISPEL_STAR, 50))
    add('dispel-necro-wraith-touch', (DISPEL_OP, 0, DISPEL_NECRO, 9), 'fx:0,26,5,0xFFFFFFFF,3')
    add('dispel-necro-aura', (DISPEL_OP, 4, DISPEL_NECRO, 9), 'fx:4,25,3,5400,4')
    add('dispel-star-web', (DISPEL_OP, 4, DISPEL_STAR, 9), 'fx:4,57,3,5400,4')
    add('dispel-naming-charming', (DISPEL_OP, 4, DISPEL_NAMING, 9), 'fx:4,16,3,5400,4')
    add('dispel-unschooled-poison', (DISPEL_OP, 4, DISPEL_NECRO, 99), 'fx:4,36,3,5400,4')
    add('dispel-mixed', (DISPEL_OP, 0, DISPEL_STAR, 5), 'fx:0,46,3,5400,4|fx:0,10,3,5400,9|fx:0,48,3,5400,6|fx:0,51,3,5400,2')
    add('dispel-potion', (DISPEL_OP, 0, DISPEL_ELEMENTAL, 99), 'potion:0,9,0,648000')

    # ---- Entity.CurePoison: Poison records cast with a lower margin.
    for margin in (0, 1, 2, 255):
        add(f'cure-m{margin}', (CURE, 4, margin), 'fx:4,36,3,5400,1')
    add('cure-none', (CURE, 4, 5))
    add('cure-high-margin', (CURE, 4, 200), 'fx:4,36,3,5400,150')

    # ---- Character.TakeDamage: the Level first; Cheat Death; death taking the effects.
    for amount in (5, 20, 21, 50, 97, 98, 99, 300):
        add(f'damage-{amount}', (DAMAGE, 0, amount))
        add(f'damage-{amount}-cheat-death', (DAMAGE, 0, amount), 'fx:0,23,5,5400,4')
    add('damage-cheat-death-m100', (DAMAGE, 0, 200), 'fx:0,23,5,5400,100')
    add('damage-cheat-death-m200', (DAMAGE, 0, 200), 'fx:0,23,5,5400,200')
    add('damage-death-takes-effects', (DAMAGE, 0, 200), 'fx:0,10,3,5400,4|fx:0,19,3,5400,4|fx:0,36,3,5400,4|potion:0,9,0,648000|potion:0,15,0,648000')
    add('damage-death-rat-held', (DAMAGE, 4, 30), 'fx:4,57,3,5400,4|fx:4,25,3,5400,4|fx:4,52,3,5400,4')
    add('damage-death-rat-charmed', (DAMAGE, 4, 30), 'fx:4,16,3,5400,4')
    add('damage-death-endurance', (DAMAGE, 4, 30), 'fx:4,19,3,5400,4|fx:4,10,3,5400,4')
    add('damage-performer', (DAMAGE, 0, 3), 'flags:0,4,0|troubadour:4')
    add('damage-performer-dies', (DAMAGE, 0, 300), 'flags:0,4,0|troubadour:4')
    add('damage-0', (DAMAGE, 0, 0))
    add('damage-negative', (DAMAGE, 0, -5), HURT0)

    # ---- Combatant.DrinkPotion: each drinkable, on Alaron hurt and whole, with what it cures on.
    for kind in IN_BATTLE:
        add(f'drink-{kind}', (DRINK_OP, 0, kind))
        add(f'drink-{kind}-hurt', (DRINK_OP, 0, kind), HURT0)
    for seed in (1, 0xBEEF, 0xC0FFEE):
        add(f'drink-healing-seed{seed:X}', (DRINK_OP, 1, 4), HURT1, seed=seed)
        add(f'drink-curing-seed{seed:X}', (DRINK_OP, 1, 6), HURT1, seed=seed)
    add('drink-healing-nearly-full', (DRINK_OP, 0, 4), 'cur:0,Stamina,76|mod:0,Stamina,76')
    add('drink-stamina-exhausted', (DRINK_OP, 0, 5), 'fx:0,31,3,5400,4')
    add('drink-stamina-exhausted-hurt', (DRINK_OP, 0, 5), f'fx:0,31,3,5400,4|{HURT0}')
    add('drink-antidote-poisoned', (DRINK_OP, 0, 7), 'fx:0,36,3,5400,200')
    add('drink-restore-all', (DRINK_OP, 0, 8),
        'fx:0,26,5,0xFFFFFFFF,3|fx:0,5,3,5400,4|fx:0,22,3,5400,4|fx:0,31,3,5400,4|fx:0,14,3,5400,4|fx:0,52,3,5400,4|fx:0,36,3,5400,4')
    add('drink-restore-wraith-touch', (DRINK_OP, 4, 8), 'fx:4,26,5,0xFFFFFFFF,3')
    for kind in (9, 10, 13, 14, 15):
        add(f'drink-{kind}-again', (DRINK_OP, 0, kind), f'potion:0,{kind},0,600')
    add('drink-strength-dead', (DRINK_OP, 0, 9), DEAD0)
    add('drink-potions-full', (DRINK_OP, 0, 10), '|'.join(f'potion:0,{k},0,648000' for k in (9, 13, 14, 15, 16, 3, 3)))

    # ---- Potions.CanDrink: every kind, whole and hurt, and with what it would take off.
    for kind in range(17):
        add(f'can-drink-{kind}', (CAN_DRINK_OP, 0, kind))
        add(f'can-drink-{kind}-hurt', (CAN_DRINK_OP, 0, kind), HURT0)
    add('can-drink-stamina-exhausted', (CAN_DRINK_OP, 0, 5), 'fx:0,31,3,5400,4')
    add('can-drink-stamina-over', (CAN_DRINK_OP, 0, 5), 'cur:0,Stamina,90|mod:0,Stamina,90')
    add('can-drink-antidote-poisoned', (CAN_DRINK_OP, 0, 7), 'fx:0,36,3,5400,4')
    for kind in (26, 5, 22, 31, 14, 52, 36):
        add(f'can-drink-restore-{kind}', (CAN_DRINK_OP, 0, 8), f'fx:0,{kind},3,5400,4')
    for kind in (9, 10, 13, 14, 15, 16):
        add(f'can-drink-{kind}-on', (CAN_DRINK_OP, 0, kind), f'potion:0,{kind},0,600')
    add('can-drink-healing-dead', (CAN_DRINK_OP, 0, 4), DEAD0)

    # ---- Combatant.ResolveHealer: Alaron as the Healer at Rheda, Lay On Hands (8) and Heal With Herbs (9).
    for level in (0, 1, 3, 5, 10):
        H = f'skill:0,Healer,{level}|herbs:3'
        add(f'heal-hands-healer{level}', (HEAL, 0, 1, 8), f'{H}|{HURT1}')
        for r in (1, 50, 99):
            add(f'heal-herbs-healer{level}-roll{r}', (HEAL, 0, 1, 9), f'{H}|{HURT1}', script={0: d100(r)})
    H = 'skill:0,Healer,5|herbs:3'
    add('heal-hands-full', (HEAL, 0, 1, 8), H)
    add('heal-herbs-full', (HEAL, 0, 1, 9), H)
    add('heal-hands-stamina2', (HEAL, 0, 1, 8), f'{H}|{HURT1}|cur:0,Stamina,2|mod:0,Stamina,2')
    add('heal-hands-stamina3', (HEAL, 0, 1, 8), f'{H}|{HURT1}|cur:0,Stamina,3|mod:0,Stamina,3')
    add('heal-herbs-stamina-short', (HEAL, 0, 1, 9), f'skill:0,Healer,1|herbs:3|{HURT1}|cur:0,Stamina,3|mod:0,Stamina,3')
    add('heal-herbs-no-herb', (HEAL, 0, 1, 9), f'skill:0,Healer,5|herbs:0|{HURT1}')
    add('heal-herbs-self', (HEAL, 0, 0, 9), f'{H}|{HURT0}', script={0: d100(10)})
    add('heal-hands-nearly-full', (HEAL, 0, 1, 8), f'{H}|cur:1,Stamina,66|mod:1,Stamina,66')
    add('heal-herbs-int0', (HEAL, 0, 1, 9), f'{H}|{HURT1}|mod:0,Intelligence,0', script={0: d100(60)})
    add('heal-herbs-roll-edge', (HEAL, 0, 1, 9), f'{H}|{HURT1}|mod:0,Intelligence,10', script={0: d100(95)})

    # ---- Combatant.StartTroubadour: (Troubadour x 5 + Intelligence) x 2 against a d100; Alaron's Troubadour 5, Intelligence 21: 92.
    for r in (1, 50, 91, 92, 93, 100):
        add(f'perform-roll{r}', (PERFORM, 0), script={0: d100(r)})
    for level in (0, 1, 6, 10):
        add(f'perform-troubadour{level}', (PERFORM, 0), f'skill:0,Troubadour,{level}', script={0: d100(30)})
    add('perform-stamina0', (PERFORM, 0), 'cur:0,Stamina,0|mod:0,Stamina,0')
    add('perform-stamina2', (PERFORM, 0), 'cur:0,Stamina,2|mod:0,Stamina,2', script={0: d100(30)})
    add('perform-already', (PERFORM, 0), 'flags:1,4,0|troubadour:3')
    add('perform-already-self', (PERFORM, 0), 'flags:0,4,0|troubadour:3')
    add('perform-bonus-no-performer', (PERFORM, 0), 'troubadour:3')
    # GainSmallExperience: a d100 at or under a tenth of (8 - skill)^2 (8 from 6), halved on a failure.
    for level, made in ((5, True), (5, False), (0, True), (0, False), (6, True), (6, False), (3, True)):
        points = (8 - level) ** 2 if level < 6 else 8
        threshold = (points if made else points >> 1) // 10
        for r in sorted({max(threshold, 1), threshold + 1}):
            add(f'perform-train{level}-{"made" if made else "failed"}-xp{r}', (PERFORM, 0), f'skill:0,Troubadour,{level}',
                script={0: d100(10 if made else 100), 1: d100(r)})

    # ---- Real states.
    s5b = 'log:20261031-201521-setup5b/battle-01:0047.rdram.gz'      # slots 2 and 3 poisoned (Poison 3, margin 1)
    for slot in (2, 3):
        for seed in (1, 0x1234):
            add(f'setup5b-age-poison{slot}-seed{seed:X}', (AGE_OP, slot, 3), source=s5b, seed=seed)
        add(f'setup5b-cure-poison{slot}', (CURE, slot, 2), source=s5b)
        add(f'setup5b-antidote{slot}', (DRINK_OP, slot, 7), source=s5b)
    s6 = 'log:20261031-205206-setup6/battle-01:0026.rdram.gz'        # Wraith Touch on slots 0 and 3, Darkness on 5
    for slot in (0, 3):
        add(f'setup6-restore{slot}', (DRINK_OP, slot, 8), source=s6)
        add(f'setup6-age{slot}', (AGE_OP, slot, 2), source=s6)
        add(f'setup6-dispel-necro{slot}', (DISPEL_OP, slot, DISPEL_NECRO, 2), source=s6)
        add(f'setup6-death{slot}', (DAMAGE, slot, 300), source=s6)
    add('setup6-age-darkness', (AGE_OP, 5, 2), source=s6)
    s10 = 'log:20261031-235130-setup10/battle-02:0014.rdram.gz'      # Cheat Death on Alaron (5, margin 4)
    for amount in (5, 60, 300):
        add(f'setup10-cheat-death-{amount}', (DAMAGE, 0, amount), source=s10)
    add('setup10-age', (AGE_OP, 0, 1), source=s10)
    s3 = 'log:20261031-172849-setup3/battle-01:0002.rdram.gz'        # the first turn, before the potions
    for slot in range(4):
        for kind in DRINKABLE:
            if kind in IN_BATTLE:
                add(f'setup3-drink{kind}-{slot}', (DRINK_OP, slot, kind), source=s3)
            add(f'setup3-can-drink{kind}-{slot}', (CAN_DRINK_OP, slot, kind), source=s3)
    for healer in range(4):
        for target in range(4):
            if target != healer:
                add(f'setup3-hands-{healer}-{target}', (HEAL, healer, target, 8), f'cur:{target},Stamina,5|mod:{target},Stamina,5|herbs:2', source=s3)
            add(f'setup3-herbs-{healer}-{target}', (HEAL, healer, target, 9), f'cur:{target},Stamina,5|mod:{target},Stamina,5|herbs:2', source=s3)
        add(f'setup3-perform-{healer}', (PERFORM, healer), source=s3)
    s3b = 'log:20261031-172849-setup3/battle-01:0047.rdram.gz'       # the potions on, and Acid's corrosion
    for slot in (0, 3, 4):
        add(f'setup3-age-potions{slot}', (AGE_OP, slot, 2), source=s3b)
        add(f'setup3-death{slot}', (DAMAGE, slot, 300), source=s3b)
    return out
