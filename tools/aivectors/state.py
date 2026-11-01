"""A battle's state in the interpreter for the formula vectors (combat parity, roadmap 2.9): where its RAM comes from, and the pokes that
change it, each a typed setter the port's side mirrors (tests/HBHT.Rom.Tests/Formulas/FormulaState.cs).

A state's source is one of:
    cp05                                  the Combat 05 checkpoint (roms/, rig.checkpoint_path)
    log:<session>/<battle>:<dump>         a parity log's RAM dump, out/parity/<session>/<battle>/<dump> (or HBHT_PARITY)

A poke is "name:arg,arg,...", several joined by "|", the text both sides parse. Slots are the combat struct's (0-13); stats and skills go by
the port's names (Stat, Skill, WeaponClass, Shield). The pokes:
    mod:slot,Stat,v        the stat's modified value                      (entity+0x18: current +7, base +0xE, modified +0x15)
    base:slot,Stat,v       its base value
    cur:slot,Stat,v        its current value
    skill:slot,Skill,v     a skill's base and modified levels             (entity+0x1C: base +slot, modified +0x18+slot)
    place:slot,x,z         the combatant to a place: its record's place and turn start, its body (its height kept), the grid's footprint
    face:slot,d            its facing and direction byte to direction d (0-7, sim.DX/DZ), its body's heading with it
    height:x,z,h           a grid cell's height (its low 7 bits; the taken bit kept)
    daypart:n              the world's part of the day (World+2: 1-3 by day, 4 night)
    troubadour:n           the Troubadour's bonus (Combat+0x13)
    weapon:slot,id         the entity's weapon (+0x24) a new instance of item id (hex, the DB's: 0x07xx), its stats left as they were
    noshield:slot          no shield (the armor pair's second, entity+0x20 +4)
    resist:slot,i,e,m      the combatant's resistance entry i (0-1: +0x30 + 8i, the element byte and the f32 multiplier at +4); element
                           0 is none
    aspect:slot,n          the sheet's aspect, 1 Lunar or 2 Solar (entity+0x14, +0xC bit 4 set for Solar)
    effect:slot,kind,power,timer,margin    Character.AddEffect (0x800702F0), as the spells lay one on
    acted:slot,margin      the combatant has acted this turn (flags +0x2C bit 0) with a cast's margin kept (entity+0x3C), as PrepareCast
                           leaves them
A family can add its own pokes (a dict of name -> function(State, args) passed to State.poke).
"""
import gzip
import os

import rig
import sim
from cpu import CPU, M32
from draws import Draws

COMBAT = 0x800E0BF0               # Combat.Current
WORLD = 0x800E303C                # World: +2 the part of the day
BODIES = 0x800D9488

STATS = ['Intelligence', 'Willpower', 'Dexterity', 'Endurance', 'Strength', 'Stamina', 'Level']
SKILLS = ['Alchemist', 'Diplomat', 'Healer', 'Loremaster', 'Mechanic', 'Merchant', 'Ranger', 'Stealth', 'Thief', 'Troubadour', 'Warrior',
          'Wizard', 'Bite', 'Breath', 'Claw', 'Hafted', 'Missile', 'Pole', 'Spit', 'Sting', 'Sword', 'Thrown', 'Tusk', 'Shield']

_cache = {}


def parity_dir():
    return os.environ.get('HBHT_PARITY') or os.path.join(rig.REPO, 'out', 'parity')


def ram(source):
    """A source's RDRAM, 8 MB big-endian."""
    if source not in _cache:
        if source.startswith('cp'):
            _cache[source] = rig.ram(int(source[2:]))
        elif source.startswith('log:'):
            folder, dump = source[4:].rsplit(':', 1)
            path = os.path.join(parity_dir(), folder, dump)
            if not os.path.exists(path):
                raise SystemExit(f'{path} not found; set HBHT_PARITY')
            data = open(path, 'rb').read()
            _cache[source] = gzip.decompress(data) if path.endswith('.gz') else data
        else:
            raise ValueError(source)
    return _cache[source]


class State:
    """A copy of a source's RAM in an interpreter, the main stream's draws recorded (`draws`), with the pokes applied."""

    def __init__(self, source, pokes='', extra=None, script=None, seed=None):
        self.source = source
        self.cpu = CPU(bytearray(ram(source)))
        self.cpu.hooks = {}
        self.S = sim.Sim(self.cpu)
        self.poke(pokes, extra)
        if seed is not None:
            self.cpu.wr32(sim.RNG, seed)
        self.draws = Draws(self.cpu, script)

    # ---- reading
    @property
    def combat(self):
        return self.cpu.rd32(COMBAT)

    def record(self, slot):
        return self.cpu.rd32(self.combat + 0x52D0 + 4 * slot)

    def entity(self, slot):
        return self.cpu.rd32(self.record(slot) + 0x40)

    def stats(self, slot):
        return self.cpu.rd32(self.entity(slot) + 0x18)

    def skills(self, slot):
        return self.cpu.rd32(self.entity(slot) + 0x1C)

    def body(self, slot):
        return self.cpu.rd32(BODIES + 4 * slot)

    def stat(self, slot, name, part='modified'):
        return self.cpu.rd8(self.stats(slot) + {'trained': 0, 'current': 7, 'base': 0xE, 'modified': 0x15}[part] + STATS.index(name))

    def skill(self, slot, name, part='modified'):
        v = self.cpu.rd8(self.skills(slot) + (0x18 if part == 'modified' else 0) + SKILLS.index(name))
        return v - 256 if v & 0x80 else v

    def call(self, address, args=(), sp_args=(), maxsteps=50_000_000):
        return self.cpu.run(address, list(args), sp_args=list(sp_args), maxsteps=maxsteps)

    # ---- the pokes
    def poke(self, text, extra=None):
        for item in (text or '').split('|'):
            if not item:
                continue
            name, _, args = item.partition(':')
            args = args.split(',') if args else []
            fn = (extra or {}).get(name) or POKES.get(name)
            if fn is None:
                raise ValueError(f'no poke {name!r}')
            fn(self, args)


def _int(s):
    return int(s, 0)


def _stat(part):
    off = {'current': 7, 'base': 0xE, 'modified': 0x15}[part]

    def poke(st, a):
        st.cpu.wr8(st.stats(_int(a[0])) + off + STATS.index(a[1]), _int(a[2]))
    return poke


def _skill(st, a):
    sk = st.skills(_int(a[0]))
    i = SKILLS.index(a[1])
    st.cpu.wr8(sk + i, _int(a[2]))
    st.cpu.wr8(sk + 0x18 + i, _int(a[2]))


def _place(st, a):
    S, p = st.S, st.record(_int(a[0]))
    c = sim.Cmb(S, p)
    x, z = float(a[1]), float(a[2])
    S.vacate(*c.cell(), c.fp)
    for off, v in ((0, x), (4, z), (8, x), (12, z)):
        S.setfl(p + off, v)
    b = c.body()
    if b:
        S.setfl(b + 0x68, x)
        S.setfl(b + 0x70, z)
    S.occupy(*c.cell(), c.fp)


def _face(st, a):
    slot, d = _int(a[0]), _int(a[1])
    S, p = st.S, st.record(slot)
    x, z = sim.vnorm(sim.DX[d], sim.DZ[d])
    S.setfl(p + 0x18, float(x))
    S.setfl(p + 0x1C, float(z))
    st.cpu.wr8(p + 0x14, d)


def _height(st, a):
    x, z, h = _int(a[0]), _int(a[1]), _int(a[2])
    v = st.S.cell(x, z)
    st.S.setcell(x, z, (v & 0x80) | (h & 0x7F))


def _daypart(st, a):
    st.cpu.wr8(st.cpu.rd32(WORLD) + 2, _int(a[0]))


def _troubadour(st, a):
    st.cpu.wr8(st.combat + 0x13, _int(a[0]))


def _weapon(st, a):
    ent = st.entity(_int(a[0]))
    item = alloc(st.cpu, 0x2C)
    st.call(0x8007B670, [item, _int(a[1])])          # the weapon DB record built as an item instance
    st.cpu.wr32(ent + 0x24, item)


def _noshield(st, a):
    eq = st.cpu.rd32(st.entity(_int(a[0])) + 0x20)
    if eq:
        st.cpu.wr32(eq + 4, 0)


def _resist(st, a):
    p = st.record(_int(a[0])) + 0x30 + 8 * _int(a[1])
    st.cpu.wr8(p, _int(a[2]))
    st.S.setfl(p + 4, float(a[3]))


def _aspect(st, a):
    # The sheet's (entity+0x14) +0xC bit 4: Solar set, Lunar clear (Combatant.AspectScale, 0x80062CF8).
    sheet = st.cpu.rd32(st.entity(_int(a[0])) + 0x14)
    v = st.cpu.rd8(sheet + 0xC)
    st.cpu.wr8(sheet + 0xC, (v | 4) if _int(a[1]) == 2 else (v & ~4))


def _effect(st, a):
    p = st.record(_int(a[0]))
    st.call(0x800702F0, [st.cpu.rd32(p + 0x40), _int(a[1]), _int(a[2]), _int(a[3]), _int(a[4]), p])


def _acted(st, a):
    p = st.record(_int(a[0]))
    st.cpu.wr16(p + 0x2C, st.cpu.rd16(p + 0x2C) | 1)
    st.cpu.wr8(st.entity(_int(a[0])) + 0x3C, _int(a[1]))


def alloc(cpu, size, tag=0x800D2668):
    return cpu.run(0x8008E6B0, [size, tag, 0], maxsteps=2_000_000) & M32


POKES = {
    'mod': _stat('modified'), 'base': _stat('base'), 'cur': _stat('current'), 'skill': _skill, 'place': _place, 'face': _face,
    'height': _height, 'daypart': _daypart, 'troubadour': _troubadour, 'weapon': _weapon, 'noshield': _noshield, 'resist': _resist,
    'aspect': _aspect, 'effect': _effect, 'acted': _acted,
}
