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
same file is in that file too, since a file's functions are linked together. Each file's code starts
on a 16-byte boundary, so the padding before one marks its start, and the code between two starts
(a unit) is one file, or several where a file ended on a boundary in both builds. 1.0 takes the
debug build's files through the match. See docs/reference-rig.md for how sure each placement is.
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
RODATA_END = 0x800E5C90  # the debug build's .data starts here, after the rodata
JR_RA = 0x03E00008


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


def file_starts(which, fs):
    """Where a file's code starts, by its padding: each file's code is aligned to 16 bytes, and
    functions inside a file abut. So code on a 16-byte boundary after one to three nops that follow
    a function's end starts a file. Some starts are of functions the code index misses, which no
    call reaches. A file whose code ends on a boundary leaves no padding, and hides its end."""
    rom, _ = load(which)
    starts = {g["at"] for f, g in zip(fs, fs[1:]) if g["at"] - f["end"] in (4, 8, 12) and g["at"] % 16 == 0}
    lo, hi = fs[0]["at"], fs[-1]["end"]
    words = struct.unpack(f">{(hi - lo) // 4}I", rom[lo - 0x80000400 + 0x1000:hi - 0x80000400 + 0x1000])
    for k in range(4, len(words), 4):
        if words[k]:
            pad = next((n for n in (1, 2, 3) if not any(words[k - n:k]) and words[k - n - 2] == JR_RA), None)
            if pad:
                starts.add(lo + 4 * k)
    return starts


def holes(fs):
    """Where the code index misses code, between one function's end and the next's start."""
    return {g["at"]: (f["end"], g["at"]) for f, g in zip(fs, fs[1:]) if g["at"] - f["end"] > 12}


def rodata_votes(debug, index, placed):
    """The file each function's rodata says it's in. Each file's rodata (strings, floats, jump
    tables, vtables) is linked in the same order as its code, so a rodata address only one
    function uses lies among the rodata of that function's file."""
    starts = [f["at"] for f in debug]
    lo, hi = index["text"][1], RODATA_END

    def owner(a):
        k = bisect.bisect_right(starts, a) - 1
        return starts[k] if k >= 0 and a < debug[k]["end"] else None

    users = collections.defaultdict(set)
    for f in index["functions"]:
        for a, _ in f["refs"]:
            if lo <= a < hi:
                users[a].add(f["at"])
    for d, t in index["pointers"]:
        o = owner(t)
        if o and lo <= d < hi:
            users[d].add(o)
    ruler = sorted((a, placed[next(iter(us))]["file"]) for a, us in users.items()
                   if all(u in placed for u in us) and len({placed[u]["file"] for u in us}) == 1)
    keys = [a for a, _ in ruler]
    votes = collections.defaultdict(collections.Counter)
    for a, us in users.items():
        if len(us) == 1 and not us & placed.keys():
            k = bisect.bisect_left(keys, a)
            if 0 < k < len(ruler) and ruler[k - 1][1] == ruler[k][1]:
                votes[next(iter(us))][ruler[k][1]] += 1
    return votes


def source_files(debug, index, us, pairs):
    """Each debug function's source file and the lines it cites, and the files' extents: a list of
    units, each one file's code, named or not."""
    rom, _ = load("debug")
    paths = {os.path.basename(v).lower(): v for v in index["strings"].values() if SOURCE.search(v)}
    files = {}
    for k, v in index["strings"].items():
        # A file name, or a message naming one: "scene.cpp, SceneGetLocatorMtx()", "app.cpp::AppProc",
        # "borganim.cpp:LoadTextureImage", "GetIdIndex (ids.cpp)".
        m = re.search(r"(?:^|\()([\w./]+\.(?:cpp|c|h))(?:$|[,: )])", v, re.I)
        if m:
            files[int(k)] = paths.get(os.path.basename(m.group(1)).lower(), m.group(1))
    placed = {}
    for f in index["functions"]:
        named = collections.Counter(files[at] for at, _ in f["refs"] if at in files)
        if named:
            placed[f["at"]] = {"file": named.most_common(1)[0][0], "lines": cited_lines(rom, f, files), "how": "cites"}

    # Units: the code between two file starts, in either build. A file is in one unit, and a unit
    # holds one file, or several where a file's end is hidden in both builds.
    order = [f["at"] for f in debug]
    us_order = [f["at"] for f in us]
    starts = file_starts("debug", debug)
    for a in file_starts("us", us):
        k = bisect.bisect_left(us_order, a)
        while k < len(us_order) and us_order[k] not in pairs:
            k += 1
        if k < len(us_order):
            starts.add(pairs[us_order[k]] if us_order[k] == a else pairs[us_order[k]] - 1)
    units, problems = [[0]], []
    starts = sorted(starts)
    for k in range(1, len(order)):
        if bisect.bisect_right(starts, order[k]) != bisect.bisect_right(starts, order[k - 1]):
            units.append([])
        units[-1].append(k)
    # A file is cited in one unit. Two units citing one path, each with its own copy of the path's
    # string in its own rodata, are two files of that name: ./src/audio.cpp is the game's sound
    # and, apart from it, the audio driver. The second is named "./src/audio.cpp (2)".
    strings = {f["at"]: {at for at, _ in f["refs"] if at in files and SOURCE.search(index["strings"][str(at)])}
               for f in index["functions"] if f["at"] in placed}
    seen = {}
    for n, unit in enumerate(units):
        cites = [order[k] for k in unit if order[k] in placed]
        for name in dict.fromkeys(placed[at]["file"] for at in cites):
            mine = set().union(*(strings[at] for at in cites if placed[at]["file"] == name))
            if name not in seen:
                seen[name] = mine
                continue
            if mine and not mine & seen[name]:
                copies = sum(1 for other in seen if other.startswith(name))
                seen[f"{name} ({copies + 1})"] = mine
                for at in cites:
                    if placed[at]["file"] == name:
                        placed[at]["file"] = f"{name} ({copies + 1})"
            else:
                problems.append(f"{name} cited in two units, the second at {order[unit[0]]:08x}")

    def fill(ks, name, how):
        for k in ks:
            placed.setdefault(order[k], {"file": name, "how": how})

    def cited(unit):
        runs = []
        for k in unit:
            if order[k] in placed and placed[order[k]]["how"] == "cites":
                name = placed[order[k]]["file"]
                if runs and runs[-1][0] == name:
                    runs[-1][2] = k
                else:
                    runs.append([name, k, k])
        return runs

    # A file hidden in a unit starts on a 16-byte boundary in both builds.
    debug_to_us = {v: k for k, v in pairs.items()}

    debug_holes, us_holes = holes(debug), holes(us)

    def can_start(k):
        """Whether a file could start at function k, or in code the index misses just before it."""
        here = debug_to_us.get(order[k])
        return (order[k] % 16 == 0 or order[k] in debug_holes) and (here is None or here % 16 == 0 or here in us_holes)

    def edges(unit, name, k0, k1):
        """A unit's functions before its first file's first citing function (k0), or after its last
        file's last (k1), are in that file, certainly where no hidden file could start between."""
        for k in range(unit[0], k0):
            fill([k], name, "probable" if any(can_start(j) for j in range(k + 1, k0 + 1)) else "unit")
        for k in range(k1 + 1, unit[-1] + 1):
            fill([k], name, "probable" if any(can_start(j) for j in range(k1 + 1, k + 1)) else "unit")

    # A run between two functions citing one file is in it, and so is the rest of a unit with one file.
    split = []
    for unit in units:
        runs = cited(unit)
        for name, k0, k1 in runs:
            fill(range(k0 + 1, k1), name, "between")
        if len({r[0] for r in runs}) != len(runs):
            problems.append(f"the unit at {order[unit[0]]:08x} has a file in two runs: {[r[0] for r in runs]}")
        elif len(runs) == 1:
            edges(unit, runs[0][0], runs[0][1], runs[0][2])
        elif runs:
            split.append((unit, runs))

    # Where a unit holds several files, each file but the first starts on a 16-byte boundary. The
    # rodata votes narrow the candidates between one file's last citing function and the next's first.
    votes = rodata_votes(debug, index, placed)
    for unit, runs in split:
        edges(unit, runs[0][0], runs[0][1], unit[-1])
        edges(unit, runs[-1][0], unit[0], runs[-1][2])
        for (x, _, kx), (y, ky, _) in zip(runs, runs[1:]):
            first, last = kx + 1, ky - 1
            for k in range(kx + 1, ky):
                v = votes.get(order[k], {})
                if v.get(x) and not v.get(y):
                    first = k + 1
                if v.get(y) and not v.get(x):
                    last = min(last, k - 1)
            if first > last + 1:
                problems.append(f"rodata disagrees about where {y} starts, near {order[ky]:08x}")
                continue
            fill(range(kx + 1, first), x, "rodata")
            fill(range(last + 1, ky), y, "rodata")
            candidates = [k for k in range(first, last + 2) if can_start(k)]
            if not candidates:
                problems.append(f"{y} can't start between {order[kx]:08x} and {order[ky]:08x}: one file, named two ways?")
            elif len(candidates) == 1:
                fill(range(first, candidates[0]), x, "boundary")
                fill(range(candidates[0], last + 1), y, "boundary")

    extents = [{"at": order[u[0]], "end": debug[u[-1]]["end"],
                "files": list(dict.fromkeys(placed[order[k]]["file"] for k in u if order[k] in placed))}
               for u in units]
    return placed, extents, problems


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
    placed, units, problems = source_files(debug, debug_index, us, pairs)

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
               "files": {str(k): v for k, v in sorted(placed.items())},
               "units": units},
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
    named = [u for u in units if u["files"]]
    print(f"{len(units)} units: {len(named)} hold {sum(len(u['files']) for u in named)} named files, "
          f"{len(units) - len(named)} are unnamed; "
          + ", ".join(f"{n} {h}" for h, n in collections.Counter(p["how"] for p in placed.values()).most_common()))
    for p in problems:
        print("problem:", p)
    for c in conflicts:
        print("name conflict:", c)


if __name__ == "__main__":
    main()
