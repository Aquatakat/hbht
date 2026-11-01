"""Casting: Combatant.CastSpell (0x800648CC) at one target, as CastCurrentSpell (0x80065C08) calls it with the caster's selected spell
built by Entity.SpellCopy (0x80070D50): CheckSpellAvailable (0x800635B8), PrepareCast (0x8006406C) with AttemptCast's reagent, Stamina,
CastChance and roll (0x80063F7C) or an item's charge, ResistRoll (0x80064298), ApplySpellEffect (0x800646E0) and SpellDamage
(0x800649E4), on the Combat checkpoints and the parity logs' states with one input changed at a time. Args are the caster's and the
target's slots. Each vector gives: what CastSpell returns (the damage, or -1 resisted, -2 not cast, -3 an effect with no number), the
caster's cast margin after (+0x3C) and its acted flag, the main stream's draws, the caster's Stamina, the spell's level after (a known
one's, which may rise), the item's charges after, the party's count of the reagent after, the target's spell records by slot (kind,
power, margin, timer), its modified and base stats, its protection, its Mirror counter, its flags (active, hostile) and its AI record's
morale and flags."""
import state
from draws import d100

FAMILY = 'Casting'

CAST_SPELL = 0x800648CC
SPELL_COPY = 0x80070D50          # Entity.SpellCopy(entity, line, 0): the selected spell built for the cast
LEARN = 0x8007AD70               # Spellbook.Learn(book, id, level)
SENSE_AURA = 0x80070070
SPELL_LIST = 0x800E2B40

SEED = 0x1234


def spell_id(cpu, kind):
    return 0x300 + cpu.rd8(SPELL_LIST + kind)


def book_find(st, slot, kind):
    """The place of the spell of `kind` in the caster's spellbook, or -1."""
    cpu = st.cpu
    book = cpu.rd32(st.entity(slot) + 0x2C)
    arr, n = cpu.rd32(book), cpu.rd8(book + 4)
    want = spell_id(cpu, kind)
    for i in range(n):
        if cpu.rd16(cpu.rd32(arr + 4 * i)) == want:
            return i
    return -1


def _learn(st, a):
    """learn:slot,kind,level: the caster knows the spell of that kind (its place in SpellList) at that level, and has it selected."""
    slot, kind, level = (int(x, 0) for x in a)
    cpu = st.cpu
    ent = st.entity(slot)
    if book_find(st, slot, kind) < 0:
        cpu.run(LEARN, [cpu.rd32(ent + 0x2C), spell_id(cpu, kind), level], maxsteps=5_000_000)
    i = book_find(st, slot, kind)
    book = cpu.rd32(ent + 0x2C)
    cpu.wr8(cpu.rd32(cpu.rd32(book) + 4 * i) + 0x14, level)
    cpu.wr8(ent + 0x3D, 1)
    cpu.wr8(ent + 0x3E, i)


def _mirror(st, a):
    """mirror:slot,n: Mirror's counter (+0x28)."""
    st.cpu.wr8(st.record(int(a[0], 0)) + 0x28, int(a[1], 0))


EXTRA = {'learn': _learn, 'mirror': _mirror}


def s16(v):
    v &= 0xFFFF
    return v - 0x10000 if v & 0x8000 else v


def s8(v):
    v &= 0xFF
    return v - 0x100 if v & 0x80 else v


def target_state(st, t):
    cpu = st.cpu
    p, ent = st.record(t), st.entity(t)
    records = []
    table = cpu.rd32(ent + 0x34)
    for i in range(15):
        r = cpu.rd32(table + 4 * i)
        if r:
            records.append(f'{cpu.rd8(r)}:{cpu.rd8(r + 1)}:{cpu.rd8(r + 3)}:{cpu.rd32(r + 8)}')
    stats = st.stats(t)
    ai = cpu.rd32(p + 0x44)
    return [('effects', '/'.join(records) or '-'),
            ('tmod', '.'.join(str(cpu.rd8(stats + 0x15 + k)) for k in range(7))),
            ('tbase', '.'.join(str(cpu.rd8(stats + 0xE + k)) for k in range(7))),
            ('prot', cpu.rd8(cpu.rd32(ent + 0x14) + 2)),
            ('mirror', cpu.rd8(p + 0x28)),
            ('tflags', cpu.rd16(p + 0x2C) & 0x12),
            ('morale', cpu.rd8(ai + 6) if ai else -1), ('aiflags', cpu.rd8(ai + 9) if ai else -1)]


def run(v):
    st = state.State(v['source'], v.get('pokes', ''), extra=EXTRA, seed=v['seed'], script=v.get('script'))
    st.cpu.hooks[SENSE_AURA] = lambda cpu: 0
    a, t = v['args']
    cpu = st.cpu
    ent = st.entity(a)
    source, index = cpu.rd8(ent + 0x3D), cpu.rd8(ent + 0x3E)
    spell = st.call(SPELL_COPY, [ent, 0x13CB, 0]) & 0xFFFFFFFF
    result = s16(st.call(CAST_SPELL, [st.record(a), st.record(t), spell, 0]))
    level = -1
    if source == 1:
        book = cpu.rd32(ent + 0x2C)
        level = cpu.rd8(cpu.rd32(cpu.rd32(book) + 4 * index) + 0x14)
    expect = [('result', result), ('margin', s8(cpu.rd8(ent + 0x3C))), ('acted', cpu.rd16(st.record(a) + 0x2C) & 1),
              ('draws', len(st.draws.drawn)), ('stamina', st.stat(a, 'Stamina')), ('level', level)]
    expect += target_state(st, t)
    return expect, st.draws.callers()


# ---- the vectors

# Checkpoint 05: Alaron (0, Solar, Wizard 10), Rheda (1), Brenna (2), Sholeh (3, Lunar); rats 4-7. The day's first part.
CASTS = {0: d100(30)}
ESCAPE, TELEPORTATION = 1, 12
# The kinds whose target type (the spell's +0x19) is the party's.
FOR_PARTY = {1, 2, 3, 10, 12, 13, 19, 23, 30, 32, 35, 37, 38, 39, 40, 41, 46, 48, 51}
NOT_RESISTED = ('Combatant.ResistRoll', d100(100))
CHARMS = (4, 17, 27)
EARTH, SOLAR, PHYSICAL, NECROMANCY, FIRE, LUNAR, NAMING, WATER, MAGIC, AIR, STAR, ELEMENTAL = 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12


def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None, steer=()):
        v = dict(id=f'{source}-{tag}' if source.startswith('cp') else tag, source=source, pokes=pokes, args=list(args), seed=seed,
                 script=dict(script or {}))
        for function, value in steer:
            # Fix the first draw a function makes once the rest are known.
            _, callers = run(v)
            if function in callers:
                v['script'][callers.index(function)] = value
        out.append(v)

    # Every spell kind, Alaron casting it at level 5: one for the party at Rheda, Teleportation at himself, the rest at rat 4, landed
    # and not resisted. Not Escape (1), whose Combatant.Leave waits on the body's fading, nor the two Detects (49, 50), which write
    # through a null pointer in a battle; no one can learn the Detects, and only cheating casts Escape (docs/systems/combat-actions.md).
    # The charms (4, 17, 27) take only the entities their filter names, which target set B keeps to, so they're cast at real ones on
    # the parity logs' states below.
    for kind in range(59):
        if kind in (ESCAPE, 49, 50) or kind in CHARMS:
            continue
        t = 0 if kind == TELEPORTATION else 1 if kind in FOR_PARTY else 4
        add(f'kind{kind}', (0, t), f'learn:0,{kind},5', script=CASTS, steer=[NOT_RESISTED])

    # The roll: Dragon Flames (6) at level 5 at rat 4, its d100 from the least to past the chance, the adjusted roll at or
    # under min(Wizard, a fifth of the chance) among them; and a Wizard of 3, which caps the level and the adjusted roll.
    DF = 'learn:0,6,5'
    for r in (1, 5, 10, 11, 30, 60, 99, 100):
        add(f'DF-roll{r}', (0, 4), DF, script={0: d100(r)}, steer=[NOT_RESISTED])
    for r in (1, 3, 4, 50, 80):
        add(f'DF-wizard3-roll{r}', (0, 4), DF + '|skill:0,Wizard,3', script={0: d100(r)}, steer=[NOT_RESISTED])
    # The chance's terms: Intelligence, the level, the Level, and the least chance of 5.
    for pk in ('mod:0,Intelligence,0', 'mod:0,Intelligence,40', 'base:0,Level,0', 'base:0,Level,60', 'mod:0,Intelligence,0|base:0,Level,0'):
        for r in (4, 6, 40):
            add(f'DF-{pk.replace(":0,", "-").replace(",", "").replace("|", "-")}-roll{r}', (0, 4), DF + '|' + pk, script={0: d100(r)},
                steer=[NOT_RESISTED])

    # Each part of the day, for Solar Alaron and for him made Lunar (whose chance goes by the moon instead), and the Troubadour's song
    # for the party's side.
    for part in range(5):
        for r in (40, 70, 99):
            add(f'DF-daypart{part}-roll{r}', (0, 4), DF + f'|daypart:{part}', script={0: d100(r)}, steer=[NOT_RESISTED])
            add(f'DF-daypart{part}-lunar-roll{r}', (0, 4), DF + f'|daypart:{part}|aspect:0,1', script={0: d100(r)}, steer=[NOT_RESISTED])
    for b in (1, 5, 21):
        add(f'DF-troubadour{b}', (0, 4), DF + f'|troubadour:{b}', script={0: d100(99)}, steer=[NOT_RESISTED])

    # The resist roll: rat 4's (Willpower x 3 + Endurance + Level), a d100 over it doesn't resist, else its step off the margin.
    for r in (1, 5, 10, 15, 20, 25, 30, 32, 33, 100):
        add(f'DF-resist{r}', (0, 4), DF, script=CASTS, steer=[('Combatant.ResistRoll', d100(r))])
        add(f'DF-roll1-resist{r}', (0, 4), DF, script={0: d100(1)}, steer=[('Combatant.ResistRoll', d100(r))])
    for pk in ('mod:4,Willpower,40', 'mod:4,Endurance,60', 'base:4,Level,50', 'mod:4,Willpower,0|mod:4,Endurance,0|base:4,Level,0'):
        for r in (10, 50, 90):
            add(f'DF-{pk.replace(":4,", "-").replace(",", "").replace("|", "-")}-resist{r}', (0, 4), DF + '|' + pk, script=CASTS,
                steer=[('Combatant.ResistRoll', d100(r))])

    # The aspects in the resist: Whitefire (58, Solar) and Lightning (9, Lunar), cast by Solar Alaron or Lunar Sholeh, at the Lunar rat
    # or the rat made Solar: x0.75 when the two share an aspect, x1.25 when they don't. Sholeh casts with Alaron's Intelligence.
    for kind in (58, 9, 6):
        for caster, cpk in ((0, ''), (3, '|mod:3,Intelligence,25')):
            for tpk, ttag in (('', 'lunar'), ('|aspect:4,2', 'solar')):
                for r in (20, 30, 40):
                    add(f'aspect-k{kind}-c{caster}-{ttag}-resist{r}', (caster, 4), f'learn:{caster},{kind},5' + cpk + tpk, script=CASTS,
                        steer=[('Combatant.ResistRoll', d100(r))])

    # The wards (38-41, +6 a level to the resist against their school), the first by slot: Dragon Flames (Elemental), Banishing (15,
    # Naming), Crushing Death (28, Necromancy), Whitefire (58, Star), each under its own ward and under another's; and two at once.
    for kind, ward in ((6, 38), (15, 39), (28, 40), (58, 41)):
        for w, tag in ((ward, 'own'), (38 if ward != 38 else 41, 'other')):
            for r in (30, 60):
                add(f'ward-k{kind}-{tag}-resist{r}', (0, 4), f'learn:0,{kind},5|effect:4,{w},3,5,4', script=CASTS,
                    steer=[('Combatant.ResistRoll', d100(r))])
    for r in (30, 60):
        add(f'ward-two-resist{r}', (0, 4), 'learn:0,6,5|effect:4,38,1,5,4|effect:4,38,6,5,4', script=CASTS,
            steer=[('Combatant.ResistRoll', d100(r))])
        add(f'ward-then-own-resist{r}', (0, 4), 'learn:0,6,5|effect:4,41,6,5,4|effect:4,38,2,5,4', script=CASTS,
            steer=[('Combatant.ResistRoll', d100(r))])

    # Mirror's counter: any resist step at all resists while it lasts, and spends the difference from the margin.
    for n in (1, 3, 9, 15, 40):
        for r in (5, 25, 32, 33):
            add(f'mirror{n}-resist{r}', (0, 4), DF + f'|mirror:4,{n}', script=CASTS, steer=[('Combatant.ResistRoll', d100(r))])
    add('mirror3-roll1-resist25', (0, 4), DF + '|mirror:4,3', script={0: d100(1)}, steer=[('Combatant.ResistRoll', d100(25))])

    # The resistances that meet a spell's damage: its elements, its school's, its aspect's, Magic; a weakness making the rat's morale
    # check due.
    entries = (('fire1.5', [(FIRE, '1.5')]), ('fire0.5', [(FIRE, '0.5')]), ('fire0', [(FIRE, '0')]), ('magic0.5', [(MAGIC, '0.5')]),
               ('magic2', [(MAGIC, '2')]), ('elemental0.25', [(ELEMENTAL, '0.25')]), ('star1.5', [(STAR, '1.5')]),
               ('solar0.5', [(SOLAR, '0.5')]), ('lunar1.25', [(LUNAR, '1.25')]), ('water0', [(WATER, '0')]), ('earth1.5', [(EARTH, '1.5')]),
               ('air0.5', [(AIR, '0.5')]), ('necro2', [(NECROMANCY, '2')]), ('naming0.5', [(NAMING, '0.5')]),
               ('two', [(FIRE, '0.5'), (MAGIC, '3')]), ('tiny', [(MAGIC, '0.01')]), ('physical0', [(PHYSICAL, '0')]))
    for kind in (6, 7, 8, 9, 11, 15, 28, 58):
        for tag, es in entries:
            pk = f'learn:0,{kind},5' + ''.join(f'|resist:4,{i},{e},{m}' for i, (e, m) in enumerate(es))
            add(f'resistance-k{kind}-{tag}', (0, 4), pk, script=CASTS, steer=[NOT_RESISTED])

    # Not enough Stamina for the cost, max(1, the spell's - L): Dragon Flames at 5 costs 5, at 10 costs 1 (with Wizard 10).
    for s in (0, 1, 4, 5, 6):
        add(f'DF-stamina{s}', (0, 4), DF + f'|mod:0,Stamina,{s}|cur:0,Stamina,{s}', script=CASTS, steer=[NOT_RESISTED])
    for s in (0, 1):
        add(f'DF10-stamina{s}', (0, 4), f'learn:0,6,10|mod:0,Stamina,{s}|cur:0,Stamina,{s}', script=CASTS, steer=[NOT_RESISTED])

    # A requirement unmet and met: Aura of Death (25) takes only the Solar, Solar Wrath (47) only the Lunar, Wraith Touch (26) and
    # Remove Poison (2) only those with a True Name (the party), Earth Smite (7) and Web (57) only the small (the heavy flag).
    for kind in (25, 47, 26, 2, 7, 57):
        add(f'require-k{kind}-rat', (0, 4), f'learn:0,{kind},5', script=CASTS, steer=[NOT_RESISTED])
        add(f'require-k{kind}-ratsolar', (0, 4), f'learn:0,{kind},5|aspect:4,2', script=CASTS, steer=[NOT_RESISTED])
        add(f'require-k{kind}-rheda', (0, 1), f'learn:0,{kind},5', script=CASTS, steer=[NOT_RESISTED])

    # A caster that has acted this turn casts at the margin it kept, with no roll, reagent or Stamina: a margin of 7, of 0, and none.
    for m in (7, 0, 0xFF, 0xFE):
        add(f'DF-acted{m}', (0, 4), DF + f'|acted:0,{m}', script=CASTS, steer=[NOT_RESISTED])
        add(f'web-acted{m}', (3, 4), f'acted:3,{m}|learn:3,57,8', script=CASTS, steer=[NOT_RESISTED])

    # The level's rise, cast or not: a d100 at or under (5 - L)^2 x 0.1, halved on a failure (ResolveCastRoll), for L 0 and 1, and 2,
    # which can't. A failure is the roll at the chance or over.
    for level in (0, 1, 2):
        for r in (1, 2, 3):
            add(f'rise-L{level}-cast-roll{r}', (0, 4), f'learn:0,6,{level}', script=CASTS,
                steer=[('Combatant.ResolveCastRoll', d100(r)), NOT_RESISTED])
            add(f'rise-L{level}-failed-roll{r}', (0, 4), f'learn:0,6,{level}', script={0: d100(100)},
                steer=[('Combatant.ResolveCastRoll', d100(r))])
    return out
