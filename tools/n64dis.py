"""Disassemble the original's code, or read its constants, by address or by the project's symbol map.

Usage:
  python tools/n64dis.py <symbol|0xaddress> [--rom us|debug] [--count N]
      Disassembles a function, from its address to the next symbol's (or N instructions),
      naming the functions it calls and the floats and doubles it loads with lui/lwc1/ldc1.
  python tools/n64dis.py --data <0xaddress> [--rom us|debug] [--count N]
      Reads N words at an address as hex, float and (in pairs) double.
  python tools/n64dis.py --find <text> [--rom us|debug]
      Lists the symbols whose names contain the text.

From the code index (roadmap 0.7), which tools/ghidra.ps1 import writes to out/ghidra:
  --callers <fn>          the functions that call it, and the data words that point at it
  --callees <fn>          the functions it calls
  --refs <addr>[..<end>]  the functions that read (r), write (w) or take (a) an address or range,
                          and the data words that point into it
  --strings <text>        the strings in the code segment holding the text, and who uses them
  --c <fn>                Ghidra's pseudocode for the function holding the address
  --other <fn>            the same function in the other build
  --file <text>           the functions placed in the original's source files whose names hold the text

Names carry between the builds, and source files come from the debug build, by tools/builds.py
(roadmap 0.9), which tools/ghidra.ps1 export runs.
A function is a symbol or any address inside it.

The code segment is at ROM 0x1000, loaded at 0x80000400, in both 1.0 ("us", the default) and the
debug ROM. Symbols come from the project's map, tools/symbols/<rom>.txt (lines of
"name = 0xaddress;"), and without one everything works by address. 1.0's functions sit in the
same order as the debug build's, so find one in the other by its neighbours. ROMs are found in roms/ (or HBHT_ROMS) by name. Needs capstone (pip install capstone).
"""
import argparse
import bisect
import json
import glob
import os
import re
import struct
import sys

from capstone import CS_ARCH_MIPS, CS_MODE_BIG_ENDIAN, CS_MODE_MIPS64, Cs

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
ROMS = os.environ.get("HBHT_ROMS") or os.path.join(REPO, "roms")
ROM_NAMES = {"us": "*(V1.0)*.z64", "debug": "AidynDebug.z64"}
LOAD, START = 0x80000400, 0x1000


def read_map(which):
    symbols = {}
    path = os.path.join(HERE, "symbols", f"{which}.txt")
    for line in open(path, encoding="utf-8") if os.path.exists(path) else []:
        m = re.match(r"\s*([\w:.~]+)\s*=\s*0x([0-9a-fA-F]+);", line)
        if m:
            symbols.setdefault(int(m.group(2), 16), m.group(1))
    return symbols


def load_builds():
    """The match between the builds and the source files, from tools/builds.py, or None."""
    path = os.path.join(REPO, "out", "ghidra", "builds.json")
    if not os.path.exists(path):
        return None
    data = json.load(open(path, encoding="utf-8"))
    us_to_debug = {int(k): v for k, v in data["us_to_debug"].items()}
    return {"us": us_to_debug, "debug": {v: k for k, v in us_to_debug.items()},
            "files": {int(k): v for k, v in data["files"].items()}, "units": data.get("units", [])}


def load(which):
    """The ROM and its symbols: its own map, with the other build's names carried across the match."""
    matches = glob.glob(os.path.join(ROMS, ROM_NAMES[which]))
    if not matches:
        sys.exit(f"No {which} ROM in {ROMS}")
    rom = open(matches[0], "rb").read()
    symbols = read_map(which)
    builds = load_builds()
    if builds:
        other = "debug" if which == "us" else "us"
        across = builds[other]
        for address, name in read_map(other).items():
            if address in across:
                symbols.setdefault(across[address], name)
    return rom, symbols


def source_file(which, address, builds):
    """The original's source file for a function's entry, by the debug build's placement."""
    if not builds:
        return None
    if which == "us":
        address = builds["us"].get(address)
    placed = builds["files"].get(address)
    if placed:
        return placed["file"] + (f" (lines {placed['lines'][0]}-{placed['lines'][-1]})" if placed.get("lines") else "")
    unit = next((u for u in builds["units"] if address is not None and u["at"] <= address < u["end"]), None)
    if not unit:
        return None
    where = f"{unit['at']:08x}-{unit['end']:08x} in debug"
    if unit["files"]:
        return f"one of {', '.join(unit['files'])} ({where})"
    return f"an unnamed file ({where})"


def load_index(which):
    path = os.path.join(REPO, "out", "ghidra", f"{which}.index.json")
    if not os.path.exists(path):
        sys.exit(f"No code index at {path}; run tools/ghidra.ps1 import {which}")
    index = json.load(open(path, encoding="utf-8"))
    index["starts"] = [f["at"] for f in index["functions"]]
    return index


def containing(index, address):
    i = bisect.bisect_right(index["starts"], address) - 1
    f = index["functions"][i] if i >= 0 else None
    return f if f and f["at"] <= address < f["end"] else None


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
    for name in ("callers", "callees", "refs", "strings", "c", "other", "file"):
        p.add_argument(f"--{name}")
    a = p.parse_args()
    rom, symbols = load(a.rom)
    by_name = {v: k for k, v in symbols.items()}

    def address_of(text):
        value = int(text, 16) if text.lower().startswith("0x") else by_name.get(text)
        if value is None:
            sys.exit(f"No symbol {text}; try --find.")
        return value

    def name(address):
        return f"{address:08x} {symbols[address]}" if address in symbols else f"{address:08x}"

    def function(index, text):
        f = containing(index, address_of(text))
        return f or sys.exit(f"No function holds {text}.")

    def where(index, address):
        f = containing(index, address)
        if not f:
            return name(address)
        return name(f["at"]) + (f" +0x{address - f['at']:x}" if address != f["at"] else "")

    if a.other:
        builds = load_builds() or sys.exit("No out/ghidra/builds.json; run tools/ghidra.ps1 export")
        f = function(load_index(a.rom), a.other)
        other = "debug" if a.rom == "us" else "us"
        there = builds[a.rom].get(f["at"])
        print(f"{name(f['at'])} ({a.rom}) is " + (f"{there:08x} in {other}" if there else f"unmatched in {other}"))
        return
    if a.file:
        builds = load_builds() or sys.exit("No out/ghidra/builds.json; run tools/ghidra.ps1 export")
        for at, placed in sorted(builds["files"].items()):
            if a.file.lower() in placed["file"].lower():
                here = at if a.rom == "debug" else builds["debug"].get(at)
                lines = f" lines {placed['lines']}" if placed.get("lines") else ""
                print(f"{placed['file']:36} {name(here) if here else '(not in 1.0)':40} {placed['how']}{lines}")
        return
    if a.callers or a.callees:
        index = load_index(a.rom)
        f = function(index, a.callers or a.callees)
        print(f"; {name(f['at'])}-{f['end']:08x}" + (", unreferenced" if f.get("unreferenced") else ""))
        if a.callees:
            for c in f["calls"]:
                print(where(index, c))
            return
        for g in index["functions"]:
            if f["at"] in g["calls"]:
                print(name(g["at"]))
        for at, to in index["pointers"]:
            if to == f["at"]:
                print(f"pointed at by data {at:08x}")
        return
    if a.refs:
        index = load_index(a.rom)
        low, _, high = a.refs.partition("..")
        low = address_of(low)
        high = address_of(high) if high else low + 1
        for g in index["functions"]:
            hits = [(at, kind) for at, kind in g["refs"] if low <= at < high]
            if hits:
                print(f"{name(g['at']):40} " + " ".join(f"{at:08x}:{kind}" for at, kind in hits))
        for at, to in index["pointers"]:
            if low <= to < high:
                print(f"pointed at by data {at:08x} -> {to:08x}")
        return
    if a.strings:
        index = load_index(a.rom)
        users = {}
        for g in index["functions"]:
            for at, _ in g["refs"]:
                users.setdefault(at, []).append(g["at"])
        for key, text in index["strings"].items():
            if a.strings.lower() not in text.lower():
                continue
            at = int(key)
            print(f"{at:08x} {text!r}")
            for u in users.get(at, []):
                print(f"    used by {name(u)}")
            for ptr, to in index["pointers"]:
                if to == at:
                    print(f"    pointed at by data {ptr:08x}" + "".join(f", read by {name(u)}" for u in users.get(ptr, [])))
        return
    if a.c:
        index = load_index(a.rom)
        f = function(index, a.c)
        path = os.path.join(REPO, "out", "ghidra", f"{a.rom}.c")
        printing = False
        for line in open(path, encoding="utf-8"):
            if line.startswith("//@ "):
                printing = int(line.split()[1], 16) == f["at"]
            if printing:
                print(line, end="")
        return

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
    start = address_of(target)
    index_path = os.path.join(REPO, "out", "ghidra", f"{a.rom}.index.json")
    f = containing(load_index(a.rom), start) if os.path.exists(index_path) else None
    later = [s for s in symbols if s > start and s < 0x80100000]
    end = start + 4 * a.count if a.count else f["end"] if f else (min(later) if later else start + 0x400)
    placed = source_file(a.rom, f["at"], load_builds()) if f else None
    print(f"; {symbols.get(start, hex(start))} ({a.rom}), {(end - start) // 4} instructions" + (f", {placed}" if placed else ""))
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
