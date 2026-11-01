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

    damage = st.call(RESOLVE_MELEE, [pa, pt])
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

def cases():
    out = []

    def add(tag, args, pokes='', source='cp05', seed=SEED, script=None):
        out.append(dict(id=f'{source.split(":")[0] if source.startswith("cp") else "log"}-{tag}', source=source, pokes=pokes, args=list(args),
                        seed=seed, script=script or {}))

    # The rolls on the plain state: Alaron (0) at rat 4, and rat 4 at Alaron, whose shield and Warrior count.
    for r in (1, 6, 7, 50, 100):
        add(f'alaron-rat4-roll{r}', (0, 4), script={0: d100(r)})
    for r in (1, 20, 40, 60, 100):
        add(f'rat4-alaron-roll{r}', (4, 0), script={0: d100(r)})
    return out
