"""Combat parity logs (roadmap 2.9): every draw a battle makes on the original's stream, its rules' entry
points, and its RAM at each turn's start, for the port to replay (docs/user-checks.md#combat-parity-29).

Inside gdb (tools/parity.ps1 runs it), against 1.0. A battle is logged from Combat.Setup to Combat.Exit,
each into a folder of its own under PARITY_OUT (battle-01, battle-02, ...):

  log.jsonl     one record a line:
                  {"t": "draw", "old", "new", "pc", "chain", "state"}   a step of the stream
                  {"t": "enter", "name", "pc", "ra", "args", "seed", "state", "delta", "frame",
                   "current", "combatant", "dump"}                       a rules entry point
                  {"t": "leave", "name", "pc", "seed"}                   a watched setup function returned
  NNNN.rdram.gz RDRAM, 8 MB big-endian from 0x80000000, at Combat.Setup, each TurnOrder.BeginTurn and
                Combat.Exit's teardown (about 2 s each, which the game waits out)

Outside a battle only the functions that fill the setup block are watched, from their entry to their
return: their draws go into the next battle's log, before its Combat.Setup.

A draw stops where Rng.Next or Float0To1 returns, its store in the return's delay slot, so $pc is in
the function that drew; "chain" is $pc and up to three return addresses above it, unwound by the
functions' prologues (the code index's starts, then addiu sp and sw ra). Weather.DriftWind's own
two draws a frame aren't unwound. Each stop costs about 15 ms in ares, so a battle plays at about 60% speed.

Outside gdb: python tools/gdb/parity.py --summary <battle folder> prints the entry points and the draws'
callers, named.
"""
import bisect
import gzip
import json
import os
import sys
import time

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(TOOLS)

SEED = 0x800D8088
STATE = 0x800D9454
DELTA = 0x800D8094
COMBAT = 0x800E0BF0
ACTIVE = 0x800DB0A0

# The rules' entry points, each logged with the current combatant; those with dump=True also save RAM.
ENTRIES = {
    0x8005D15C: ("Combat.Setup", True),
    0x8006AC4C: ("TurnOrder.Advance", False),
    0x8006A9C8: ("TurnOrder.BeginTurn", True),
    0x8005B314: ("EnemyAI.Step", False),
    0x80065F6C: ("Combatant.ExecuteAction", False),
    0x800219D8: ("Combat.EndCast", False),
    0x8006A62C: ("TurnOrder.EndRound", False),
    0x8005E2AC: ("Combat.TallyRewards", False),
    0x80023090: ("Combat.Exit", True),  # past its check of Combat.ExitRequested, which it makes every frame
}
# What fills the setup block in trek: its draws are watched from entry to return.
WINDOWS = {
    0x800094B4: "BattleSetup.FromMonsterParty",
    0x800096DC: "CampEncounters.Load",
    0x80050804: "BattleSetup.Scripted",
}
# Weather.DriftWind's own draws, two every frame: logged, never unwound (those through ParticleHead.FloatRange are).
UNWOUND_NOT = {0x8007BAE8, 0x8007BB74}
DEPTH = 4

# The generator, seed * A + C, and a step back.
A, C = 0x19660D, 0x3C6EF35F
A_INVERSE = pow(A, -1, 1 << 32)


def back(seed):
    return (seed - C) * A_INVERSE & 0xFFFFFFFF


class Code:
    """Function starts from the code index, and each function's frame size and saved $ra by its prologue."""

    def __init__(self, read):
        index = json.load(open(os.path.join(REPO, "out", "ghidra", "us.index.json"), encoding="utf-8"))
        self.starts = sorted(f["at"] for f in index["functions"])
        self.read = read
        self.frames = {}

    def frame(self, pc):
        """(frame size, $ra's offset in it) of the function holding pc, or None for a leaf."""
        i = bisect.bisect_right(self.starts, pc) - 1
        if i < 0:
            return None
        start = self.starts[i]
        if start not in self.frames:
            words = self.read(start, 0x60)
            size = offset = None
            for n in range(0, len(words), 4):
                w = int.from_bytes(words[n:n + 4], "big")
                if w >> 16 == 0x27BD and w & 0x8000:  # addiu sp, sp, -N
                    size = 0x10000 - (w & 0xFFFF)
                elif w >> 16 == 0xAFBF:  # sw ra, off(sp)
                    offset = w & 0xFFFF
                if size and offset is not None:
                    break
            self.frames[start] = (size, offset) if size and offset is not None else None
        return self.frames[start]

    def chain(self, pc, sp, ra):
        out = [pc]
        for depth in range(DEPTH - 1):
            frame = self.frame(pc - 4 if depth else pc)
            if frame is None:
                if depth:
                    break  # a leaf above the first frame can't be unwound
                pc = ra
            else:
                size, offset = frame
                pc = int.from_bytes(self.read(sp + offset, 4), "big")
                sp += size
            if not 0x80000000 <= pc < 0x80800000:
                break
            out.append(pc)
        return out


def log():
    import gdb

    inferior = gdb.selected_inferior()

    def read(address, size):
        return bytes(inferior.read_memory(address, size))

    def word(address):
        return int.from_bytes(read(address, 4), "big")

    def reg(name):
        return int(gdb.parse_and_eval(f"${name}")) & 0xFFFFFFFF

    code = Code(read)
    root = os.environ["PARITY_OUT"]
    os.makedirs(root, exist_ok=True)
    progress = open(os.path.join(root, "progress.txt"), "a", encoding="utf-8")

    def mark(text):
        progress.write(f"# {text}\n")
        progress.flush()

    for setting in ("pagination off", "confirm off", "breakpoint always-inserted on"):
        gdb.execute(f"set {setting}")
    for at in list(ENTRIES) + list(WINDOWS):
        gdb.execute(f"break *0x{at:x}", to_string=True)
    gdb.execute(f"watch *(unsigned int*)0x{SEED:x}", to_string=True)
    watch = [b for b in gdb.breakpoints() if b.type == gdb.BP_HARDWARE_WATCHPOINT][0]

    battle = 0
    pending = []  # records made before a battle's folder exists: a setup window's draws
    out = None
    dumps = 0
    returns = {}  # a window's return address -> its name

    def write(record):
        if out:
            out.write(json.dumps(record) + "\n")
            out.flush()
        else:
            pending.append(record)

    def begin():
        nonlocal battle, out, dumps
        if out:
            out.close()  # a battle whose Combat.Exit was never reached
        battle += 1
        folder = os.path.join(root, f"battle-{battle:02}")
        os.makedirs(folder, exist_ok=True)
        out = open(os.path.join(folder, "log.jsonl"), "w", encoding="utf-8")
        dumps = 0
        for r in pending:
            out.write(json.dumps(r) + "\n")
        pending.clear()
        mark(f"battle {battle} begins")
        return folder

    folder = None
    in_battle = word(ACTIVE) != 0
    if in_battle:
        folder = begin()
        mark("attached during a battle; logging from here")
    watch.enabled = in_battle
    mark("ready")

    while True:
        try:
            text = gdb.execute("continue", to_string=True)
        except gdb.error as e:
            mark(f"stopped: {e}")
            break
        if "exited" in text or "Remote connection closed" in text:
            mark("ares closed")
            break
        pc = reg("pc")

        if pc not in ENTRIES and pc not in WINDOWS and pc not in returns:
            # The watchpoint (its old and new values aren't in the text gdb hands back): the seed now, and
            # the one before it a step back.
            seed = word(SEED)
            chain = [pc] if pc in UNWOUND_NOT else code.chain(pc, reg("sp"), reg("ra"))
            write({"t": "draw", "old": back(seed), "new": seed, "pc": pc, "chain": chain, "state": read(STATE, 1)[0]})
            continue

        if pc in returns:
            name = returns.pop(pc)
            gdb.execute(f"clear *0x{pc:x}", to_string=True)
            write({"t": "leave", "name": name, "pc": pc, "seed": word(SEED)})
            if not in_battle and not returns:
                watch.enabled = False
            continue

        if pc in WINDOWS:
            ra = reg("ra")
            returns[ra] = WINDOWS[pc]
            gdb.execute(f"break *0x{ra:x}", to_string=True)
            write({"t": "enter", "name": WINDOWS[pc], "pc": pc, "ra": ra,
                   "args": [reg(r) for r in ("a0", "a1", "a2", "a3")], "seed": word(SEED)})
            watch.enabled = True
            continue

        if pc not in ENTRIES:
            continue  # a stop of gdb's own
        name, dump = ENTRIES[pc]
        if name == "Combat.Setup":
            folder = begin()
            in_battle = True
            watch.enabled = True
        if out is None:
            folder = begin()
            in_battle = True
            watch.enabled = True
        combat = word(COMBAT)
        record = {"t": "enter", "name": name, "pc": pc, "ra": reg("ra"),
                  "args": [reg(r) for r in ("a0", "a1", "a2", "a3")], "seed": word(SEED),
                  "state": read(STATE, 2).hex(), "delta": read(DELTA, 4).hex(), "frame": None,
                  "current": None, "combatant": None, "dump": None}
        if 0x80000000 <= combat < 0x80800000:
            record["frame"] = word(combat + 0x5448)
            current = word(combat + 0x2C)
            if 0x80000000 <= current < 0x80800000:
                block = read(current, 0x90)
                record["current"] = block[0x20]
                record["combatant"] = block.hex()
        if dump:
            dumps += 1
            record["dump"] = f"{dumps:04}.rdram.gz"
            t = time.time()
            with gzip.open(os.path.join(folder, record["dump"]), "wb", compresslevel=1) as f:
                f.write(read(0x80000000, 0x800000))
            record["dumpSeconds"] = round(time.time() - t, 2)
        write(record)
        if name == "TurnOrder.BeginTurn":
            mark(f"battle {battle}: turn of slot {record['current']}")
        if name == "Combat.Exit":
            mark(f"battle {battle} ends")
            out.close()
            out = None
            in_battle = False
            watch.enabled = bool(returns)
    progress.close()


def summary(folder):
    sys.path.insert(0, TOOLS)
    from n64dis import load

    _, symbols = load("us")
    starts = sorted(symbols)

    def name(at):
        i = bisect.bisect_right(starts, at) - 1
        return f"{symbols[starts[i]]}+0x{at - starts[i]:x}" if i >= 0 else f"{at:08x}"

    callers = {}
    for line in open(os.path.join(folder, "log.jsonl"), encoding="utf-8"):
        r = json.loads(line)
        if r["t"] == "enter":
            who = f" slot {r['current']}" if r.get("current") is not None else ""
            dump = f" -> {r['dump']}" if r.get("dump") else ""
            print(f"{r['name']}{who} seed {r['seed']:08x}{dump}")
        elif r["t"] == "draw" and r["pc"] not in UNWOUND_NOT:
            key = " < ".join(name(a) for a in r["chain"])
            callers[key] = callers.get(key, 0) + 1
    print()
    for key, n in sorted(callers.items(), key=lambda kv: -kv[1]):
        print(f"{n:6}  {key}")


if __name__ == "__main__" and "--summary" in sys.argv:
    summary(sys.argv[sys.argv.index("--summary") + 1])
else:
    log()
