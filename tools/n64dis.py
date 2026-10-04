"""Disassemble the original's code, or read its constants, by address or by the project's symbol map.

Usage:
  python tools/n64dis.py <symbol|0xaddress> [--rom us|debug] [--count N]
      Disassembles a function, from its address to the next symbol's (or N instructions),
      naming the functions it calls and the floats and doubles it loads with lui/lwc1/ldc1.
  python tools/n64dis.py --data <0xaddress> [--rom us|debug] [--count N]
      Reads N words at an address as hex, float and (in pairs) double.
  python tools/n64dis.py --find <text> [--rom us|debug]
      Lists the symbols whose names contain the text.

The code segment is at ROM 0x1000, loaded at 0x80000400, in both 1.0 ("us", the default) and the
debug ROM. Symbols come from the project's map, tools/symbols/<rom>.txt (lines of
"name = 0xaddress;"), and without one everything works by address. 1.0's functions sit in the
same order as the debug build's, so find one in the other by its neighbours. ROMs are found in roms/ (or AIDYN_ROMS) by name. Needs capstone (pip install capstone).
"""
import argparse
import glob
import os
import re
import struct
import sys

from capstone import CS_ARCH_MIPS, CS_MODE_BIG_ENDIAN, CS_MODE_MIPS64, Cs

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
ROMS = os.environ.get("AIDYN_ROMS") or os.path.join(REPO, "roms")
ROM_NAMES = {"us": "*(V1.0)*.z64", "debug": "AidynDebug.z64"}
LOAD, START = 0x80000400, 0x1000


def load(which):
    matches = glob.glob(os.path.join(ROMS, ROM_NAMES[which]))
    if not matches:
        sys.exit(f"No {which} ROM in {ROMS}")
    rom = open(matches[0], "rb").read()
    symbols = {}
    path = os.path.join(HERE, "symbols", f"{which}.txt")
    for line in open(path, encoding="utf-8") if os.path.exists(path) else []:
        m = re.match(r"\s*([\w:~]+)\s*=\s*0x([0-9a-fA-F]+);", line)
        if m:
            symbols.setdefault(int(m.group(2), 16), m.group(1))
    return rom, symbols


def offset(address):
    return address - LOAD + START


def word(rom, address):
    return rom[offset(address):offset(address) + 4]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("target", nargs="?")
    p.add_argument("--rom", choices=ROM_NAMES, default="us")
    p.add_argument("--count", type=int)
    p.add_argument("--data")
    p.add_argument("--find")
    a = p.parse_args()
    rom, symbols = load(a.rom)
    by_name = {v: k for k, v in symbols.items()}

    if a.find:
        for address, name in sorted(symbols.items()):
            if a.find.lower() in name.lower():
                print(f"{address:08x} {name}")
        return
    if a.data:
        address = int(a.data, 16)
        for i in range(a.count or 4):
            raw = word(rom, address + 4 * i)
            text = f"{address + 4 * i:08x} {raw.hex()} f32={struct.unpack('>f', raw)[0]!r}"
            if i % 2 == 0:
                text += f" f64={struct.unpack('>d', rom[offset(address + 4 * i):offset(address + 4 * i) + 8])[0]!r}"
            print(text)
        return

    target = a.target or sys.exit("Give a symbol or an address.")
    start = int(target, 16) if target.lower().startswith("0x") else by_name.get(target)
    if start is None:
        sys.exit(f"No symbol {target}; try --find.")
    later = [s for s in symbols if s > start and s < 0x80100000]
    end = start + 4 * a.count if a.count else (min(later) if later else start + 0x400)
    print(f"; {symbols.get(start, hex(start))} ({a.rom}), {(end - start) // 4} instructions")
    md = Cs(CS_ARCH_MIPS, CS_MODE_MIPS64 + CS_MODE_BIG_ENDIAN)
    upper = {}
    for i in md.disasm(rom[offset(start):offset(end)], start):
        note = ""
        ops = [o.strip() for o in i.op_str.split(",")]
        if i.mnemonic == "lui":
            upper[ops[0]] = int(ops[1], 0) << 16
            if upper[ops[0]] and upper[ops[0]] < 0x80000000 and abs(struct.unpack(">f", upper[ops[0]].to_bytes(4, "big"))[0]) < 1e7:
                note = f"as a float's top: {struct.unpack('>f', upper[ops[0]].to_bytes(4, 'big'))[0]!r}"
        elif i.mnemonic == "ori" and ops[1] in upper:
            value = (upper[ops[1]] | int(ops[2], 0)) & 0xFFFFFFFF
            note = f"0x{value:08x}, as a float {struct.unpack('>f', value.to_bytes(4, 'big'))[0]!r}"
        elif i.mnemonic in ("jal", "j") and ops[0].startswith("0x"):
            note = symbols.get(int(ops[0], 16), "")
        elif i.mnemonic in ("lwc1", "ldc1", "lw", "addiu") and len(ops) >= 2:
            m = re.match(r"(-?0x[0-9a-f]+|-?\d+)?\((\$\w+)\)", ops[-1]) if i.mnemonic != "addiu" else None
            base, disp = (m.group(2), int(m.group(1) or "0", 0)) if m else (None, None)
            if base in upper and i.mnemonic in ("lwc1", "ldc1"):
                address = (upper[base] + disp) & 0xFFFFFFFF
                if LOAD <= address < LOAD + len(rom) - START:
                    raw = rom[offset(address):offset(address) + 8]
                    value = struct.unpack(">f", raw[:4])[0] if i.mnemonic == "lwc1" else struct.unpack(">d", raw)[0]
                    note = f"[{address:08x}] = {value!r}"
        print(f"{i.address:08x} {i.mnemonic:8} {i.op_str}" + (f"   ; {note}" if note else ""))


if __name__ == "__main__":
    main()
