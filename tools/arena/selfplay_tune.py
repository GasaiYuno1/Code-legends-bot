#!/usr/bin/env python3
"""Подбор весов по силе, а не по подражанию: покоординатный спуск, где оценка варианта — матч
против зафиксированной эталонной сборки (мини-арбитр BotMatch, стороны чередуются).

Вариант принимается, если выиграл не меньше accept-доли партий; принятые ключи накапливаются.
Эталон и кандидат — одна и та же сборка (копии в build/bot-ref и build/bot-cand), различаются только аргументами.

Запуск: python3 tools/arena/selfplay_tune.py [--games 40] [--accept 0.6] [--passes 2] [--seed 1000]
        [--fixed key=value ...] [--grid hp=0.2,0.45 ...]
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
REF = ROOT / "build" / "bot-ref" / "LocmBot.dll"
CAND = ROOT / "build" / "bot-cand" / "LocmBot.dll"
CLASSES = ROOT / "build" / "refcheck-classes"
RESOURCES = Path("/home/user/codingame/legendsofcodeandmagic/src/main/resources")
DEFAULT_GRID = {
    "hp": [0.2, 0.45],
    "lowhp": [0.4, 1.2],
    "def": [0.6, 1.0],
    "guard": [0.4, 1.2],
    "ward": [0.8, 1.8],
    "lethal": [1.0, 2.5],
    "hand": [0.6, 1.5],
    "oppdraw": [1.0, 2.5],
    "reply": [0.6, 0.9],
    "draftwin": [1],
    "curvew": [0.0, 0.3],
}
WINS_RE = re.compile(r"A: wins (\d+)/(\d+)")


def match(games, seed, params):
    cand = f"dotnet {CAND} " + " ".join(f"{k}={v}" for k, v in params.items())
    cmd = ["java", "-cp", f"{CLASSES}:{RESOURCES}", "BotMatch", str(games), str(seed), cand, f"dotnet {REF}"]
    out = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT).stdout
    m = WINS_RE.search(out)
    if not m:
        print(out[-500:], file=sys.stderr)
        raise RuntimeError("match failed")
    return int(m.group(1)), int(m.group(2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--games", type=int, default=40)
    ap.add_argument("--accept", type=float, default=0.6)
    ap.add_argument("--passes", type=int, default=2)
    ap.add_argument("--seed", type=int, default=1000)
    ap.add_argument("--fixed", nargs="*", default=[])
    ap.add_argument("--grid", nargs="*", default=[])
    args = ap.parse_args()

    grid = dict(DEFAULT_GRID)
    for g in args.grid:
        k, vs = g.split("=", 1)
        grid[k] = [float(x) for x in vs.split(",")]
    params = {}
    for f in args.fixed:
        k, v = f.split("=", 1)
        params[k] = float(v)

    seed = args.seed
    for p in range(args.passes):
        improved = False
        for key, values in grid.items():
            for v in values:
                if key in params and abs(params[key] - v) < 1e-9:
                    continue
                trial = dict(params)
                trial[key] = v
                seed += 1
                w, n = match(args.games, seed, trial)
                mark = ""
                if w / n >= args.accept:
                    params, improved, mark = trial, True, "  <- accepted"
                print(f"pass {p + 1} {key}={v} (+{params if mark else ''}): {w}/{n}{mark}", flush=True)
        print(f"after pass {p + 1}: {params}", flush=True)
        if not improved:
            break
    # финальная проверка принятого набора против эталона на других сидах
    if params:
        w, n = match(args.games * 2, seed + 1000, params)
        print(f"FINAL {params}: {w}/{n} vs reference", flush=True)
    else:
        print("FINAL: nothing accepted", flush=True)


if __name__ == "__main__":
    main()
