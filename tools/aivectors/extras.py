"""The vectors that aren't a caster's turn: ScoreSpellCells for one action slot at a time (single), SpellValue by kind (kinds),
Combatant.AICanCast (aican), and the area danger and the morale spells (misc). Each is run in the interpreter on a checkpoint's RAM."""
from vec2 import *

SINGLE_CASES = [
    dict(id='single-cp08-s5-Sorceror', cp=8, slot=5, eid=0x24b, mods=[]),
    dict(id='single-cp08-s5-Hobgoblin', cp=8, slot=5, eid=0x21a, mods=[]),
    dict(id='single-cp08-s5-LizardManBoss', cp=8, slot=5, eid=0x21f, mods=[]),
    dict(id='single-cp08-s5-Pochanargat', cp=8, slot=5, eid=0x224, mods=[]),
    dict(id='single-cp08-s5-Wight-dispel', cp=8, slot=5, eid=0x264, mods=[('effect', 4, 52, 3, 100, 5)]),
    dict(id='single-cp02-s4-Gechina', cp=2, slot=4, eid=0x233, mods=[]),
    dict(id='single-cp09-s4-Sorceror-engaged', cp=9, slot=4, eid=0x24b, mods=[]),
    dict(id='single-cp08-s5-Sorceror-radius0', cp=8, slot=5, eid=0x24b, mods=[('radius', 0)]),
]


def run_single(case):
    ctx = build(case['cp'], case['slot'], case['eid'], case['mods'])
    begin_turn(ctx, sortseed=1)
    S = ctx.S
    full_fork = fork(ctx)
    full, steps = raw_score(full_fork)
    out = dict(id=case['id'], checkpoint=case['cp'], slot=case['slot'], entity=case['eid'], mods=[list(m) for m in case['mods']], radius=ctx.c.radius,
               aiSlots=[S.rd8(ctx.ai + 10 + k) for k in range(8)], full=full, perSlot=[], independent=True)
    for i in range(8):
        if S.rd8(ctx.ai + 10 + i) == 0xff: continue
        f = fork(ctx)
        for k in range(8):
            if k != i: f.S.wr8(f.ai + 10 + k, 0xff)
        ent, st = raw_score(f)
        flt = [e for e in full if e['slot'] == i + 1]
        out['perSlot'].append(dict(index=i, kind=S.rd8(ctx.ai + 10 + i), entries=ent, matchesFilteredFull=(ent == flt)))
        if ent != flt: out['independent'] = False
    return out


def kinds():
    """SpellValue (0x80057498) for every kind 0..58 in a neutral context and in contexts that satisfy its conditions."""
    out = dict(neutral={}, conditional=[])


    def ctx_with_B(cp=8, cand=(0, 1, 2, 3, 4, 5)):
        ctx = build(cp, 5, 0x24b)
        begin_turn(ctx, sortseed=1)
        S = ctx.S
        B = S.m + B_SET
        # set B: the given slots as candidates (the flags SpellValue reads)
        for k in range(14):
            S.c.wr32(B + 4 * k, 1 if k in cand else 0)
        return ctx


    def val(ctx, kind):
        v = ctx.cpu.run(0x80057498, [ctx.ai, kind], maxsteps=10_000_000)
        return ((v + 0x80000000) & 0xffffffff) - 0x80000000


    ctx = ctx_with_B()
    for k in range(59):
        out['neutral'][k] = val(ctx, k)

    # conditional kinds
    EL = []
    cpu = ctx.cpu
    db = cpu.rd32(0x800e2830); ent0 = cpu.rd32(db + 0x10)
    for i in range(221):
        eid = cpu.rd16(ent0 + i * 0x80)
        if cpu.run(0x8006e110, [eid]) & 0xff: EL.append(eid)


    def with_effect(kind, school_kind, power=3, timer=50, who=4, harmful=None):
        c = ctx_with_B()
        S = c.S
        p = S.rd32(S.m + 0x52d0 + 4 * who)
        c.cpu.run(0x800702f0, [S.rd32(p + 0x40), school_kind, power, timer, 3, p], maxsteps=2_000_000)
        return c


    # Remove Poison (kind 2): a candidate poisoned (kind 36)
    c = with_effect(2, 36)
    out['conditional'].append(dict(kind=2, name='Remove Poison', context='candidate slot 4 poisoned', value=val(c, 2)))
    # Control Elem (4) and Banishing (15): an elemental candidate
    for kk, nm in ((4, 'Control Elem'), (15, 'Banishing')):
        c = ctx_with_B()
        S = c.S
        p = S.rd32(S.m + 0x52d0 + 4 * 4)
        S.c.wr16(S.rd32(p + 0x40), EL[0])
        out['conditional'].append(dict(kind=kk, name=nm, context='candidate slot 4 is entity %04x (an elemental)' % EL[0], value=val(c, kk)))
    # Control Zombies (27): a Zombie 0x266 / Plague Zombie 0x28c
    for zid in (0x266, 0x28c):
        c = ctx_with_B()
        S = c.S
        p = S.rd32(S.m + 0x52d0 + 4 * 4)
        S.c.wr16(S.rd32(p + 0x40), zid)
        out['conditional'].append(dict(kind=27, name='Control Zombies', context='candidate slot 4 is entity %04x' % zid, value=val(c, 27)))
    # Darkness (29) / Light (54): part of the day and the aspect balance
    for part in (1, 2, 3, 4):
        c = ctx_with_B()
        S = c.S
        w = c.cpu.rd32(0x800e303c); S.wr8(w + 2, part)
        row = dict(part=part, darkness=val(c, 29), light=val(c, 54))
        # balance: how many combatants are hindered by the current time, by side
        bal = []
        for k in range(S.rd8(S.m + 1)):
            p = S.rd32(S.m + 0x52d0 + 4 * k)
            bal.append((k, c.cpu.run(0x800615f4, [p]) & 0xff, (S.rd16(p + 0x2c) >> 4) & 1))
        row['isAspectOpposed(slot, opposed, hostileSide)'] = bal
        out['conditional'].append(dict(kind='29/54', name='Darkness/Light', context='World+2 = %d' % part, **row))
    # dispels (42-45): schools 1, 2, 3, 5; a harmful effect of the school on an ally (the caster's side) and a helpful one on a party member
    for kk, school_kind, nm in ((42, None, 'Dispel Elemental'), (43, None, 'Dispel Naming'), (44, None, 'Dispel Necro'), (45, None, 'Dispel Star')):
        school = {42: 1, 43: 2, 44: 3, 45: 5}[kk]
        # effect kinds by school: Elemental: 3 (Air Shield, helpful) 5 (Debilitation, harmful); Naming: 13 Brilliance(h) 14 Stupidity(harm); Necro: 31 Exhaustion(harm) 32 Stamina(help); Star: 48 Starlight Shield (help) 52 Clumsiness (harm)
        helpful = {1: 3, 2: 13, 3: 32, 5: 48}[school]
        harmful = {1: 5, 2: 14, 3: 31, 5: 52}[school]
        for desc, who, ek in (('none', None, None), ('helpful effect on a party member (slot 0)', 0, helpful), ('harmful effect on a party member (slot 0)', 0, harmful),
                              ('harmful effect on an ally (slot 4)', 4, harmful), ('helpful effect on an ally (slot 4)', 4, helpful)):
            c = ctx_with_B()
            if who is not None:
                S = c.S
                p = S.rd32(S.m + 0x52d0 + 4 * who)
                c.cpu.run(0x800702f0, [S.rd32(p + 0x40), ek, 3, 50, 3, p], maxsteps=2_000_000)
            out['conditional'].append(dict(kind=kk, name=nm, context=desc, value=val(c, kk)))
    return out


def aican():
    """Combatant.AICanCast (0x80060A94) directly: Stamina, charges, requirement, part of the day, for known spells and an item's spell."""
    rows = []


    def go(eid, slot_index, label):
        ctx = build(8, 5, eid)
        S, cpu = ctx.S, ctx.cpu
        ce = ctx.ent
        SI = [0, 1, 2, 3, 4, 0, 0, 0]; SK = [1, 1, 1, 1, 1, 2, 3, 4]
        S.wr8(ce + 0x3e, SI[slot_index]); S.wr8(ce + 0x3d, SK[slot_index])
        sp = cpu.rd32 and (cpu.run(0x80070cb4, [ce]) & 0xffffffff)
        info = spell_info(S, sp)
        tgt = S.rd32(S.m + 0x52d0 + 4 * 0)                     # Alaron: the target whose requirement is read
        tce = S.rd32(tgt + 0x40)
        st = S.rd32(ce + 0x18)
        base_mod = S.rd8(st + 0x15 + 5)
        lst = S.rd32(ce + 0x24) and S.rd32(S.rd32(ce + 0x24) + 0x10)
        for stamina in (0, 1, 2, 3, 4, 5, 8, 10, 15, 20, 60):
            for charges in ((None,) if SK[slot_index] == 1 else (0, 1, 3)):
                for special, part in ((None, 1),) if True else ():
                    c2 = fork(ctx)
                    S2, cpu2 = c2.S, c2.cpu
                    st2 = S2.rd32(c2.ent + 0x18)
                    S2.wr8(st2 + 0x15 + 5, stamina)
                    if charges is not None:
                        item = S2.rd32(c2.ent + 0x24)
                        S2.wr8(S2.rd32(item + 0x10) + 4, charges)
                    r = cpu2.run(0x80060a94, [c2.p, tgt, sp], maxsteps=2_000_000) & 0xff
                    lvl = cpu2.run(0x80070e68, [c2.ent, sp]) & 0xff
                    cost = cpu2.run(0x80070efc, [c2.ent, sp, lvl]) & 0xff
                    rows.append(dict(label=label, entity=eid, actionSlot=slot_index, spell=info['name'], kind=info['kind'], source=SK[slot_index], spellLevel=info['level'], castLevel=lvl, cost=cost,
                                     staminaModified=stamina, charges=charges, aiFlags=S2.rd8(c2.ai + 9), result=bool(r)))


    go(0x24b, 0, 'Sorceror, Web Of Starlight (known)')
    go(0x24b, 2, 'Sorceror, Whitefire (known)')
    go(0x24b, 3, 'Sorceror, Lightning (known)')
    go(0x262, 7, 'Lugash, his weapon\'s spell (item, source 4)')
    go(0x213, 7, 'Shamsuk, his weapon\'s spell (item, source 4)')

    # the part of the day and the requirement for a spell struct edited to carry them (no spell in the data does)
    ctx = build(8, 5, 0x24b)
    S, cpu = ctx.S, ctx.cpu
    ce = ctx.ent
    S.wr8(ce + 0x3e, 3); S.wr8(ce + 0x3d, 1)
    sp = cpu.run(0x80070cb4, [ce]) & 0xffffffff
    tgt = S.rd32(S.m + 0x52d0)
    for special in (0, 1, 2, 3, 4, 5, 6):
        for part in (1, 2, 3, 4):
            c2 = fork(ctx)
            S2 = c2.S
            S2.wr8(sp + 0x1b, special)
            w = c2.cpu.rd32(0x800e303c); S2.wr8(w + 2, part)
            r = c2.cpu.run(0x80060a94, [c2.p, tgt, sp], maxsteps=2_000_000) & 0xff
            req = c2.cpu.run(0x800712c0, [S2.rd32(tgt + 0x40), sp]) & 0xff
            tm = c2.cpu.run(0x80071260, [c2.ent, sp]) & 0xff
            rows.append(dict(label='edited spell struct (Whitefire) for the part of the day / requirement', special=special, partOfDay=part, targetEntity='%04x' % S2.rd16(S2.rd32(tgt + 0x40)),
                             requirement=req, timeAllows=tm, staminaModified=S2.rd8(S2.rd32(c2.ent + 0x18) + 0x15 + 5), result=bool(r)))
    # a combatant without an AI record or with flag 8 clear is always allowed
    c2 = fork(ctx)
    c2.S.wr8(c2.ai + 9, 0)
    sw = c2.S.rd32(c2.ent + 0x18); c2.S.wr8(sw + 0x15 + 5, 0)
    rows.append(dict(label='AI flag 8 clear, Stamina 0', aiFlags=0, result=bool(c2.cpu.run(0x80060a94, [c2.p, tgt, sp], maxsteps=2_000_000) & 0xff)))

    return rows


def misc():
    """Combat+0x12 (area danger), the morale spells and the flask tiers: vectors from the original's code."""
    OUT = {}


    def party_ents(cpu):
        party = cpu.rd32(0x800d9684)
        return party, [cpu.rd32(party + 4 * m) for m in range(4)]


    # ---- A. area danger -------------------------------------------------------------------------------------------------
    def trial(flasks=(), fireballs=(), wizard=None, dead=(), cp=8, hp_zero_flags=()):
        cpu = fresh(cp); cpu.hooks = dict(HK)
        S = Sim(cpu)
        party, ents = party_ents(cpu)
        inv = cpu.rd32(party + 0x10)
        for a in flasks: cpu.run(0x80073574, [inv, a, 1], maxsteps=2_000_000)
        for m, lvl in fireballs:
            cpu.run(0x8007ad70, [cpu.rd32(ents[m] + 0x2c), 0x305, lvl], maxsteps=5_000_000)
        for m, w in (wizard or {}).items():
            sk = cpu.rd32(ents[m] + 0x1c)
            cpu.wr8(sk + 0x23, w); cpu.wr8(sk + 0x0b, w)
        for m in dead:
            cpu.run(0x8006eb8c, [ents[m], 10000, 0], maxsteps=2_000_000)
        tier = cpu.run(0x8005c750, []) & 0xff
        S.wr8(S.m + 0x12, tier)                                       # Combat.Setup's store
        cpu.run(0x8005c7c8, [0], maxsteps=100_000_000)                # Combat.CreatePartyCombatants (the raise)
        danger = S.rd8(S.m + 0x12)
        # the effective casting level of each member's Fireball, as the original reads it
        info = []
        for m in range(4):
            e = ents[m]
            wiz = cpu.run(0x8007a85c, [cpu.rd32(e + 0x1c), 11]) & 0xff
            idx = 0
            r = cpu.run(0x8007aee0, [cpu.rd32(e + 0x2c), 0x305, 0x80500a00], maxsteps=1_000_000) & 0xff
            if r:
                idx = cpu.rd8(0x80500a00)
                sp = cpu.rd32(cpu.rd32(cpu.rd32(e + 0x2c)) + 4 * idx)
                info.append(dict(member=m, entity='%04x' % cpu.rd16(e), wizardModified=wiz, fireballIndex=idx, knownLevel=cpu.rd8(sp + 0x14)))
            else:
                info.append(dict(member=m, entity='%04x' % cpu.rd16(e), wizardModified=wiz, fireballIndex=None))
        return dict(tier=tier, danger=danger, party=info, countsAfter=[S.rd8(S.m + i) for i in range(1, 5)])


    cases = []
    for fl in ([], [0x1000], [0x1001], [0x1002], [0x1003], [0x1000, 0x1001], [0x1000, 0x1002], [0x1002, 0x1003], [0x1000, 0x1002, 0x1003], [0x1001, 0x1003]):
        cases.append(dict(flasks=[hex(x) for x in fl], fireballs=[], wizard={}, dead=[]))
    for fb, wz in (([(0, 3)], None), ([(1, 6)], None), ([(1, 6)], {1: 9}), ([(1, 6)], {1: 5}), ([(1, 6)], {1: 3}), ([(2, 4)], {2: 0}), ([(0, 1)], {0: 1}),
                   ([(0, 2), (1, 5)], {1: 9}), ([(3, 7)], {3: 7}), ([(3, 15)], {3: 15}), ([(2, 1), (3, 1)], {2: 3, 3: 3})):
        cases.append(dict(flasks=[], fireballs=fb, wizard=wz or {}, dead=[]))
    cases.append(dict(flasks=['0x1001'], fireballs=[(1, 6)], wizard={1: 5}, dead=[]))
    cases.append(dict(flasks=['0x1001'], fireballs=[(1, 6)], wizard={1: 3}, dead=[]))
    cases.append(dict(flasks=['0x1000'], fireballs=[(1, 6)], wizard={1: 2}, dead=[]))
    cases.append(dict(flasks=[], fireballs=[(1, 6)], wizard={1: 9}, dead=[1]))
    cases.append(dict(flasks=[], fireballs=[(0, 5), (1, 6)], wizard={0: 5, 1: 9}, dead=[1]))
    rows = []
    for c in cases:
        r = trial(flasks=[int(x, 16) for x in c['flasks']], fireballs=c['fireballs'], wizard={int(k): v for k, v in c['wizard'].items()}, dead=c['dead'])
        r['case'] = dict(flasks=c['flasks'], fireballs=[list(f) for f in c['fireballs']], wizard={str(k): v for k, v in c['wizard'].items()}, dead=c['dead'])
        rows.append(r)
    OUT['danger'] = rows

    # ---- B. the morale spells -------------------------------------------------------------------------------------------
    mor = []
    for kind in (0x19, 0x2f):
        for morale, power, timer in ((5, 3, 5), (5, 7, 3), (2, 3, 4), (0, 4, 4), (11, 15, 6), (7, 4, 2)):
            cpu = fresh(8); cpu.hooks = dict(HK)
            S = Sim(cpu)
            p = S.rd32(S.m + 0x52d0 + 4 * 5)
            c = Cmb(S, p); ai = c.ai; ent = c.ent
            S.wr8(ai + 6, morale)
            row = dict(kind=kind, morale0=morale, power=power, timer=timer)
            r = cpu.run(0x800702f0, [ent, kind, power, timer, 7, p], maxsteps=2_000_000) & 0xffff
            row['slot'] = r
            row['moraleAfterStart'] = S.rd8(ai + 6)
            row['aiFlagsAfterStart'] = S.rd8(ai + 9)
            efp = cpu.rd32(ent + 0x34)
            rec = cpu.rd32(efp + 4 * r) if r != 0xffff else 0
            row['record'] = [S.rd8(rec + k) for k in range(0x18)] if rec else None
            # a second cast while it holds: refused
            r2 = cpu.run(0x800702f0, [ent, kind, power, timer, 7, p], maxsteps=2_000_000) & 0xffff
            row['secondCast'] = r2; row['moraleAfterSecond'] = S.rd8(ai + 6)
            # the round's ageing until the record goes
            rounds = []
            for n in range(1, 12):
                cpu.run(0x80070a3c, [ent, p, 1], maxsteps=2_000_000)
                gone = (cpu.rd32(efp + 4 * r) == 0)
                rounds.append(dict(round=n, morale=S.rd8(ai + 6), recordPresent=not gone))
                if gone: break
            row['rounds'] = rounds
            row['moraleAtEnd'] = S.rd8(ai + 6)
            mor.append(row)
    # removal by the other paths: Dispel (Aura of Death is Necromancy: Dispel Necro), a lethal hit, Leave
    for path in ('dispel', 'lethal', 'death', 'removeall'):
        cpu = fresh(8); cpu.hooks = {0x80021564: lambda cc: 0, 0x80066aec: lambda cc: 0, 0x800c25c0: lambda cc: 0, 0x800be860: lambda cc: 0}
        S = Sim(cpu)
        p = S.rd32(S.m + 0x52d0 + 4 * 5)
        c = Cmb(S, p); ai = c.ai; ent = c.ent
        S.wr8(ai + 6, 5)
        r = cpu.run(0x800702f0, [ent, 0x19, 3, 99, 7, p], maxsteps=2_000_000) & 0xffff
        m1 = S.rd8(ai + 6)
        row = dict(path=path, moraleAfterStart=m1)
        efp = cpu.rd32(ent + 0x34)
        if path == 'dispel':
            # Entity.Dispel(kind 44) on the target with a generous margin
            row['ret'] = cpu.run(0x80071cd8, [ent, p, 44, 50], maxsteps=2_000_000) & 0xff
        elif path == 'lethal':
            cpu.run(0x8006eb8c, [ent, 10000, p], maxsteps=2_000_000)
        elif path == 'death':
            cpu.run(0x80062080, [p], maxsteps=2_000_000)
        elif path == 'removeall':
            cpu.run(0x8006701c, [p], maxsteps=2_000_000)
        row['moraleAfter'] = S.rd8(ai + 6)
        row['recordPresent'] = cpu.rd32(efp + 4 * r) != 0
        mor.append(row)
    OUT['moraleSpells'] = mor

    return OUT
