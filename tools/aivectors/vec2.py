"""Session 11: running a case (see vec.py)."""
from vec import *


def run_style(ctx):
    """EnemyAI.RunStyle(ai, &x, &z) -> goal cell and what it chose"""
    cpu, S = ctx.cpu, ctx.S
    px, pz = 0x80500900, 0x80500904
    cpu.wr32(px, 0); cpu.wr32(pz, 0)
    cpu.run(0x80059628, [ctx.ai, px, pz], maxsteps=2_000_000_000)
    return dict(goal=[S.rd8(px), S.rd8(pz)], target=s8(S.rd8(ctx.ai + 5)), spell=[S.rd8(ctx.ent + 0x3d), S.rd8(ctx.ent + 0x3e)], kind=S.rd8(ctx.ai + 4),
                wanted=S.rd8(ctx.ai + 7), sortRng=S.rd32(SORT), rng=S.rd32(RNG), listCount=S.rd16(0x800e0be2))


def arrive(ctx):
    """The body reaches the end of its leg: the last point it was sent to (the queue's last, else its current goal)."""
    S, cpu = ctx.S, ctx.cpu
    b = ctx.body
    q = queue(S, b)
    if q['items']:
        idx = (q['tail'] - 1) % 16
        it = [i for i in q['items'] if i[0] == idx]
        x, z = (it[0][1], it[0][2]) if it else (q['items'][-1][1], q['items'][-1][2])
    else:
        x, z = q['goal'][0], q['goal'][1]
    S.setfl(b + 0x68, x); S.setfl(b + 0x70, z)
    S.c.wr16(b + 0x1e, S.rd16(b + 0x1e) & ~0x10)       # no destination left
    return [x, z]


def drive(ctx, cast=True):
    """From the state Step left: the walk ends (the body at its leg's end), state 5 -> 6, then 6 -> Execute -> 6 -> the action."""
    cpu, S = ctx.cpu, ctx.S
    out = dict(trace=[])
    ticks = 2
    st = lambda: (S.rd8(0x800d9454), S.rd8(0x800d9455))
    out['trace'].append(['Step', list(st())])
    if st()[0] == 5:
        out['arrival'] = arrive(ctx)
        S.wr8(0x800d94d8, 0)
        cpu.run(0x80021fd0, [0x80500000, ticks], maxsteps=20_000_000)           # Combat.StateMoving
        out['trace'].append(['StateMoving', list(st())])
    for it in range(3):
        if st()[0] == 6:
            S.c.wr16(ctx.body + 0x1e, S.rd16(ctx.body + 0x1e) & ~0x20)           # the turning is over
            if st()[1] == 6 and not cast:
                break
            if st() == (6, 6):
                out['execute'] = state_after(S, ctx.c, ctx.ai, ctx.body, ctx.ent)   # the state the action starts from
            try:
                cpu.run(0x800221b0, [0x80500000, ticks], maxsteps=100_000_000)      # Combat.StateFacing
            except Exception as e:
                out['trace'].append(['StateFacing error', str(e)])
                break
            out['trace'].append(['StateFacing', list(st()), 'target', S.rd8(S.m + 0x14)])
        else:
            break
    return out


def spell_values(ctx):
    """for each of the AI's action slots holding a spell: the spell chosen, set B built at the caster's place, the original's SpellValue and counts"""
    cpu, S = ctx.cpu, ctx.S
    out = []
    ce = ctx.ent
    SI = [0, 1, 2, 3, 4, 0, 0, 0]; SK = [1, 1, 1, 1, 1, 2, 3, 4]
    for i in range(8):
        kind = S.rd8(ctx.ai + 10 + i)
        if kind == 0xff: continue
        S.wr8(ce + 0x3e, SI[i]); S.wr8(ce + 0x3d, SK[i])
        sp = cpu.run(0x80070cb4, [ce]) & 0xffffffff
        lvl = cpu.run(0x80070e68, [ce, sp]) & 0xff
        cost = cpu.run(0x80070efc, [ce, sp, lvl]) & 0xff
        tset_rebuild(S, B_SET, ctx.p)
        b = tset(S, B_SET)
        area = None
        if kind == 8:
            cnt = cpu.run(0x80058008, [ctx.ai], maxsteps=100_000_000)
            cnt = ((cnt + 0x80000000) & 0xffffffff) - 0x80000000
            area = dict(count=cnt, focus=[S.fl(S.m + 0x18), S.fl(S.m + 0x1c)], setB=tset(S, B_SET))
        val = cpu.run(0x80057498, [ctx.ai, kind], maxsteps=10_000_000)
        val = ((val + 0x80000000) & 0xffffffff) - 0x80000000
        out.append(dict(index=i, spellKind=kind, source=SK[i], place=SI[i], info=spell_info(S, sp), castLevel=lvl, castCost=cost,
                        staminaModified=S.rd8(S.rd32(ce + 0x18) + 0x15 + 5), setB=b, spellValue=val, targetCountScore=b['nsel'] - 1, area=area))
    return out


def run_case(case):
    """case: dict(id, cp, slot, eid, mods, seeds, step_seeds, style_seeds)"""
    import time
    t0 = time.time()
    ctx = build(case['cp'], case['slot'], case['eid'], case.get('mods', ()))
    pre = pre_state(ctx)
    begin_turn(ctx, sortseed=case['seeds'][0])
    pre['afterBeginTurn'] = dict(setA=tset(ctx.S, 0x4e64), setB=tset(ctx.S, 0x4f0c), sortRng=ctx.S.rd32(SORT), rng=ctx.S.rd32(RNG), lastTarget=ctx.S.rd8(ctx.S.m + 0x14), focus=[ctx.S.fl(ctx.S.m + 0x18), ctx.S.fl(ctx.S.m + 0x1c)])
    res = dict(id=case['id'], pre=pre)
    res['spells'] = spell_values(fork(ctx))
    base = fork(ctx)
    raw, steps = raw_score(base)
    res['raw'] = dict(entries=raw, steps=steps)
    snap = bytes(base.cpu.m)
    res['choose'] = []
    for sd in case['seeds']:
        f = fork(base)
        f.cpu.m[:] = snap
        f.cpu.wr32(SORT, sd)
        if raw:
            tail(f)
        res['choose'].append(dict(sortSeed=sd, returned=bool(raw), entries=entries(f.S) if raw else [], sortRng=f.cpu.rd32(SORT)))
    if case.get('check', True):
        f = fork(ctx)
        f.cpu.wr32(SORT, case['seeds'][0])
        r, ent2, st2 = choose_full(f)
        res['chooseFull'] = dict(sortSeed=case['seeds'][0], returned=bool(r), entries=ent2, sortRng=f.cpu.rd32(SORT), steps=st2,
                                 equalsTail=(ent2 == res['choose'][0]['entries']))
    res['runStyle'] = []
    for sd in case.get('style_seeds', case['seeds'][:2]):
        f = fork(ctx)
        f.cpu.wr32(SORT, sd)
        rs = run_style(f); rs['sortSeed'] = sd
        res['runStyle'].append(rs)
    res['step'] = []
    for sd in case.get('step_seeds', case['seeds'][:2]):
        f = fork(ctx)
        f.cpu.wr32(SORT, sd)
        f.cpu.run(0x8005b314, [f.ai], maxsteps=2_000_000_000)             # EnemyAI.Step
        row = dict(sortSeed=sd, afterStep=state_after(f.S, f.c, f.ai, f.body, f.ent), steps=f.cpu.steps)
        if case.get('drive', True):
            f.cpu.hooks.update(CAST_HK)
            row['drive'] = drive(f, cast=case.get('cast', True))
            row['final'] = state_after(f.S, f.c, f.ai, f.body, f.ent)
            row['finalCombatants'] = combatants(f.S)
        res['step'].append(row)
    res['seconds'] = time.time() - t0
    return res
