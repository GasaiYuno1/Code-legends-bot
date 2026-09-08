#!/usr/bin/env python3
"""Подбор весов оценки по совпадению с ходами Legend-игроков (режим `compare` тестового проекта).

Покоординатный спуск: для каждого ключа перебираем кандидаты, оставляем лучший по доле «same move»,
повторяем проходы, пока есть улучшение. Каждая оценка — запуск compare на выборке ходов.

Запуск: python3 tools/arena/tune.py <каталог логов> [--limit 2000] [--step 3] [--ms 30] [--passes 2]
        [--fixed key=value ...] [--grid hp=0.05,0.1,0.15,0.25 ...]
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
DEFAULT_GRID = {
    "hp": [0.05, 0.1, 0.15, 0.25, 0.4],
    "lowhp": [0.2, 0.4, 0.8, 1.2],
    "def": [0.6, 0.8, 1.0],
    "guard": [0.4, 0.8, 1.2, 1.6],
    "ward": [0.6, 1.2, 1.8],
    "lethal": [0.8, 1.5, 2.5],
    "hand": [0.5, 1.0, 1.5],
    "oppdraw": [1.0, 1.5, 3.0],
    "reply": [0.5, 0.75, 1.0],
}
SAME_RE = re.compile(r"same move:\s+(\d+) \(([\d.]+)%\)")


def evaluate(logs, ms, limit, step, params):
    cmd = ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(ROOT / "tests" / "LocmBot.Tests"), "--",
           "compare", logs, str(ms), f"limit={limit}", f"step={step}"] + [f"{k}={v}" for k, v in params.items()]
    out = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT).stdout
    m = SAME_RE.search(out)
    if not m:
        print(out, file=sys.stderr)
        raise RuntimeError("compare failed")
    return float(m.group(2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("logs")
    ap.add_argument("--limit", type=int, default=2000)
    ap.add_argument("--step", type=int, default=3)
    ap.add_argument("--ms", type=int, default=30)
    ap.add_argument("--passes", type=int, default=2)
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

    best = evaluate(args.logs, args.ms, args.limit, args.step, params)
    print(f"baseline {best:.2f}% with {params}", flush=True)
    for p in range(args.passes):
        improved = False
        for key, values in grid.items():
            current = params.get(key)
            for v in values:
                if current is not None and abs(v - current) < 1e-9:
                    continue
                trial = dict(params)
                trial[key] = v
                score = evaluate(args.logs, args.ms, args.limit, args.step, trial)
                mark = ""
                if score > best + 0.05:
                    best, params, improved, mark = score, trial, True, "  <- best"
                print(f"pass {p + 1} {key}={v}: {score:.2f}%{mark}", flush=True)
        print(f"after pass {p + 1}: {best:.2f}% with {params}", flush=True)
        if not improved:
            break
    print("BEST", best, params)


if __name__ == "__main__":
    main()
