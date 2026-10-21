"""The cases: one entity built by 1.0's own builder in a rat's place at a combat checkpoint, with changes to the battle (the mods of
vec.apply_mod), and the sort-stream seeds to run it for. 123 in four batches, then the one-off vectors (single, kinds, aican, misc).

A case's id names the checkpoint, the rat's slot, the entity id and its name and what the case changes: cp08-s5-24b-Sorceror-nostamina."""
SEEDS = [1, 2, 3, 0x1234, 0xdeadbeef]

# entity id -> short name
ENT = {0x24b: 'Sorceror', 0x27c: 'Spellweaver', 0x21a: 'Hobgoblin', 0x21c: 'BanditBoss', 0x21f: 'LizardManBoss', 0x233: 'Gechina', 0x20d: 'ChaosMajor',
       0x202: 'FemaleDryad', 0x262: 'Lugash', 0x264: 'Wight', 0x249: 'Keelin', 0x20f: 'ChaosScout', 0x213: 'Shamsuk', 0x221: 'Marquis', 0x224: 'Pochanargat',
       0x245: 'ChaosStormer', 0x207: 'Wyvern', 0x2c6: 'BanditBoss2C6',
       0x247: 'Dougal', 0x24a: 'Niesen', 0x261: 'Firelord', 0x21d: 'Kitarak', 0x294: 'Golnar', 0x24d: 'Sholeh'}


# ---- batch 1: every caster and boss, and the changes that decide what they do
def batch1():
    out = []

    def add(cp, slot, eid, mods=(), tag='', **kw):
        cid = 'cp%02d-s%d-%03x-%s%s' % (cp, slot, eid, ENT.get(eid, ''), ('-' + tag) if tag else '')
        d = dict(id=cid, cp=cp, slot=slot, eid=eid, mods=list(mods), seeds=SEEDS)
        d.update(kw)
        out.append(d)

    base = [0x24b, 0x27c, 0x21a, 0x21c, 0x21f, 0x233, 0x20d, 0x202, 0x262, 0x264, 0x249, 0x20f, 0x213, 0x221, 0x224, 0x245, 0x207]
    for e in base:
        add(8, 5, e)
    for e in (0x24b, 0x27c, 0x21a, 0x21c, 0x21f, 0x233, 0x20d, 0x202, 0x262, 0x264, 0x249, 0x224):
        add(2, 4, e)
    for e in (0x24b, 0x21f, 0x233, 0x262, 0x21c, 0x202, 0x249):
        add(9, 4, e)                 # engaged with Brenna: radius 1
    for e in (0x24b, 0x21f, 0x264):
        add(10, 4, e)
    for e in (0x24b, 0x21f, 0x233, 0x262):
        add(11, 6, e)
    # mods
    for e in (0x24b, 0x21f, 0x262, 0x202):
        add(8, 5, e, [('stamina', 5, 0)], tag='nostamina')
    for e in (0x24b, 0x262, 0x21f):
        add(8, 5, e, [('stamina', 5, 4)], tag='stamina4')
    for e in (0x24b, 0x21f, 0x233):
        add(8, 5, e, [('danger', 7)], tag='danger7')
    add(2, 4, 0x21f, [('danger', 12)], tag='danger12')
    add(2, 4, 0x233, [('danger', 5)], tag='danger5')
    add(8, 5, 0x24b, [('effect', 4, 48, 5, 100, 5), ('effect', 5, 48, 5, 100, 5)], tag='shielded')       # Starlight Shield already on
    add(8, 5, 0x21a, [('effect', 4, 35, 3, 100, 5), ('effect', 5, 35, 3, 100, 5)], tag='shielded')       # Spirit Shield already on
    add(8, 5, 0x24b, [('stamina', 1, 0)], tag='partystamina0')
    add(8, 5, 0x21a, [('stamina', 1, 0), ('stamina', 0, 0)], tag='partystamina0')
    add(8, 5, 0x24b, [('radius', 0)], tag='radius0')
    add(8, 5, 0x21f, [('radius', 0)], tag='radius0')
    add(8, 5, 0x24b, [('move', 5, 61.0, 54.0)], tag='near')
    add(8, 5, 0x21f, [('move', 5, 62.0, 52.0)], tag='near')
    add(8, 5, 0x24b, [('spellsel', 1, 2)], tag='sel2')
    for e in (0x24b, 0x21f, 0x262):
        add(9, 4, e, [('aibytes', {'4': 0, '7': 0}), ('spellsel', 0, 0)], tag='afterweapon')       # a caster that fought with its weapon last turn
    add(8, 5, 0x264, [('daypart', 4)], tag='night')
    add(8, 5, 0x213, [('daypart', 4)], tag='night')
    return out


# ---- batch 2: the Fireball paths (every other action slot switched off), the weapon fallbacks of styles 7, 8 and 10, the dispels, the wrap
FB = {0x21f: [1], 0x233: [0, 1, 3], 0x20d: [1, 2, 3], 0x224: [1, 2, 3], 0x247: [1, 2, 3, 4], 0x24a: [0, 1, 2, 3, 4], 0x261: [0, 1, 2, 3]}   # the action slots (index into +0x0A) to switch off; the Fireball stays


def off(eid):
    return ('aibytes', {str(10 + i): 0xff for i in FB[eid]})


def batch2():
    out = []

    def add(cp, slot, eid, mods=(), tag='', **kw):
        cid = 'cp%02d-s%d-%03x-%s%s' % (cp, slot, eid, ENT.get(eid, ''), ('-' + tag) if tag else '')
        d = dict(id=cid, cp=cp, slot=slot, eid=eid, mods=list(mods), seeds=SEEDS)
        d.update(kw)
        out.append(d)

    for e in (0x21f, 0x233, 0x20d, 0x224):
        add(8, 5, e, [off(e)], tag='fireball')
    for e in (0x21f, 0x233, 0x20d):
        add(2, 4, e, [off(e)], tag='fireball')
    add(9, 4, 0x21f, [off(0x21f)], tag='fireball')
    add(11, 6, 0x21f, [off(0x21f)], tag='fireball')
    add(8, 5, 0x21f, [off(0x21f), ('danger', 7)], tag='fireball-danger7')
    add(8, 5, 0x21f, [off(0x21f), ('radius', 0)], tag='fireball-radius0')
    add(8, 5, 0x247, [off(0x247)], tag='fireball')
    # the wrap: the party spread so that its centroid is in the rats' cluster and only two are within the Fireball's reach of it
    add(2, 4, 0x21f, [off(0x21f), ('move', 0, 50.0, 55.0), ('move', 1, 78.0, 55.0), ('move', 2, 64.0, 51.0), ('move', 3, 64.0, 59.0)], tag='fireball-wrap')
    add(2, 4, 0x20d, [off(0x20d), ('move', 0, 50.0, 55.0), ('move', 1, 78.0, 55.0), ('move', 2, 64.0, 51.0), ('move', 3, 64.0, 59.0)], tag='fireball-wrap')
    # the weapon fallbacks of styles 7, 8, 10 (and 5, 6, 9 once more, engaged): no Stamina
    for e in (0x21c, 0x249, 0x20f, 0x221, 0x2c6, 0x213):
        add(8, 5, e, [('stamina', 5, 0)], tag='nostamina')
        add(9, 4, e, [('stamina', 4, 0)], tag='nostamina')
    for e in (0x21c, 0x249, 0x20f, 0x24b, 0x262):
        add(2, 4, e, [('stamina', 4, 0)], tag='nostamina')
    add(11, 6, 0x21c, [('stamina', 6, 0)], tag='nostamina')
    # the dispels: a harmful effect of the right school on an ally (Clumsiness, Star) so that Dispel Star scores
    add(8, 5, 0x264, [('effect', 4, 52, 3, 100, 5)], tag='dispelstar')
    add(2, 4, 0x264, [('effect', 5, 52, 3, 100, 5), ('effect', 6, 52, 3, 100, 5)], tag='dispelstar')
    # Remove Poison / charm are not in the data; Darkness by night
    add(8, 5, 0x262, [('daypart', 4)], tag='night')
    add(8, 5, 0x27c, [('daypart', 4)], tag='night')
    # a ByLevel spell with more candidates than targets
    add(2, 4, 0x245, [], tag='')
    add(8, 5, 0x245, [('effect', 5, 35, 3, 100, 5)], tag='x')
    # casters that fought with their weapon last turn, away from any foe (the spell, and the weapon turn's leftovers)
    add(2, 4, 0x24b, [('aibytes', {'4': 0, '7': 0}), ('spellsel', 0, 0)], tag='afterweapon')
    add(8, 5, 0x21c, [('aibytes', {'4': 1, '7': 1}), ('spellsel', 0, 0)], tag='afterranged')
    add(9, 4, 0x21c, [('aibytes', {'4': 1, '7': 1}), ('spellsel', 0, 0)], tag='afterranged')
    return out


# ---- batch 3: spells from the weapon (action slot 7, source 4)
def batch3():
    out = []
    def add(cp, slot, eid, mods=(), tag=''):
        cid = 'cp%02d-s%d-%03x-%s%s' % (cp, slot, eid, ENT.get(eid, ''), ('-' + tag) if tag else '')
        out.append(dict(id=cid, cp=cp, slot=slot, eid=eid, mods=list(mods), seeds=SEEDS))
    only7 = lambda n: ('aibytes', {str(10 + i): 0xff for i in range(n)})
    add(8, 5, 0x262, [only7(5)], 'weaponspell')                       # Lugash: Web Of Starlight on his weapon
    add(8, 5, 0x262, [only7(5), ('stamina', 5, 3)], 'weaponspell-st3')  # cost 5 at level 5: refused
    add(8, 5, 0x262, [only7(5), ('stamina', 5, 5)], 'weaponspell-st5')
    add(8, 5, 0x21d, [only7(3)], 'weaponspell')                        # Kitarak: Darkness
    add(8, 5, 0x213, [only7(5)], 'weaponspell')                        # Shamsuk: Control Zombies (no zombie: value 0)
    add(8, 5, 0x294, [only7(4)], 'weaponspell')                        # Golnar: Dragon Flames on the weapon
    add(9, 4, 0x262, [only7(5)], 'weaponspell')
    add(2, 4, 0x262, [only7(5)], 'weaponspell')
    return out


# ---- batch 4: spells from the armor and the shield (action slots 5 and 6, sources 2 and 3), synthetic: the data gives no enemy one
def batch4():
    out = []
    def add(cp, slot, eid, mods, tag):
        out.append(dict(id='cp%02d-s%d-%03x-%s-%s' % (cp, slot, eid, ENT.get(eid, ''), tag), cp=cp, slot=slot, eid=eid, mods=list(mods), seeds=SEEDS))
    off5 = ('aibytes', {str(10 + i): 0xff for i in range(5)})
    add(8, 5, 0x24b, [off5, ('itemspell', 'armor', 0x73e)], 'armorspell')                        # Web Of Starlight on the armor
    add(8, 5, 0x24b, [off5, ('itemspell', 'shield', 0x757)], 'shieldspell')                      # Dragon Flames on the shield
    add(8, 5, 0x24b, [off5, ('itemspell', 'armor', 0x73e), ('stamina', 5, 3)], 'armorspell-st3')  # cost 5: refused
    add(8, 5, 0x24b, [off5, ('itemspell', 'armor', 0x738), ('itemspell', 'shield', 0x73e)], 'armor-and-shield')
    add(9, 4, 0x24b, [off5, ('itemspell', 'armor', 0x73e)], 'armorspell')
    return out


def all_cases():
    return batch1() + batch2() + batch3() + batch4()
