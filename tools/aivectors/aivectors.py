"""Regenerates tests/Aidyn.Rom.Tests/CasterVectors.cs: 1.0's own enemy-caster AI, replayed on the combat checkpoints' RAM.

    python tools/aivectors/aivectors.py                  # all 123 cases, about 12 minutes on 22 workers
    python tools/aivectors/aivectors.py --only Lugash    # the cases whose id has the text; writes out/aivectors/vectors.json only
    python tools/aivectors/aivectors.py --roms DIR --jobs 8 --out other.cs

What it does (docs/reference-rig.md, "Replaying the original's AI"):
  1. Reads 1.0's code segment from the ROM and disassembles it (rig.code, cached in out/aivectors/insns.pkl), and each case's checkpoint
     (roms/<1.0 ROM> - Combat NN, <label>.bs1, Combat 02 and 08 to 11) as RDRAM with tools/bst.py (cached in out/aivectors/ram/).
  2. For every case (cases.py) builds an enemy caster or boss in a rat's place with the original's own entity builder (ovl.py), begins its
     turn, and runs ScoreSpellCells, ChooseSpellOrAttack, RunStyle and EnemyAI.Step in the MIPS interpreter (cpu.py) for five seeds of the
     sort's stream (vec.py, vec2.py). Then the one-off vectors of extras.py: ScoreSpellCells by action slot, SpellValue by kind, AICanCast,
     the area danger and the morale spells.
  3. Puts them in out/aivectors/vectors.json (emit.assemble) and writes the test's table from it (emit.write_cs).

Needs `pip install capstone numpy`. The ROM and the states are found in roms/ or AIDYN_ROMS. Nothing it builds is kept outside out/.
"""
import argparse
import json
import os
import sys
import time
import traceback
from multiprocessing import Pool

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig  # noqa: E402

DEFAULT_CS = os.path.join(rig.REPO, 'tests', 'Aidyn.Rom.Tests', 'CasterVectors.cs')


def work(task):
    """Runs one task in a worker: ('case', case) | ('single', case) | ('kinds'|'aican'|'misc', None)."""
    kind, payload = task
    name = payload['id'] if payload else kind
    t = time.time()
    try:
        if kind == 'case':
            from vec2 import run_case
            result = run_case(payload)
        elif kind == 'single':
            import extras
            result = extras.run_single(payload)
        else:
            import extras
            result = getattr(extras, kind)()
        return kind, name, result, time.time() - t, None
    except Exception:
        return kind, name, None, time.time() - t, traceback.format_exc()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--roms', help='the folder with the 1.0 ROM and the checkpoints (default AIDYN_ROMS, else roms/)')
    ap.add_argument('--out', default=DEFAULT_CS, help='the C# file to write')
    ap.add_argument('--jobs', type=int, default=min(os.cpu_count() or 1, 22), help='worker processes')
    ap.add_argument('--only', help='run only the cases whose id has this text, and write only out/aivectors/vectors.json')
    a = ap.parse_args()
    if a.roms:
        os.environ['AIDYN_ROMS'] = a.roms     # inherited by the workers

    import cases as case_lists
    import extras
    cases = case_lists.all_cases()
    singles = extras.SINGLE_CASES
    extra = ['kinds', 'aican', 'misc']
    if a.only:
        cases = [c for c in cases if a.only in c['id']]
        singles = [c for c in singles if a.only in c['id']]
        extra = []
    tasks = [(k, None) for k in extra] + [('single', c) for c in singles] + [('case', c) for c in cases]
    print(f'{len(cases)} cases, {len(singles)} single-slot, {len(extra)} other; ROM {rig.rom_path()}', flush=True)

    rig.code()      # build the disassembly once, before the workers need it
    for n in sorted({c['cp'] for c in cases + singles} | ({8} if extra else set())):
        rig.ram(n)       # read each state once, here, and fail early if one is missing

    t0 = time.time()
    done = {'case': [], 'single': [], 'kinds': None, 'aican': None, 'misc': None}
    with Pool(max(1, min(a.jobs, len(tasks)))) as pool:
        for kind, name, result, seconds, error in pool.imap_unordered(work, tasks, chunksize=1):
            if error:
                pool.terminate()
                sys.exit(f'{name} failed after {seconds:.0f}s:\n{error}')
            print(f'{name} ok {seconds:.0f}s', flush=True)
            if kind in ('case', 'single'):
                done[kind].append(result)
            else:
                done[kind] = result
    print(f'ran in {time.time() - t0:.0f}s', flush=True)

    import emit
    order = {c['id']: i for i, c in enumerate(extras.SINGLE_CASES)}
    done['single'].sort(key=lambda r: order[r['id']])
    vectors = emit.assemble(done['case'], done['single'], done['kinds'], done['aican'], done['misc'])
    os.makedirs(rig.OUT, exist_ok=True)
    path = os.path.join(rig.OUT, 'vectors.json')
    with open(path, 'w') as f:
        json.dump(vectors, f, separators=(',', ':'))
    print(f'{path}: {os.path.getsize(path) // 1024} KB')
    if a.only:
        print('--only: the C# file is not written')
        return
    n, size = emit.write_cs(vectors, a.out)
    print(f'{a.out}: {n} cases, {size} bytes')


if __name__ == '__main__':
    main()
