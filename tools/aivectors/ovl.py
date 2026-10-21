"""Overlay an enemy caster entity onto a rat's combatant in a checkpoint's RAM, with the original's own builders."""
from sim import *
import struct

def hooks_for_equip(cpu):
    h = getattr(cpu, 'hooks', None) or {}
    h[0x80066aec] = lambda s: 0    # Combatant.EquipVisuals(c): the body's model; nothing for the AI
    cpu.hooks = h

def alloc(cpu, size, tag=0x800d2668):
    return cpu.run(0x8008e6b0, [size, tag, 0], maxsteps=2_000_000) & 0xffffffff

def overlay(cpu, slot, eid):
    """Replace the combatant at `slot` by a fresh one of entity `eid`, the way Combat.CreateEnemyCombatants and Combat.CreateBody do
    (FUN_8006E434 mode 1, Combatant.Init with EnemyAI.Init, UpdateRadius, the default action), keeping the rat's cell, direction, footprint,
    float position, body and slot."""
    hooks_for_equip(cpu)
    S = Sim(cpu)
    p = S.rd32(S.m + 0x52d0 + 4 * slot)
    old = bytes(cpu.m[p & 0x7fffff:(p & 0x7fffff) + 0x8c])
    c = Cmb(S, p)
    cx, cz = c.cell(); fp = c.fp
    ox, oz, oox, ooz = S.fl(p), S.fl(p + 4), S.fl(p + 8), S.fl(p + 12)
    direction = S.rd8(p + 0x14)
    S.vacate(cx, cz, fp)
    ent = alloc(cpu, 0x48)
    cpu.run(0x8006e434, [ent, eid, 1], maxsteps=20_000_000)
    # Combatant.Init(c, ent, -1 (unused), cellx, cellz, direction, footprint, isEnemy = 1, slot)
    cpu.run(0x8005eae0, [p, ent, 0xffffffff, cx, cz, direction, fp, 1, slot][:4], maxsteps=20_000_000) if False else None
    args = [p, ent, 0xffffffff, cx]
    cpu.run(0x8005eae0, args, maxsteps=20_000_000, sp_args=[cz, direction, fp, 1, slot])
    # the float position and the old position of the rat (Init puts the cell's corner); the cell is the same
    S.setfl(p, ox); S.setfl(p + 4, oz); S.setfl(p + 8, oox); S.setfl(p + 12, ooz)
    ncx, ncz = Cmb(S, p).cell()
    S.vacate(cx, cz, fp)          # Init occupied the cell it was given
    S.occupy(ncx, ncz, fp)
    cpu.run(0x8005f29c, [p], maxsteps=5_000_000)    # UpdateRadius (CreateBody's first act)
    # Combatant.SetDefaultAction(c, 0) minus EquipVisuals: the action from the wielded weapon
    w = S.rd32(ent + 0x24)
    act = 0
    if w:
        act = 1
        if S.rd8(w + 0x27) != 0:
            act = 4 if (cpu.run(0x8007b730, [w]) & 0xff) else 2
    S.wr8(p + 0x25, act)
    return S, p, old
