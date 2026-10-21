"""Session 11: run 1.0's own enemy-caster AI (ChooseSpellOrAttack, RunStyle, Step, Execute) on checkpoint RAM with an enemy caster overlaid."""
from ovl import *
import json, struct, copy

HK = {0x800c25c0: lambda cc: 0, 0x800be860: lambda cc: 0, 0x800bd910: lambda cc: 0, 0x8006701c: lambda cc: 0, 0x80071754: lambda cc: 0,
      0x80086230: lambda cc: 0, 0x8008aa68: lambda cc: 0, 0x8008c880: lambda cc: 0, 0x80021564: lambda cc: 0, 0x80066aec: lambda cc: 0}
# the sound / visual calls a cast makes (session 9's stubs): AttachSpellVisual, and the cast scene
CAST_HK = {0x80064b10: lambda cc: 0, 0x80089b80: lambda cc: 0, 0x8008a000: lambda cc: 0, 0x8008a128: lambda cc: 0, 0x8009eae8: lambda cc: 0,
           0x8008c43c: lambda cc: 7, 0x80065bfc: lambda cc: 0, 0x8008d1f0: lambda cc: 0}
SORT = 0x800e6b60
SPELLLIST = 0x800e2b40


def s8(v):
    v &= 0xff
    return v - 256 if v & 0x80 else v


def cstr(cpu, a, n=80):
    o = a & 0x7fffff
    return bytes(cpu.m[o:o + n]).split(b'\0')[0].decode('latin1')


class Ctx:
    pass


def spell_info(S, sp):
    if not sp: return None
    c = S.c
    idv = S.rd16(sp)
    low = idv & 0xff
    kind = None
    for k in range(0x3b):
        if S.rd8(SPELLLIST + k) == low: kind = k; break
    return dict(id=idv, kind=kind, name=cstr(c, S.rd32(sp + 4), 24), level=S.rd8(sp + 0x14), school=S.rd8(sp + 0x15), damage=S.rd8(sp + 0x16),
                cost=S.rd8(sp + 0x17), count=S.rd8(sp + 0x18), type=S.rd8(sp + 0x19), wizard=S.rd8(sp + 0x1a), special=S.rd8(sp + 0x1b),
                range=S.rd8(sp + 0x1c), unk1e=S.rd8(sp + 0x20), charges=S.rd8(sp + 4 + 0) if False else None)


def item_spell(S, item):
    """an item's first spell struct (item+0x10 -> list pointer): id, level, charges (+4 of the list header)"""
    if not item: return None
    lst = S.rd32(item + 0x10)
    if not lst: return None
    sp = S.rd32(lst)
    d = spell_info(S, sp)
    if d: d['charges'] = S.rd8(lst + 4)
    return d


def entity_info(S, ent):
    c = S.c
    st = S.rd32(ent + 0x18)
    sk = S.rd32(ent + 0x1c)
    book = S.rd32(ent + 0x2c)
    spells = []
    if book:
        arr = S.rd32(book); n = S.rd8(book + 4)
        for i in range(n):
            spells.append(spell_info(S, S.rd32(arr + 4 * i)))
    eq = S.rd32(ent + 0x20)
    wp = S.rd32(ent + 0x24)
    d = dict(id=S.rd16(ent), name=cstr(c, S.rd32(ent + 4), 24),
             statsBase=[S.rd8(st + 0xe + k) for k in range(7)], statsModified=[S.rd8(st + 0x15 + k) for k in range(7)], statsCurrent=[S.rd8(st + 7 + k) for k in range(7)],
             wizard=c.run(0x8007a85c, [sk, 11]) & 0xff, hp=c.run(0x8006e93c, [ent]), maxhp=c.run(0x8006e8d4, [ent]),
             spells=spells, sel=[S.rd8(ent + 0x3c), S.rd8(ent + 0x3d), S.rd8(ent + 0x3e)],
             weapon=None, armor=None, shield=None)
    if wp:
        d['weapon'] = dict(id=S.rd16(wp), cls=S.rd8(wp + 0x24), hit=S.rd8(wp + 0x25), dice=S.rd8(wp + 0x26), range=S.rd8(wp + 0x27), spell=item_spell(S, wp))
    if eq:
        a = S.rd32(eq); sh = S.rd32(eq + 4)
        if a: d['armor'] = dict(id=S.rd16(a), spell=item_spell(S, a))
        if sh: d['shield'] = dict(id=S.rd16(sh), spell=item_spell(S, sh))
    return d


def combatants(S):
    res = []
    for i in range(14):
        p = S.rd32(S.m + 0x52d0 + 4 * i)
        if not p: continue
        c = Cmb(S, p)
        e = c.ent
        st = S.rd32(e + 0x18)
        b = c.body()
        eff = []
        efp = S.rd32(e + 0x34)
        if efp:
            for k in range(15):
                r = S.rd32(efp + 4 * k)
                if r: eff.append([S.rd8(r), S.rd8(r + 1), S.rd8(r + 2), S.rd8(r + 3), S.rd32(r + 4), S.rd32(r + 8)])
        res.append(dict(slot=i, entity=S.rd16(e), x=S.fl(p), z=S.fl(p + 4), ox=S.fl(p + 8), oz=S.fl(p + 12), cell=list(c.cell()), dir=S.rd8(p + 0x14), flags=S.rd16(p + 0x2c),
                        radius=c.radius, fp=c.fp, action=S.rd8(p + 0x25), hp=S.c.run(0x8006e93c, [e]), maxhp=S.c.run(0x8006e8d4, [e]),
                        body=[S.fl(b + 0x68), S.fl(b + 0x6c), S.fl(b + 0x70)] if b else None, bodyRadius=[S.fl(b + 0x34), S.fl(b + 0x38)] if b else None,
                        weapon=S.rd16(S.rd32(e + 0x24)) if S.rd32(e + 0x24) else None,
                        stamina=dict(current=S.rd8(st + 7 + 5), modified=S.rd8(st + 0x15 + 5), base=S.rd8(st + 0xe + 5)),
                        dead=bool(S.c.run(0x8006ea54, [e]) & 0xff), effects=eff, lastTarget=s8(S.rd8(p + 0x10)),
                        ai=[S.rd8(c.ai + k) for k in range(0x1c)] if c.ai else None))
    return res


def counts(S):
    return dict(total=S.rd8(S.m + 1), active=S.rd8(S.m + 2), partyStart=S.rd8(S.m + 3), partyActive=S.rd8(S.m + 4), enemyStart=S.rd8(S.m + 5),
                enemyActive=S.rd8(S.m + 6), reserves=S.rd8(S.m + 7), leader=S.rd8(S.m + 0x10), leaderMorale=S.rd8(S.m + 0x11), danger=S.rd8(S.m + 0x12),
                leaderGone=S.rd32(S.m + 0xc), partOfDay=S.rd8(S.rd32(0x800e303c) + 2) if False else None)


def tset(S, off):
    B = S.m + off
    return dict(cand=[1 if S.rd32(B + 4 * k) else 0 for k in range(14)], sel=[1 if S.rd32(B + 0x48 + 4 * k) else 0 for k in range(14)],
                order=[S.rd8(B + 0x90 + k) for k in range(14)], mode=S.rd8(B + 0xa2), ncand=S.rd8(B + 0xa3), nsel=S.rd8(B + 0xa4), most=S.rd8(B + 0xa5),
                cursor=S.rd8(B + 0xa6), pos=S.rd8(B + 0xa7))


def entries(S):
    n = S.rd16(0x800e0be2); lst = S.rd32(0x800e0be4); out = []
    for i in range(n):
        e = lst + 8 * i
        t = S.rd32(e + 4)
        out.append(dict(score=S.rd8(e), x=S.rd8(e + 1), z=S.rd8(e + 2), slot=S.rd8(e + 3), combatant=(S.rd8(t + 0x20) if t else None)))
    return out


def queue(S, b):
    q = []
    for i in range(16):
        e = b + 0x154 + 16 * i
        if S.rd16(e + 12): q.append([i, S.fl(e), S.fl(e + 4), S.fl(e + 8), S.rd16(e + 14)])
    return dict(items=q, tail=S.rd16(b + 0x254), head=S.rd16(b + 0x256), flags=S.rd16(b + 0x1e), goal=[S.fl(b + 0x260), S.fl(b + 0x264), S.fl(b + 0x268)])


def logs(S):
    return [cstr(S.c, S.m + 0x4fc4), cstr(S.c, S.m + 0x50c4)]


def ai_info(S, ai):
    return dict(bytes=[S.rd8(ai + k) for k in range(0x1c)], spare=S.rd16(S.rd32(ai)) if S.rd32(ai) else None)


def state_after(S, c, ai, b, ent):
    p = c.p
    return dict(state=S.rd8(0x800d9454), next=S.rd8(0x800d9455), stuck=S.rd32(S.m + 0x4fbc), lastTarget=S.rd8(S.m + 0x14),
                focus=[S.fl(S.m + 0x18), S.fl(S.m + 0x1c)], castMargin=S.rd8(ent + 0x3c), spell=[S.rd8(ent + 0x3d), S.rd8(ent + 0x3e)],
                ai=ai_info(S, ai), target=s8(S.rd8(ai + 5)), kind=S.rd8(ai + 4), wanted=S.rd8(ai + 7), pathIdx=s8(S.rd8(ai + 0x12)), pathLen=S.rd8(ai + 0x13),
                path=[[S.rd8(S.m + 0x5320 + 2 * i), S.rd8(S.m + 0x5321 + 2 * i)] for i in range(S.rd8(ai + 0x13) if S.rd8(ai + 0x13) < S.diag() else 0)],
                x=S.fl(p), z=S.fl(p + 4), ox=S.fl(p + 8), oz=S.fl(p + 12), dir=S.rd8(p + 0x14), face=[S.fl(p + 0x18), S.fl(p + 0x1c)], flags=S.rd16(p + 0x2c),
                action=S.rd8(p + 0x25), weapon=S.rd16(S.rd32(ent + 0x24)) if S.rd32(ent + 0x24) else None,
                setA=tset(S, 0x4e64), setB=tset(S, 0x4f0c), queue=queue(S, b), rest=S.rd16(0x800db0d0), logs=logs(S),
                rng=S.rd32(RNG), sortRng=S.rd32(SORT), lastTargetOfCombatant=s8(S.rd8(p + 0x10)))


# ---- scenario ---------------------------------------------------------------------------------------------------------

def move_combatant(S, p, x, z):
    """put a combatant (and its body, keeping the body's height) at float place (x, z); the grid follows"""
    c = Cmb(S, p)
    cx, cz = c.cell()
    S.vacate(cx, cz, c.fp)
    S.setfl(p, float(x)); S.setfl(p + 4, float(z)); S.setfl(p + 8, float(x)); S.setfl(p + 12, float(z))
    b = c.body()
    if b:
        S.setfl(b + 0x68, float(x)); S.setfl(b + 0x70, float(z))
    nx, nz = c.cell()
    S.occupy(nx, nz, c.fp)


def set_stamina(S, p, value):
    st = S.rd32(S.rd32(p + 0x40) + 0x18)
    S.wr8(st + 7 + 5, value); S.wr8(st + 0x15 + 5, value)


def party_inv(cpu):
    party = cpu.rd32(0x800d9684)
    return cpu.rd32(party + 0x10)


def apply_mod(ctx, m):
    cpu, S = ctx.cpu, ctx.S
    op = m[0]
    if op == 'move':
        move_combatant(S, S.rd32(S.m + 0x52d0 + 4 * m[1]), m[2], m[3])
    elif op == 'stamina':
        set_stamina(S, S.rd32(S.m + 0x52d0 + 4 * m[1]), m[2])
    elif op == 'flask':
        cpu.run(0x80073574, [party_inv(cpu), m[1], 1], maxsteps=2_000_000)
    elif op == 'danger':
        S.wr8(S.m + 0x12, m[1])
    elif op == 'hp':
        e = S.rd32(S.rd32(S.m + 0x52d0 + 4 * m[1]) + 0x40)
        cpu.run(0x8006eb8c, [e, m[2], 0], maxsteps=2_000_000)        # Character.TakeDamage(entity, amount, combatant 0)
    elif op == 'radius':
        S.wr8(ctx.p + 0x21, m[1])
    elif op == 'aiflags':
        S.wr8(ctx.ai + 9, m[1])
    elif op == 'aibytes':
        for k, v in m[1].items(): S.wr8(ctx.ai + int(k), v)
    elif op == 'effect':       # Character.AddEffect(entity of slot, kind, power, timer, margin, combatant slot)
        p = S.rd32(S.m + 0x52d0 + 4 * m[1])
        cpu.run(0x800702f0, [S.rd32(p + 0x40), m[2], m[3], m[4], m[5], p], maxsteps=2_000_000)
    elif op == 'spellsel':     # the entity's chosen spell (+0x3D source, +0x3E place)
        e = ctx.ent
        S.wr8(e + 0x3d, m[1]); S.wr8(e + 0x3e, m[2])
    elif op == 'itemspell':    # an item carrying a spell worn as the armor (source 2) or shield (source 3): the weapon DB record m[2] built as an item (FUN_8007B670), its first spell's kind into the AI slot 5 or 6
        slot = 5 if m[1] == 'armor' else 6
        item = alloc(cpu, 0x2c, 0x800d2668)
        cpu.run(0x8007b670, [item, m[2]], maxsteps=2_000_000)
        eq = S.rd32(ctx.ent + 0x20)
        S.c.wr32(eq + (0 if m[1] == 'armor' else 4), item)
        lst = S.rd32(item + 0x10)
        sp = S.rd32(lst)
        low = S.rd16(sp) & 0xff
        kind = [k for k in range(0x3b) if S.rd8(SPELLLIST + k) == low][0]
        S.wr8(ctx.ai + 10 + slot, kind)
    elif op == 'daypart':      # the world's part of the day (World+2)
        w = cpu.rd32(0x800e303c)
        S.wr8(w + 2, m[1])
    else:
        raise ValueError(op)


def build(cp, slot, eid, mods=()):
    cpu = fresh(cp)
    cpu.hooks = dict(HK)
    ctx = Ctx()
    ctx.cp, ctx.slot, ctx.eid, ctx.mods = cp, slot, eid, list(mods)
    ctx.cpu = cpu
    ctx.rng0 = cpu.rd32(RNG)
    S, p, old = overlay(cpu, slot, eid)
    ctx.S, ctx.p, ctx.oldrec = S, p, old
    c = Cmb(S, p)
    ctx.c = c; ctx.ai = c.ai; ctx.ent = c.ent; ctx.body = c.body()
    ctx.grid0 = None
    for m in mods:
        apply_mod(ctx, m)
    # the radius is worked out as each turn begins (UpdateRadius, with the ring); a mod that moved or changed the caster is followed by it
    if not any(m[0] == 'radius' for m in mods):
        cpu.run(0x8005f29c, [p], maxsteps=5_000_000)
    return ctx


def begin_turn(ctx, sortseed=None, rngseed=None):
    """what TurnOrder.BeginTurn does for a combatant with an AI record, minus CheckFlee: the focus on it, state 8, both target sets built"""
    cpu, S, p = ctx.cpu, ctx.S, ctx.p
    cpu.wr32(S.m + 0x2c, p)
    # TurnOrder.Advance: radius, start place, then the focus (Combat+0x18/+0x1C) on the combatant, then BeginTurn
    cpu.run(0x80067074, [p], maxsteps=2_000_000)         # Combatant.SyncFromBody
    S.setfl(S.m + 0x18, S.fl(p)); S.setfl(S.m + 0x1c, S.fl(p + 4))
    cpu.run(0x8005f770, [p], maxsteps=2_000_000)         # Combatant.SaveStartPosition
    S.wr8(0x800d9454, 8); S.wr8(0x800d9455, 8)
    S.c.wr32(S.m + 0x4fbc, 0)
    tset_rebuild(S, A_SET, p)
    tset_rebuild(S, B_SET, p)
    if sortseed is not None: cpu.wr32(SORT, sortseed)
    if rngseed is not None: cpu.wr32(RNG, rngseed)


def fork(ctx):
    """a copy of the context on a copy of RAM (hooks kept)"""
    n = Ctx()
    n.__dict__.update(ctx.__dict__)
    cpu = fresh(ctx.cp)
    cpu.m[:] = ctx.cpu.m
    cpu.hooks = dict(ctx.cpu.hooks)
    n.cpu = cpu
    n.S = Sim(cpu)
    n.S.base_grid = getattr(ctx.S, 'base_grid', None)
    n.c = Cmb(n.S, ctx.p)
    return n


def pre_state(ctx):
    S = ctx.S
    return dict(checkpoint=ctx.cp, slot=ctx.slot, entity=ctx.eid, mods=[list(m) for m in ctx.mods], counts=counts(S), combatants=combatants(S),
                caster=entity_info(S, ctx.ent), casterAi=ai_info(S, ctx.ai), radius=ctx.c.radius, footprint=ctx.c.fp, sortRng=S.rd32(SORT), rng=S.rd32(RNG),
                partOfDay=S.rd8(S.rd32(0x800e303c) + 2) if False else None, gridWidth=S.width(), gridDepth=S.depth(),
                state=S.rd8(0x800d9454), setA=tset(S, 0x4e64), setB=tset(S, 0x4f0c), focus=[S.fl(S.m + 0x18), S.fl(S.m + 0x1c)], lastTarget=S.rd8(S.m + 0x14))


def raw_score(ctx):
    """EnemyAI.ScoreSpellCells alone: the entries as it adds them (unsorted)"""
    cpu, S = ctx.cpu, ctx.S
    steps0 = cpu.steps
    cpu.run(0x800581c8, [ctx.ai], maxsteps=2_000_000_000)
    return entries(S), cpu.steps


def tail(ctx):
    """what ChooseSpellOrAttack does after ScoreSpellCells when an entry exists"""
    cpu, S = ctx.cpu, ctx.S
    cpu.run(0x8005bfc4, [0], maxsteps=50_000_000)        # AiScores.Sort(0)
    cpu.run(0x8005c018, [], maxsteps=50_000_000)         # AiScores.KeepBest
    cpu.run(0x80058b90, [ctx.ai, 1], maxsteps=50_000_000)   # EnemyAI.ScoreByDistance(ai, 1)
    cpu.run(0x8005bfc4, [1], maxsteps=50_000_000)        # AiScores.Sort(1)


def choose_full(ctx):
    cpu, S = ctx.cpu, ctx.S
    r = cpu.run(0x800590b0, [ctx.ai], maxsteps=2_000_000_000)
    return r & 0xff, entries(S), cpu.steps
