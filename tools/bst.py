"""Read N64 RDRAM out of an ares save state (.bs1-.bs9) without running the emulator.

ares stores RDRAM uncompressed as 32-bit little-endian words. It's located by
fingerprinting the game's code segment (ROM 0x1000 -> RAM 0x80000400), so the
matching ROM is needed. By default it's found from the state's filename prefix
("<rom basename> - <label>.bsN" or "<rom basename>.bsN").

Usage:
  bst.py STATE ADDR [LEN]      hexdump LEN bytes (default 64) at a KSEG0 address
  bst.py STATE --word ADDR     print one big-endian u32
  bst.py STATE --dump OUT      write all of RDRAM (8 MB, big-endian) to OUT
  Add --rom PATH to override ROM detection.
"""
import argparse
import os
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


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("state")
    ap.add_argument("addr", nargs="?")
    ap.add_argument("length", nargs="?", default="64")
    ap.add_argument("--word")
    ap.add_argument("--dump")
    ap.add_argument("--rom")
    a = ap.parse_args()

    ram = load_rdram(a.state, a.rom or find_rom(a.state))
    phys = lambda s: int(s, 0) & 0x7FFFFF

    if a.dump:
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
