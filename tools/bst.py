"""Read N64 RDRAM out of an ares save state (.bs1-.bs9) without running the emulator.

ares stores RDRAM uncompressed as 32-bit little-endian words. It's located by
fingerprinting the game's code segment (ROM 0x1000 -> RAM 0x80000400), so the
matching ROM is needed. By default it's found from the state's filename prefix
("<rom basename> - <label>.bsN" or "<rom basename>.bsN").

Usage:
  bst.py STATE ADDR [LEN]      hexdump LEN bytes (default 64) at a KSEG0 address
  bst.py STATE --word ADDR     print one big-endian u32
  bst.py STATE --dump OUT      write all of RDRAM (8 MB, big-endian) to OUT
  bst.py STATE --diff OTHER [--range A..B] [--width 1|2|4] [--was X] [--now Y] [--max N]
                               what changed between two states of one ROM (roadmap 0.10): runs of
                               changed words, or with --was/--now, the values of that width that went
                               from X to Y. Static addresses are named by the nearest symbol in
                               tools/symbols. Find where a value lives by changing it in the game.
  Add --rom PATH to override ROM detection.
"""
import argparse
import bisect
import os
import re
import struct
import sys

RDRAM_SIZE = 0x800000
CODE_ROM, CODE_RAM = 0x1000, 0x400
FP_OFFSET, FP_LEN = 0x1000, 32  # fingerprint: 32 bytes, 4 KB into the code segment


def swap32(b):
    return b"".join(b[i:i + 4][::-1] for i in range(0, len(b), 4))


def find_rom(state_path):
    # ROM names can contain " - " themselves, so match on prefix; longest wins.
    folder, name = os.path.split(os.path.abspath(state_path))
    stem = os.path.splitext(name)[0]
    roms = [f for f in os.listdir(folder) if os.path.splitext(f)[1].lower() in (".z64", ".n64", ".v64")]
    hits = [f for f in roms if stem == os.path.splitext(f)[0] or stem.startswith(os.path.splitext(f)[0] + " - ")]
    if not hits:
        sys.exit(f"can't find ROM for {name}; pass --rom")
    return os.path.join(folder, max(hits, key=len))


def load_rdram(state_path, rom_path):
    state = open(state_path, "rb").read()
    if state[:4] != b"BST1":
        sys.exit(f"{state_path}: not an ares state (magic {state[:4]!r})")
    rom = open(rom_path, "rb").read()
    fp = swap32(rom[CODE_ROM + FP_OFFSET:CODE_ROM + FP_OFFSET + FP_LEN])
    i = state.find(fp)
    if i < 0:
        sys.exit("RDRAM fingerprint not found; wrong ROM for this state?")
    base = i - (CODE_RAM + FP_OFFSET)
    return swap32(state[base:base + RDRAM_SIZE])


def symbols_for(rom_path):
    """The symbol map for the state's ROM, as sorted (address, name)."""
    which = "debug" if "debug" in os.path.basename(rom_path).lower() else "us"
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "symbols", f"{which}.txt")
    found = []
    for line in open(path, encoding="utf-8") if os.path.exists(path) else []:
        m = re.match(r"\s*([\w:.~]+)\s*=\s*0x([0-9a-fA-F]+);", line)
        if m:
            found.append((int(m.group(2), 16), m.group(1)))
    return sorted(found)


def label(symbols, address, segment_end=0x80100000):
    """The nearest symbol at or before a static address, within 4 KB."""
    if address >= segment_end:
        return ""
    k = bisect.bisect_right([s[0] for s in symbols], address) - 1
    if k < 0 or address - symbols[k][0] >= 0x1000:
        return ""
    at, name = symbols[k]
    return f"  {name}" + (f"+0x{address - at:x}" if address != at else "")


def diff(a, b, args, symbols):
    low, _, high = (args.range or "0x80000000..0x80800000").partition("..")
    low, high = int(low, 0) & 0x7FFFFF, (int(high, 0) - 1 & 0x7FFFFF) + 1
    width = args.width
    fmt = {1: ">B", 2: ">H", 4: ">I"}[width]
    shown = 0
    if args.was is not None or args.now is not None:
        for off in range(low - low % width, high, width):
            x, y = struct.unpack(fmt, a[off:off + width])[0], struct.unpack(fmt, b[off:off + width])[0]
            if x != y and (args.was is None or x == int(args.was, 0)) and (args.now is None or y == int(args.now, 0)):
                print(f"{0x80000000 + off:08x}: {x:#x} -> {y:#x}{label(symbols, 0x80000000 + off)}")
                shown += 1
                if shown >= args.max:
                    print(f"... stopped at --max {args.max}")
                    return
        return
    off = low - low % 4
    runs = 0
    while off < high:
        if a[off:off + 4] == b[off:off + 4]:
            off += 4
            continue
        start = off
        while off < high and a[off:off + 4] != b[off:off + 4]:
            off += 4
        runs += 1
        if shown < args.max:
            print(f"{0x80000000 + start:08x} +{off - start:#x}{label(symbols, 0x80000000 + start)}")
            for w in range(start, min(off, start + 32), 4):
                print(f"    {0x80000000 + w:08x}: {a[w:w + 4].hex()} -> {b[w:w + 4].hex()}")
            if off - start > 32:
                print("    ...")
            shown += 1
    print(f"{runs} changed runs" + (f", first {args.max} shown" if runs > args.max else ""))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("state")
    ap.add_argument("addr", nargs="?")
    ap.add_argument("length", nargs="?", default="64")
    ap.add_argument("--word")
    ap.add_argument("--dump")
    ap.add_argument("--rom")
    ap.add_argument("--diff")
    ap.add_argument("--range")
    ap.add_argument("--width", type=int, choices=(1, 2, 4), default=4)
    ap.add_argument("--was")
    ap.add_argument("--now")
    ap.add_argument("--max", type=int, default=200)
    a = ap.parse_args()

    rom_path = a.rom or find_rom(a.state)
    ram = load_rdram(a.state, rom_path)
    phys = lambda s: int(s, 0) & 0x7FFFFF

    if a.diff:
        diff(ram, load_rdram(a.diff, rom_path), a, symbols_for(rom_path))
    elif a.dump:
        open(a.dump, "wb").write(ram)
    elif a.word:
        print(f"{struct.unpack('>I', ram[phys(a.word):phys(a.word) + 4])[0]:#010x}")
    elif a.addr:
        start, n = phys(a.addr), int(a.length, 0)
        for off in range(0, n, 16):
            row = ram[start + off:start + min(off + 16, n)]
            print(f"{0x80000000 + start + off:08x}: {row.hex(' ', 4)}")
    else:
        ap.print_help()


if __name__ == "__main__":
    main()
