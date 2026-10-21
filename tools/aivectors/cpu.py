"""A small MIPS III interpreter (FR=0) for running the original's functions on a checkpoint's RAM."""
import struct, re, math
import numpy as np
import rig
CODE = {a: (m, o) for a, m, o in rig.code()}
REGN = {n: i for i, n in enumerate('zero at v0 v1 a0 a1 a2 a3 t0 t1 t2 t3 t4 t5 t6 t7 s0 s1 s2 s3 s4 s5 s6 s7 t8 t9 k0 k1 gp sp fp ra'.split())}
REGN['s8'] = 30
M32 = 0xffffffff
M64 = 0xffffffffffffffff


def sx32(v):
    v &= M32
    return v - (1 << 32) if v & 0x80000000 else v


def u64(v): return v & M64


def s64(v):
    v &= M64
    return v - (1 << 64) if v >> 63 else v


def reg(s):
    s = s.strip()
    if s.startswith('$f') and s[2:].isdigit():
        return int(s[2:])
    return REGN[s[1:]] if not s[1:].isdigit() else int(s[1:])


mem_re = re.compile(r'^(-?(?:0x)?[0-9a-fA-F]*)\((\$\w+)\)$')


class Halt(Exception):
    pass


class CPU:
    def __init__(s, ram):
        s.m = ram
        s.r = [0] * 32
        s.f = [0] * 32
        s.hi = 0
        s.lo = 0
        s.cc = False
        s.pc = 0
        s.steps = 0
        s.parsed = {}
        s.trace = None
        s.calls = None   # list of (addr,ra) for jal targets when set

    def o(s, a):
        a &= M32
        if 0x80000000 <= a < 0x80800000:
            return a & 0x7fffff
        if 0xa0000000 <= a < 0xa0800000:
            return a & 0x7fffff
        raise Halt('bad addr %08x pc=%08x' % (a, s.pc))

    def rd8(s, a): return s.m[s.o(a)]

    def rd16(s, a):
        o = s.o(a)
        return (s.m[o] << 8) | s.m[o + 1]

    def rd32(s, a): return struct.unpack_from('>I', s.m, s.o(a))[0]

    def rd64(s, a): return struct.unpack_from('>Q', s.m, s.o(a))[0]

    def wr8(s, a, v): s.m[s.o(a)] = v & 0xff

    def wr16(s, a, v): struct.pack_into('>H', s.m, s.o(a), v & 0xffff)

    def wr32(s, a, v): struct.pack_into('>I', s.m, s.o(a), v & M32)

    def wr64(s, a, v): struct.pack_into('>Q', s.m, s.o(a), v & M64)

    def fs(s, n): return np.float32(struct.unpack('>f', struct.pack('>I', s.f[n] & M32))[0])

    def setfs(s, n, v):
        v = float(np.float32(v))
        try:
            s.f[n] = struct.unpack('>I', struct.pack('>f', v))[0]
        except OverflowError:
            s.f[n] = 0x7f800000 if v > 0 else 0xff800000

    def fd(s, n):
        bits = (s.f[n + 1] << 32) | s.f[n]
        return np.float64(struct.unpack('<d', struct.pack('<Q', bits & M64))[0])

    def setfd(s, n, v):
        bits = struct.unpack('<Q', struct.pack('<d', float(v)))[0]
        s.f[n] = bits & M32
        s.f[n + 1] = (bits >> 32) & M32

    def decode(s, a):
        p = s.parsed.get(a)
        if p is None:
            if a not in CODE:
                raise Halt('no code at %08x' % a)
            m, o = CODE[a]
            ops = [x.strip() for x in o.split(',')] if o else []
            p = (m, ops)
            s.parsed[a] = p
        return p

    def getr(s, n): return 0 if n == 0 else s.r[n]

    def setr(s, n, v):
        if n:
            s.r[n] = u64(v)

    def sr(s, n): return s64(s.getr(n))

    def run(s, entry, args=(), stack=0x80600000, ra=0x8006a124, maxsteps=5_000_000, sp_args=()):
        s.r = [0] * 32
        s.f = [0] * 32
        s.setr(29, stack)
        for i, v in enumerate(args[:4]):
            s.setr(4 + i, v)
        for i, v in enumerate(list(args[4:]) + list(sp_args)):
            s.wr32(stack + 0x10 + 4 * i, v)
        s.setr(31, ra)
        s.pc = entry
        s.steps = 0
        while s.pc != ra:
            s.step()
            if s.steps > maxsteps:
                raise Halt('too many steps')
        return sx32(s.r[2])

    def step(s):
        pc = s.pc
        m, ops = s.decode(pc)
        s.steps += 1
        if s.trace is not None:
            s.trace.append(pc)
        npc = pc + 4
        br = s.exec(pc, m, ops)
        if br is None:
            s.pc = npc
            return
        kind, target, cond = br
        if m in ('jal', 'jalr') and s.calls is not None:
            s.calls.append((target, pc + 8))
        if kind == 'likely' and not cond:
            s.pc = npc + 4
            return
        m2, ops2 = s.decode(npc)
        s.steps += 1
        if s.trace is not None:
            s.trace.append(npc)
        s.exec(npc, m2, ops2)
        s.pc = target if cond else npc + 4

    def exec(s, pc, m, ops):
        g = s.getr
        sr = s.sr
        setr = s.setr

        def T(x): return int(x, 0)
        if m in ('nop', 'cache', 'sync', 'mtc0', 'break'):
            return
        if m == 'mfc0':
            setr(reg(ops[0]), 0); return
        if m in ('addiu', 'addi'):
            setr(reg(ops[0]), sx32(sr(reg(ops[1])) + T(ops[2]))); return
        if m == 'lui':
            setr(reg(ops[0]), sx32(T(ops[1]) << 16)); return
        if m == 'move':
            setr(reg(ops[0]), g(reg(ops[1]))); return
        if m in ('addu', 'add'):
            setr(reg(ops[0]), sx32(g(reg(ops[1])) + g(reg(ops[2])))); return
        if m in ('subu', 'sub'):
            setr(reg(ops[0]), sx32(g(reg(ops[1])) - g(reg(ops[2])))); return
        if m == 'negu':
            setr(reg(ops[0]), sx32(-g(reg(ops[1])))); return
        if m == 'not':
            setr(reg(ops[0]), ~g(reg(ops[1]))); return
        if m == 'and':
            setr(reg(ops[0]), g(reg(ops[1])) & g(reg(ops[2]))); return
        if m == 'or':
            setr(reg(ops[0]), g(reg(ops[1])) | g(reg(ops[2]))); return
        if m == 'xor':
            setr(reg(ops[0]), g(reg(ops[1])) ^ g(reg(ops[2]))); return
        if m == 'nor':
            setr(reg(ops[0]), ~(g(reg(ops[1])) | g(reg(ops[2])))); return
        if m == 'andi':
            setr(reg(ops[0]), g(reg(ops[1])) & T(ops[2])); return
        if m == 'ori':
            setr(reg(ops[0]), g(reg(ops[1])) | T(ops[2])); return
        if m == 'xori':
            setr(reg(ops[0]), g(reg(ops[1])) ^ T(ops[2])); return
        if m == 'slt':
            setr(reg(ops[0]), 1 if sr(reg(ops[1])) < sr(reg(ops[2])) else 0); return
        if m == 'sltu':
            setr(reg(ops[0]), 1 if g(reg(ops[1])) < g(reg(ops[2])) else 0); return
        if m == 'slti':
            setr(reg(ops[0]), 1 if sr(reg(ops[1])) < T(ops[2]) else 0); return
        if m == 'sltiu':
            setr(reg(ops[0]), 1 if g(reg(ops[1])) < (T(ops[2]) & M64) else 0); return
        if m == 'sll':
            setr(reg(ops[0]), sx32((g(reg(ops[1])) & M32) << T(ops[2]))); return
        if m == 'srl':
            setr(reg(ops[0]), sx32((g(reg(ops[1])) & M32) >> T(ops[2]))); return
        if m == 'sra':
            setr(reg(ops[0]), sx32(sx32(g(reg(ops[1]))) >> T(ops[2]))); return
        if m == 'sllv':
            setr(reg(ops[0]), sx32((g(reg(ops[1])) & M32) << (g(reg(ops[2])) & 31))); return
        if m == 'srlv':
            setr(reg(ops[0]), sx32((g(reg(ops[1])) & M32) >> (g(reg(ops[2])) & 31))); return
        if m == 'srav':
            setr(reg(ops[0]), sx32(sx32(g(reg(ops[1]))) >> (g(reg(ops[2])) & 31))); return
        if m in ('div', 'divu', 'mult', 'multu') and len(ops) == 3:
            ops = ops[1:]
        if m == 'mult':
            p = sx32(g(reg(ops[0]))) * sx32(g(reg(ops[1])))
            s.lo = sx32(p); s.hi = sx32(p >> 32); return
        if m == 'multu':
            p = (g(reg(ops[0])) & M32) * (g(reg(ops[1])) & M32)
            s.lo = sx32(p); s.hi = sx32(p >> 32); return
        if m in ('div', 'divu', 'mult', 'multu') and len(ops) == 3:
            ops = ops[1:]
        if m == 'div':
            a = sx32(g(reg(ops[0]))); b = sx32(g(reg(ops[1])))
            if b:
                q = abs(a) // abs(b)
                q = q if (a < 0) == (b < 0) else -q
                s.lo = sx32(q); s.hi = sx32(a - q * b)
            return
        if m == 'divu':
            a = g(reg(ops[0])) & M32; b = g(reg(ops[1])) & M32
            if b:
                s.lo = sx32(a // b); s.hi = sx32(a % b)
            return
        if m == 'mfhi':
            setr(reg(ops[0]), s.hi); return
        if m == 'mflo':
            setr(reg(ops[0]), s.lo); return
        if m == 'mthi':
            s.hi = g(reg(ops[0])); return
        if m == 'mtlo':
            s.lo = g(reg(ops[0])); return
        a = None
        if m in ('lw', 'lh', 'lhu', 'lb', 'lbu', 'sw', 'sh', 'sb', 'lwc1', 'swc1', 'ldc1', 'sdc1', 'ld', 'sd', 'lwl', 'lwr', 'swl', 'swr', 'lwu'):
            mo = mem_re.match(ops[1])
            base = g(reg(mo.group(2)))
            a = (sx32(base) + (int(mo.group(1), 0) if mo.group(1) not in ('', '-') else 0)) & M32
        if m == 'lw': setr(reg(ops[0]), sx32(s.rd32(a))); return
        if m == 'lwu': setr(reg(ops[0]), s.rd32(a)); return
        if m == 'lh':
            v = s.rd16(a); setr(reg(ops[0]), v - 65536 if v & 0x8000 else v); return
        if m == 'lhu': setr(reg(ops[0]), s.rd16(a)); return
        if m == 'lb':
            v = s.rd8(a); setr(reg(ops[0]), v - 256 if v & 0x80 else v); return
        if m == 'lbu': setr(reg(ops[0]), s.rd8(a)); return
        if m == 'ld': setr(reg(ops[0]), s.rd64(a)); return
        if m == 'sw': s.wr32(a, g(reg(ops[0]))); return
        if m == 'sh': s.wr16(a, g(reg(ops[0]))); return
        if m == 'sb': s.wr8(a, g(reg(ops[0]))); return
        if m == 'sd': s.wr64(a, g(reg(ops[0]))); return
        if m == 'lwl':
            sh = a & 3; w = s.rd32(a & ~3); old = g(reg(ops[0])) & M32
            nv = ((w << (8 * sh)) & M32) | (old & ((1 << (8 * sh)) - 1)); setr(reg(ops[0]), sx32(nv)); return
        if m == 'lwr':
            sh = 3 - (a & 3); w = s.rd32(a & ~3); old = g(reg(ops[0])) & M32
            nv = (w >> (8 * sh)) | (old & (M32 ^ (M32 >> (8 * sh)))); setr(reg(ops[0]), sx32(nv)); return
        if m == 'swl':
            sh = a & 3; w = s.rd32(a & ~3); v = g(reg(ops[0])) & M32
            mask = M32 >> (8 * sh); s.wr32(a & ~3, (w & (M32 ^ mask)) | (v >> (8 * sh))); return
        if m == 'swr':
            sh = 3 - (a & 3); w = s.rd32(a & ~3); v = g(reg(ops[0])) & M32
            mask = (M32 << (8 * sh)) & M32; s.wr32(a & ~3, (w & (M32 ^ mask)) | ((v << (8 * sh)) & M32)); return
        if m == 'lwc1': s.f[reg(ops[0])] = s.rd32(a); return
        if m == 'swc1': s.wr32(a, s.f[reg(ops[0])]); return
        if m == 'ldc1':
            n = reg(ops[0]); v = s.rd64(a); s.f[n] = v & M32; s.f[n + 1] = (v >> 32) & M32; return
        if m == 'sdc1':
            n = reg(ops[0]); s.wr64(a, (s.f[n + 1] << 32) | s.f[n]); return

        def tgt(x): return int(x, 0) & M32
        if m == 'beq': return ('n', tgt(ops[2]), g(reg(ops[0])) == g(reg(ops[1])))
        if m == 'bne': return ('n', tgt(ops[2]), g(reg(ops[0])) != g(reg(ops[1])))
        if m == 'beql': return ('likely', tgt(ops[2]), g(reg(ops[0])) == g(reg(ops[1])))
        if m == 'bnel': return ('likely', tgt(ops[2]), g(reg(ops[0])) != g(reg(ops[1])))
        if m == 'beqz': return ('n', tgt(ops[1]), g(reg(ops[0])) == 0)
        if m == 'bnez': return ('n', tgt(ops[1]), g(reg(ops[0])) != 0)
        zz = {'blez': lambda v: v <= 0, 'bgtz': lambda v: v > 0, 'bltz': lambda v: v < 0, 'bgez': lambda v: v >= 0}
        if m in zz: return ('n', tgt(ops[1]), zz[m](sr(reg(ops[0]))))
        zl = {'blezl': lambda v: v <= 0, 'bgtzl': lambda v: v > 0, 'bltzl': lambda v: v < 0, 'bgezl': lambda v: v >= 0}
        if m in zl: return ('likely', tgt(ops[1]), zl[m](sr(reg(ops[0]))))
        if m == 'b': return ('n', tgt(ops[0]), True)
        if m == 'bal':
            setr(31, pc + 8); return ('n', tgt(ops[0]), True)
        if m == 'bgezal':
            c = sr(reg(ops[0])) >= 0; setr(31, pc + 8); return ('n', tgt(ops[1]), c)
        if m == 'j': return ('n', tgt(ops[0]), True)
        if m == 'jal':
            setr(31, pc + 8); return ('n', tgt(ops[0]), True)
        if m == 'jr': return ('n', g(reg(ops[0])) & M32, True)
        if m == 'jalr':
            if len(ops) == 1:
                rd = 31; rs = reg(ops[0])
            else:
                rd = reg(ops[0]); rs = reg(ops[1])
            t = g(rs) & M32; setr(rd, pc + 8); return ('n', t, True)
        if m == 'bc1t': return ('n', tgt(ops[0]), s.cc)
        if m == 'bc1f': return ('n', tgt(ops[0]), not s.cc)
        if m == 'bc1tl': return ('likely', tgt(ops[0]), s.cc)
        if m == 'bc1fl': return ('likely', tgt(ops[0]), not s.cc)
        if m == 'mfc1': setr(reg(ops[0]), sx32(s.f[reg(ops[1])])); return
        if m == 'mtc1': s.f[reg(ops[1])] = g(reg(ops[0])) & M32; return
        if m == 'cfc1': setr(reg(ops[0]), 0); return
        if m == 'ctc1': return
        if m == 'mov.s': s.f[reg(ops[0])] = s.f[reg(ops[1])]; return
        if m == 'mov.d':
            x = reg(ops[0]); y = reg(ops[1]); s.f[x] = s.f[y]; s.f[x + 1] = s.f[y + 1]; return
        with np.errstate(all='ignore'):
            sd = m.split('.')
            if len(sd) == 2 and sd[1] in ('s', 'd') and sd[0] in ('add', 'sub', 'mul', 'div'):
                x0 = reg(ops[0]); x1 = reg(ops[1]); x2 = reg(ops[2])
                if sd[1] == 's':
                    x = s.fs(x1); y = s.fs(x2)
                    r = {'add': x + y, 'sub': x - y, 'mul': x * y, 'div': x / y}[sd[0]]
                    s.setfs(x0, np.float32(r))
                else:
                    x = s.fd(x1); y = s.fd(x2)
                    r = {'add': x + y, 'sub': x - y, 'mul': x * y, 'div': x / y}[sd[0]]
                    s.setfd(x0, r)
                return
            if m == 'sqrt.s': s.setfs(reg(ops[0]), np.sqrt(s.fs(reg(ops[1])))); return
            if m == 'neg.s': s.setfs(reg(ops[0]), -s.fs(reg(ops[1]))); return
            if m == 'neg.d': s.setfd(reg(ops[0]), -s.fd(reg(ops[1]))); return
            if m == 'abs.s': s.setfs(reg(ops[0]), abs(s.fs(reg(ops[1])))); return
            if m == 'cvt.s.w': s.setfs(reg(ops[0]), np.float32(sx32(s.f[reg(ops[1])]))); return
            if m == 'cvt.d.w': s.setfd(reg(ops[0]), np.float64(sx32(s.f[reg(ops[1])]))); return
            if m == 'cvt.d.s': s.setfd(reg(ops[0]), np.float64(s.fs(reg(ops[1])))); return
            if m == 'cvt.s.d': s.setfs(reg(ops[0]), np.float32(s.fd(reg(ops[1])))); return

            def toint(x, lim=2 ** 31):
                if x != x: return 0x7fffffff
                v = math.trunc(x) if abs(x) < 1e300 else 0
                if v >= lim: return 0x7fffffff
                if v < -lim: return 0x80000000
                return v & M32
            if m == 'trunc.w.s': s.f[reg(ops[0])] = toint(float(s.fs(reg(ops[1])))); return
            if m == 'trunc.w.d': s.f[reg(ops[0])] = toint(float(s.fd(reg(ops[1])))); return
            if m in ('cvt.w.s', 'round.w.s'):
                s.f[reg(ops[0])] = toint(round(float(s.fs(reg(ops[1]))))); return
            if m in ('cvt.w.d', 'round.w.d'):
                s.f[reg(ops[0])] = toint(round(float(s.fd(reg(ops[1]))))); return
            if m == 'floor.w.s': s.f[reg(ops[0])] = toint(math.floor(float(s.fs(reg(ops[1]))))); return
            if m == 'ceil.w.s': s.f[reg(ops[0])] = toint(math.ceil(float(s.fs(reg(ops[1]))))); return
            if m.startswith('c.'):
                _, cond, fmt = m.split('.')
                x = s.fs(reg(ops[0])) if fmt == 's' else s.fd(reg(ops[0]))
                y = s.fs(reg(ops[1])) if fmt == 's' else s.fd(reg(ops[1]))
                un = (x != x) or (y != y)
                res = {'eq': x == y, 'lt': x < y, 'le': x <= y, 'ult': un or x < y, 'ule': un or x <= y, 'ueq': un or x == y,
                       'olt': x < y, 'ole': x <= y, 'f': False, 'un': un, 'seq': x == y}.get(cond)
                if res is None: raise Halt('cond ' + cond)
                s.cc = bool(res); return
        raise Halt('unimpl %s %s at %08x' % (m, ops, pc))
