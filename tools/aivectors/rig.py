"""Where the interpreter gets its inputs: 1.0's code segment from the ROM, and the combat checkpoints' RAM.

Nothing derived from the ROM is kept in the repository. The disassembly is built here at run time (and cached in the gitignored
out/aivectors/), and a checkpoint's RAM is read straight from its save state with tools/bst.py, as `bst.py --dump` does.

The ROM and the states are found as the reference rig keeps them: in roms/ (or AIDYN_ROMS), the ROM by its name and a state as
"<rom basename> - Combat NN, <label>.bs1".
"""
import glob
import hashlib
import os
import pickle
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(REPO, 'out', 'aivectors')
sys.path.insert(0, os.path.join(REPO, 'tools'))
import bst  # noqa: E402  (tools/bst.py: RDRAM out of an ares state)

BASE = 0x80000400        # where the code segment loads (ROM 0x1000)
TEXT_END = 0x800CCEC0    # 1.0's text ends here, with data after it (reference-rig.md, Ghidra)
CODE_ROM = 0x1000

_rom_cache = {}
_ram_cache = {}


def roms_dir():
    return os.environ.get('AIDYN_ROMS') or os.path.join(REPO, 'roms')


def rom_path():
    """1.0's ROM (rev byte 0x00), by name."""
    hits = glob.glob(os.path.join(glob.escape(roms_dir()), '*(V1.0)*.z64'))
    if not hits:
        raise SystemExit(f'1.0 ROM (*(V1.0)*.z64) not found in {roms_dir()}; set AIDYN_ROMS')
    return hits[0]


def checkpoint_path(n):
    """Combat checkpoint n (1..14): '<rom basename> - Combat NN, <label>.bs1' beside the ROM."""
    stem = os.path.splitext(rom_path())[0]
    hits = glob.glob(glob.escape(stem) + ' - Combat %02d,*.bs1' % n)
    if not hits:
        raise SystemExit(f'checkpoint Combat {n:02d} not found beside {rom_path()}')
    return hits[0]


def _rom():
    if 'rom' not in _rom_cache:
        _rom_cache['rom'] = open(rom_path(), 'rb').read()
    return _rom_cache['rom']


def code():
    """The code segment disassembled, one (address, mnemonic, operands) per word up to the end of the text. Cached in out/aivectors/."""
    from capstone import Cs, CS_ARCH_MIPS, CS_MODE_BIG_ENDIAN, CS_MODE_MIPS64
    seg = _rom()[CODE_ROM:CODE_ROM + (TEXT_END - BASE)]
    key = hashlib.sha1(seg).hexdigest()
    cache = os.path.join(OUT, 'insns.pkl')
    if os.path.exists(cache):
        try:
            k, insns = pickle.load(open(cache, 'rb'))
            if k == key:
                return insns
        except Exception:
            pass
    md = Cs(CS_ARCH_MIPS, CS_MODE_BIG_ENDIAN | CS_MODE_MIPS64)
    insns = []
    for off in range(0, len(seg), 4):
        w = seg[off:off + 4]
        d = list(md.disasm(w, BASE + off))
        if d:
            insns.append((BASE + off, d[0].mnemonic, d[0].op_str))
        else:
            insns.append((BASE + off, '.word', '%08x' % int.from_bytes(w, 'big')))
    os.makedirs(OUT, exist_ok=True)
    tmp = cache + '.%d.tmp' % os.getpid()
    pickle.dump((key, insns), open(tmp, 'wb'))
    os.replace(tmp, cache)
    return insns


def ram(n):
    """Checkpoint n's RDRAM, 8 MB big-endian (what `bst.py <state> --dump` writes). Read from the state once, into out/aivectors/ram/, so
    that the workers don't each unpack a state (hundreds of MB of working set a time)."""
    if n not in _ram_cache:
        state = checkpoint_path(n)
        cache = os.path.join(OUT, 'ram', 'cp%02d.bin' % n)
        if not (os.path.exists(cache) and os.path.getmtime(cache) >= os.path.getmtime(state)):
            os.makedirs(os.path.dirname(cache), exist_ok=True)
            tmp = cache + '.%d.tmp' % os.getpid()
            open(tmp, 'wb').write(bst.load_rdram(state, rom_path()))
            os.replace(tmp, cache)
        _ram_cache[n] = open(cache, 'rb').read()
    return _ram_cache[n]


def fresh(n=5):
    """A CPU on a copy of checkpoint n's RAM."""
    from cpu import CPU
    return CPU(bytearray(ram(n)))
