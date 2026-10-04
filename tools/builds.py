"""Match 1.0's functions to the debug build's, and place each in one of the original's source files
(roadmap 0.9). Reads the Ghidra export (tools/ghidra.ps1) and writes out/ghidra/builds.json, which
tools/n64dis.py reads, and out/ghidra/<rom>.symbols.txt, each build's symbol map with the other's
names carried across, which tools/ghidra/ApplySymbols.java applies.

Usage: python tools/builds.py        (tools/ghidra.ps1 export runs it)

Matching. Both builds come from the same source with the same link order; the debug build adds
asserts and debug code. A function's shape is its opcodes without registers or immediates. Shapes
unique in both builds anchor the match, between anchors an alignment pairs the rest by shape
similarity, and then matched callers vote on how their callees pair, overriding the alignment where
two neighbours look alike.

Source files. The debug build passes a file name and line to its allocator and its asserts, so a
function citing a file is in it, and its lines order it there. A run of functions between two in the
same file is in that file too, since a file's functions are linked together. 1.0 takes the debug
build's files through the match.
"""
import bisect
import collections
import difflib
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from n64dis import REPO, load  # noqa: E402

OUT = os.path.join(REPO, "out", "ghidra")
SOURCE = re.compile(r"\.(cpp|c|h)$")


def shape(word):
    op = word >> 26
    if op == 0:
        return ("s", word & 0x3F)
    if op == 1:
        return ("r", (word >> 16) & 0x1F)
    if op in (0x10, 0x11):
        fmt = (word >> 21) & 0x1F
        return ("c", op, fmt, word & 0x3F if fmt >= 0x10 else 0)
    return op


def functions(which):
    rom, _ = load(which)
    index = json.load(open(os.path.join(OUT, f"{which}.index.json"), encoding="utf-8"))
    out = []
    for f in index["functions"]:
        o = f["at"] - 0x80000400 + 0x1000
        words = struct.unpack(f">{(f['end'] - f['at']) // 4}I", rom[o:o + f["end"] - f["at"]])
        out.append({"at": f["at"], "end": f["end"], "calls": f["calls"], "shape": tuple(shape(w) for w in words)})
    return out, index


def similarity(a, b):
    if a["shape"] == b["shape"]:
        return 1.0
    return difflib.SequenceMatcher(None, a["shape"], b["shape"], autojunk=False).ratio()


def longest_increasing(pairs):
    tails, links, ends = [], [None] * len(pairs), []
    for k, (_, j) in enumerate(pairs):
        p = bisect.bisect_left(tails, j)
        if p == len(tails):
            tails.append(j)
            ends.append(k)
        else:
            tails[p] = j
            ends[p] = k
        links[k] = ends[p - 1] if p else None
    chain, k = [], ends[-1] if ends else None
    while k is not None:
        chain.append(pairs[k])
        k = links[k]
    return chain[::-1]


def match(us, debug):
    def unique(fs):
        counts = collections.Counter(f["shape"] for f in fs)
        return {f["shape"]: i for i, f in enumerate(fs) if counts[f["shape"]] == 1 and len(f["shape"]) >= 6}

    ua, da = unique(us), unique(debug)
    chain = longest_increasing(sorted((ua[s], da[s]) for s in ua.keys() & da.keys()))
    pairs = dict(chain)

    # Between anchors: align in order, maximising similarity, pairing only functions at least half alike.
    bounds = [(-1, -1)] + chain + [(len(us), len(debug))]
    for (i0, j0), (i1, j1) in zip(bounds, bounds[1:]):
        a, b = list(range(i0 + 1, i1)), list(range(j0 + 1, j1))
        if not a or not b or len(a) * len(b) > 20000:
            continue
        sims = [[similarity(us[x], debug[y]) for y in b] for x in a]
        score = [[0.0] * (len(b) + 1) for _ in range(len(a) + 1)]
        for x in range(1, len(a) + 1):
            for y in range(1, len(b) + 1):
                s = sims[x - 1][y - 1]
                score[x][y] = max(score[x - 1][y], score[x][y - 1], score[x - 1][y - 1] + (s if s >= 0.5 else -1))
        x, y = len(a), len(b)
        while x and y:
            s = sims[x - 1][y - 1]
            if s >= 0.5 and abs(score[x][y] - (score[x - 1][y - 1] + s)) < 1e-9:
                pairs[a[x - 1]] = b[y - 1]
                x, y = x - 1, y - 1
            elif score[x][y] == score[x - 1][y]:
                x -= 1
            else:
                y -= 1

    # Callers vote on their callees.
    ui = {f["at"]: i for i, f in enumerate(us)}
    di = {f["at"]: j for j, f in enumerate(debug)}
    for _ in range(5):
        votes = collections.defaultdict(collections.Counter)
        for i, j in pairs.items():
            for c in us[i]["calls"]:
                if c in ui:
                    alike = [d for d in debug[j]["calls"] if d in di and similarity(us[ui[c]], debug[di[d]]) >= 0.5]
                    if len(alike) == 1:
                        votes[ui[c]][di[alike[0]]] += 1
        taken = {j: i for i, j in pairs.items()}
        changed = 0
        for i, counter in votes.items():
            (j, n), total = counter.most_common(1)[0], sum(counter.values())
            if n >= 2 and n > total / 2 and pairs.get(i) != j:
                other = taken.get(j)
                if other is not None:
                    if votes.get(other, {}).get(j, 0) >= n:
                        continue
                    del pairs[other]
                pairs[i] = j
                taken[j] = i
                changed += 1
        if not changed:
            break
    return {us[i]["at"]: debug[j]["at"] for i, j in pairs.items()}


def cited_lines(rom, f, files):
    """The line numbers a function passes beside a file name: a call whose a1 is the file's address
    (lui/addiu) and whose a2 is a constant (addiu or ori from zero), set in the eight instructions
    before the jal or in its delay slot."""
    o = f["at"] - 0x80000400 + 0x1000
    words = struct.unpack(f">{(f['end'] - f['at']) // 4}I", rom[o:o + f["end"] - f["at"]])
    lines = set()
    for k, w in enumerate(words):
        if w >> 26 != 3:
            continue
        hi, a1, a2 = {}, None, None
        for v in words[max(0, k - 8):k + 2]:
            op, rs, rt, imm = v >> 26, (v >> 21) & 31, (v >> 16) & 31, v & 0xFFFF
            if op == 0x0F:
                hi[rt] = imm << 16
            elif op == 0x09 and rt == 5 and rs in hi:
                a1 = (hi[rs] + (imm - 0x10000 if imm & 0x8000 else imm)) & 0xFFFFFFFF
            elif op in (0x09, 0x0D) and rt == 6 and rs == 0:
                a2 = imm
        if a1 in files and a2 is not None:
            lines.add(a2)
    return sorted(lines)


def source_files(debug, index):
    """Each debug function's source file and the lines it cites, from the file names it passes."""
    rom, _ = load("debug")
    paths = {os.path.basename(v).lower(): v for v in index["strings"].values() if SOURCE.search(v)}
    files = {}
    for k, v in index["strings"].items():
        # A file name, or a message naming one: "scene.cpp, SceneGetLocatorMtx()", "app.cpp::AppProc".
        m = re.match(r"^([\w./]+\.(?:cpp|c|h))(?:$|,|::)", v, re.I)
        if m:
            files[int(k)] = paths.get(os.path.basename(m.group(1)).lower(), m.group(1))
    cites = {}
    for f in index["functions"]:
        named = collections.Counter(files[at] for at, _ in f["refs"] if at in files)
        if named:
            cites[f["at"]] = (named.most_common(1)[0][0], cited_lines(rom, f, files))

    placed = {}
    order = [f["at"] for f in debug]
    known = [(k, at) for k, at in enumerate(order) if at in cites]
    for at, (name, lines) in cites.items():
        placed[at] = {"file": name, "lines": lines, "how": "cites"}
    for (k0, a0), (k1, a1) in zip(known, known[1:]):
        if cites[a0][0] == cites[a1][0]:
            for k in range(k0 + 1, k1):
                placed[order[k]] = {"file": cites[a0][0], "how": "between"}
    return placed


def symbol_maps():
    maps = {}
    for which in ("us", "debug"):
        path = os.path.join(REPO, "tools", "symbols", f"{which}.txt")
        entries = {}
        for line in open(path, encoding="utf-8") if os.path.exists(path) else []:
            m = re.match(r"\s*([\w:.~]+)\s*=\s*0x([0-9a-fA-F]+);", line)
            if m:
                entries.setdefault(int(m.group(2), 16), m.group(1))
        maps[which] = entries
    return maps


def main():
    us, us_index = functions("us")
    debug, debug_index = functions("debug")
    pairs = match(us, debug)
    placed = source_files(debug, debug_index)

    by_at = {f["at"]: f for f in debug}
    exact = sum(1 for f in us if f["at"] in pairs and by_at[pairs[f["at"]]]["shape"] == f["shape"])
    calls = agree = 0
    for f in us:
        if f["at"] in pairs:
            for c in f["calls"]:
                if c in pairs:
                    calls += 1
                    agree += pairs[c] in by_at[pairs[f["at"]]]["calls"]

    # 1.0's own file names check the match.
    us_files = {int(k): v for k, v in us_index["strings"].items() if SOURCE.search(v)}
    checked = wrong = 0
    for f in us_index["functions"]:
        for at, _ in f["refs"]:
            if at in us_files and f["at"] in pairs and pairs[f["at"]] in placed:
                checked += 1
                wrong += os.path.basename(placed[pairs[f["at"]]]["file"]).lower() != os.path.basename(us_files[at]).lower()

    json.dump({"us_to_debug": {str(k): v for k, v in sorted(pairs.items())},
               "files": {str(k): v for k, v in sorted(placed.items())}},
              open(os.path.join(OUT, "builds.json"), "w", encoding="utf-8"))

    # Each build's map with the other's names carried across the match, for Ghidra.
    maps = symbol_maps()
    debug_to_us = {v: k for k, v in pairs.items()}
    conflicts = []
    for which, other, across in (("us", "debug", debug_to_us), ("debug", "us", pairs)):
        merged = dict(maps[which])
        for at, name in maps[other].items():
            mine = across.get(at)
            if mine is None:
                continue
            if mine in merged and merged[mine] != name:
                conflicts.append(f"{which} {mine:08x} {merged[mine]} vs {other} {at:08x} {name}")
            merged.setdefault(mine, name)
        with open(os.path.join(OUT, f"{which}.symbols.txt"), "w", encoding="utf-8") as w:
            for at, name in sorted(merged.items()):
                w.write(f"{name} = 0x{at:08x};\n")

    print(f"matched {len(pairs)} of 1.0's {len(us)} functions to the debug build's {len(debug)}; "
          f"{exact} alike exactly; {agree} of {calls} matched calls agree")
    print(f"placed {len(placed)} debug functions in {len(set(p['file'] for p in placed.values()))} files "
          f"({sum(1 for p in placed.values() if p['how'] == 'cites')} cite theirs); "
          f"1.0's own file names agree for {checked - wrong} of {checked}")
    for c in conflicts:
        print("name conflict:", c)


if __name__ == "__main__":
    main()
