"""First-hit function traces (roadmap 0.12): which functions an action runs that standing still doesn't.

Inside gdb (tools/trace.ps1 runs it): a temporary breakpoint on every function entry in the code index,
each removed by its first hit, so a function that runs every frame costs one stop and then nothing.
A baseline window runs first, then an action window; a function first hit in the action window never
ran in the baseline. The log has one line per first hit, "<phase> <seconds> <address>", and "# <phase>"
markers that trace.ps1 shows as they're written. Settings come from the environment: TRACE_ROM (us or
debug), TRACE_OUT (the log), TRACE_PHASES ("baseline:20,action:30"), and optionally TRACE_RANGE
("lo..hi") or TRACE_FILES (text a source file's name holds, by tools/builds.py's placement).

Outside gdb: python tools/gdb/trace.py --report <log> prints the action's functions, named, with
their source files.
"""
import json
import os
import sys
import threading
import time

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(TOOLS)


def entries(rom):
    index = json.load(open(os.path.join(REPO, "out", "ghidra", f"{rom}.index.json"), encoding="utf-8"))
    starts = [f["at"] for f in index["functions"]]
    if os.environ.get("TRACE_RANGE"):
        low, _, high = os.environ["TRACE_RANGE"].partition("..")
        starts = [a for a in starts if int(low, 0) <= a < int(high, 0)]
    if os.environ.get("TRACE_FILES"):
        builds = json.load(open(os.path.join(REPO, "out", "ghidra", "builds.json"), encoding="utf-8"))
        text = os.environ["TRACE_FILES"].lower()
        placed = {int(k) for k, v in builds["files"].items() if text in v["file"].lower()}
        if rom == "us":
            placed = {int(k) for k, v in builds["us_to_debug"].items() if v in placed}
        starts = [a for a in starts if a in placed]
    return starts


def trace():
    import gdb

    rom = os.environ.get("TRACE_ROM", "us")
    phases = [(name, float(seconds)) for name, seconds in
              (p.split(":") for p in os.environ.get("TRACE_PHASES", "baseline:20,action:30").split(","))]
    starts = entries(rom)
    breakpoints = set(starts)
    for setting in ("pagination off", "confirm off", "breakpoint always-inserted on"):
        gdb.execute(f"set {setting}")

    def interrupt():
        try:
            gdb.execute("interrupt")
        except gdb.error:
            pass  # already stopped at a breakpoint as the window closed

    with open(os.environ["TRACE_OUT"], "w", encoding="utf-8") as log:
        log.write(f"# rom {rom}\n# setup {len(starts)} functions\n")
        log.flush()
        t0 = time.time()
        for at in starts:
            gdb.execute(f"tbreak *0x{at:x}", to_string=True)
        log.write(f"# ready after {time.time() - t0:.1f} s\n")
        t0 = time.time()
        for name, seconds in phases:
            log.write(f"# {name} {seconds:g} s\n")
            log.flush()
            deadline = time.time() + seconds
            timer = threading.Timer(seconds, lambda: gdb.post_event(interrupt))
            timer.start()
            while time.time() < deadline:
                gdb.execute("continue", to_string=True)
                pc = int(gdb.parse_and_eval("$pc")) & 0xFFFFFFFF
                if pc in breakpoints:
                    breakpoints.discard(pc)
                    log.write(f"{name} {time.time() - t0:.3f} {pc:08x}\n")
                    log.flush()
            timer.cancel()
        log.write("# done\n")


def report(path):
    sys.path.insert(0, TOOLS)
    from n64dis import load, load_builds, source_file

    lines = [l.split() for l in open(path, encoding="utf-8") if l.strip() and not l.startswith("#")]
    rom = next((l.split()[2] for l in open(path, encoding="utf-8") if l.startswith("# rom ")), "us")
    _, symbols = load(rom)
    builds = load_builds()
    order = [l.split()[1] for l in open(path, encoding="utf-8")
             if l.startswith("# ") and l.rstrip().endswith(" s") and len(l.split()) == 4]
    counts = {p: sum(1 for l in lines if l[0] == p) for p in order}
    print(", ".join(f"{p}: {n} functions first hit" for p, n in counts.items()))
    last = order[-1] if order else None
    if last and not counts[last]:
        print(f"Nothing ran in the {last} window that the earlier windows didn't.")
    for phase, seconds, address in lines:
        if phase == last:
            at = int(address, 16)
            where = source_file(rom, at, builds) or ""
            name = symbols.get(at, "")
            print(f"{float(seconds):8.3f}  {at:08x}  {name:40} {where}".rstrip() if where else f"{float(seconds):8.3f}  {at:08x}  {name}".rstrip())


if __name__ == "__main__" and "--report" in sys.argv:
    report(sys.argv[sys.argv.index("--report") + 1])
else:
    trace()
