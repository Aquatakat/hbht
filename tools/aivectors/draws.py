"""The main stream's draws in the interpreter: each one recorded with the function that drew it, and any of them scripted.

Every draw on the main stream (0x800D8088) goes through Rng.Next (0x80093198, a0 the stream) or Rng.Float0To1 (0x800933D4), which
hooks replace here: each records
the draw and, for a scripted one, sets the stream first so that the draw comes out as the script's value. The port's side does the same
through Lcg.BeforeDraw (FormulaState.Draws), so a vector's draws are the same on both sides whatever each side drew before them.

A scripted value is the generator's raw 32-bit output. A d100 of r is any value v with (100 * v) >> 32 == r - 1 (Rng.Below), and
value_for(r - 1, 100) gives the middle one, so a script can say "this d100 comes up 7" without depending on how the draw is turned.
"""
import bisect
import os
import re

import rig

RNG = 0x800D8088                  # gGlobals + 0: the main stream
RNG_NEXT = 0x80093198             # Rng.Next(stream): Below, Range and RollD draw through it
RNG_FLOAT = 0x800933D4            # Rng.Float0To1(stream), which steps the stream itself
A, C = 0x19660D, 0x3C6EF35F
A_INV = pow(A, -1, 1 << 32)
M32 = 0xFFFFFFFF


def step(seed):
    return (seed * A + C) & M32


def seed_before(value):
    """The seed whose next draw is `value`."""
    return ((value - C) * A_INV) & M32


def value_for(result, n):
    """The middle raw value v for which Below(n) = (n * v) >> 32 is `result`."""
    lo = -(-(result << 32) // n)
    hi = (((result + 1) << 32) - 1) // n
    return (lo + hi) // 2


def d100(r):
    """The raw value of a d100 (Range(1, 100), Below(100) + 1) that comes up r."""
    return value_for(r - 1, 100)


# ---- the symbol map, to name a draw's callers
_symbols = None


def symbols():
    global _symbols
    if _symbols is None:
        rows = []
        for line in open(os.path.join(rig.REPO, 'tools', 'symbols', 'us.txt'), encoding='utf-8'):
            m = re.match(r'^(\S+) = 0x([0-9a-fA-F]+);', line)
            if m and int(m.group(2), 16) < 0x800D0000:
                rows.append((int(m.group(2), 16), m.group(1)))
        rows.sort()
        _symbols = ([a for a, _ in rows], [n for _, n in rows])
    return _symbols


def name(address):
    starts, names = symbols()
    i = bisect.bisect_right(starts, address) - 1
    return names[i] if i >= 0 else '%08x' % address


GENERATOR = ('Rng.', 'RandomExtensions.')


class Draws:
    """Installs itself on a CPU: `script` maps a draw's index (0 first) to its raw value; `drawn` lists (index, old seed, new value,
    caller) for every draw on the main stream, the caller the first function up the stack outside the generator."""

    def __init__(self, cpu, script=None):
        self.cpu = cpu
        self.script = dict(script or {})
        self.drawn = []
        cpu.hooks = dict(getattr(cpu, 'hooks', None) or {})
        cpu.hooks[RNG_NEXT] = self._next
        cpu.hooks[RNG_FLOAT] = self._float
        self._stack = []

    def _caller(self):
        """The first function up the stack outside the generator: from ra, each generator frame unwound by its prologue (its
        `addiu sp, sp, -N` and `sw ra, off(sp)`), as tools/gdb/parity.py unwinds a draw's chain."""
        cpu = self.cpu
        ra, sp = cpu.getr(31) & M32, cpu.getr(29) & M32
        for _ in range(8):
            n = name(ra - 8)
            if not n.startswith(GENERATOR) and n != '__udivdi3':
                return n
            start = symbols()[0][bisect.bisect_right(symbols()[0], ra - 8) - 1]
            size = saved = None
            for at in range(start, ra, 4):
                m, ops = cpu.decode(at)
                if m == 'addiu' and ops[:2] == ['$sp', '$sp'] and int(ops[2], 0) < 0:
                    size = -int(ops[2], 0)
                elif m == 'sw' and ops[0] == '$ra' and ops[1].endswith('($sp)'):
                    saved = int(ops[1][:-5] or '0', 0)
                if size is not None and saved is not None:
                    break
            if size is None or saved is None:
                return '?'
            ra, sp = cpu.rd32(sp + saved), sp + size
        return '?'

    def _draw(self, cpu):
        stream = cpu.getr(4) & M32
        seed = cpu.rd32(stream)
        if stream == RNG:
            i = len(self.drawn)
            if i in self.script:
                seed = seed_before(self.script[i])
            self.drawn.append((i, seed, step(seed), self._caller()))
        new = step(seed)
        cpu.wr32(stream, new)
        return new

    def _next(self, cpu):
        new = self._draw(cpu)
        return new - (1 << 32) if new & 0x80000000 else new

    def _float(self, cpu):
        # (float)(double)(u32)new * 2^-32 in single precision (0x800D52E8), into $f0.
        import numpy as np
        new = self._draw(cpu)
        cpu.setfs(0, np.float32(np.float32(np.float64(new)) * np.float32(2.3283064365386963e-10)))
        return 0

    def callers(self):
        return [d[3] for d in self.drawn]
