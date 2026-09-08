#!/usr/bin/env python3
"""SPSA-тюнинг весов оценки по силе в self-play (мини-арбитр BotMatch, Java 17+).

Каждая итерация: случайное направление Δ∈{±1}^d, матч θ+cΔ против θ−cΔ (стороны чередуются, партии
параллельно в нескольких процессах BotMatch), шаг по направлению Δ пропорционально (2·winrate − 1).
Раз в --eval-every итераций текущий θ играет против замороженного эталона (build/bot-ref2, дефолтные веса);
лучший по этой проверке θ сохраняется в build/spsa/best.json, полный журнал — build/spsa/log.txt.

Запуск (в фоне, на часы):
  python3 tools/arena/spsa.py --iters 100 --games 1000 --workers 4
  python3 tools/arena/spsa.py --resume            # продолжить с build/spsa/state.json
  python3 tools/arena/spsa.py --check build/spsa/best.json --games 4000   # только проверка θ против эталона
Сборка бота: build/bot-ref2/LocmBot.dll (dotnet build src/LocmBot -c Release && cp -r src/LocmBot/bin/Release/net8.0 build/bot-ref2).
"""
import argparse
import json
import random
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
BOT = ROOT / "build" / "bot-spsa" / "LocmBot.dll"   # эталон = текущая версия арены (SPSA-веса + selfw=20)
CLASSES = ROOT / "build" / "refcheck-classes"
RESOURCES = Path("/home/user/codingame/legendsofcodeandmagic/src/main/resources")
OUT = ROOT / "build" / "spsa"
WINS_RE = re.compile(r"A: wins (\d+)/(\d+)")

# ключ Tuning: (дефолт, масштаб возмущения c, минимум, максимум)
PARAMS = {
    "atk": (0.9567, 0.15, 0.2, 3.0),
    "def": (0.6572, 0.15, 0.1, 3.0),
    "guard": (0.7925, 0.2, 0.0, 3.0),
    "guarddef": (0.007, 0.1, 0.0, 1.5),
    "ward": (1.1138, 0.3, 0.0, 4.0),
    "wardatk": (0.4796, 0.1, 0.0, 1.5),
    "lethal": (2.0783, 0.4, 0.0, 5.0),
    "drain": (0.2222, 0.1, 0.0, 1.5),
    "breakthrough": (0.0553, 0.06, 0.0, 1.0),
    "charge": (0.1949, 0.1, 0.0, 1.5),
    "hp": (0.1024, 0.04, 0.0, 0.8),
    "lowhp": (0.9682, 0.2, 0.0, 3.0),
    "midhp": (0.0467, 0.05, 0.0, 0.6),
    "hand": (1.3635, 0.2, 0.0, 3.0),
    "handrating": (0.0936, 0.15, 0.0, 1.5),
    "fragile1": (0.0127, 0.15, 0.0, 1.5),
    "fragile2": (0.0, 0.1, 0.0, 1.0),
    "blue": (0.1463, 0.2, 0.0, 2.0),
    "oppdraw": (1.4892, 0.3, 0.0, 4.0),
    "mydraw": (1.5101, 0.3, 0.0, 4.0),
    "reply": (0.9, 0.08, 0.3, 0.95),
    # драфт
    "curvew": (0.1, 0.1, 0.0, 1.0),
    "selfw": (20.0, 8.0, 0.0, 80.0),
    "itempenalty": (3.0, 1.0, 0.0, 8.0),
}


def fmt(theta):
    return " ".join(f"{k}={theta[k]:.4g}" for k in PARAMS)


def cmd(theta, extra="warmup=0"):
    args = " ".join(f"{k}={theta[k]:.5g}" for k in PARAMS) if theta is not None else ""
    return f"dotnet {BOT} {extra} {args}".strip()


def match(games, seed, cmd_a, cmd_b, workers):
    """Партии делятся между workers процессами BotMatch; возвращает (победы A, всего)."""
    per = [games // workers + (1 if i < games % workers else 0) for i in range(workers)]
    procs = []
    for i, n in enumerate(per):
        if n == 0:
            continue
        c = ["java", "-cp", f"{CLASSES}:{RESOURCES}", "BotMatch", str(n), str(seed * 1000 + i), cmd_a, cmd_b]
        procs.append(subprocess.Popen(c, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True))
    wins = total = 0
    for p in procs:
        out, _ = p.communicate()
        m = WINS_RE.search(out)
        if not m:
            print(out[-800:], file=sys.stderr)
            raise RuntimeError("match failed")
        wins += int(m.group(1))
        total += int(m.group(2))
    return wins, total


def clip(theta):
    for k, (_, _, lo, hi) in PARAMS.items():
        theta[k] = min(hi, max(lo, theta[k]))
    return theta


def log(msg):
    line = time.strftime("%H:%M:%S ") + msg
    print(line, flush=True)
    with open(OUT / "log.txt", "a") as f:
        f.write(line + "\n")


def main():
    global CLASSES, OUT
    ap = argparse.ArgumentParser()
    ap.add_argument("--iters", type=int, default=100)
    ap.add_argument("--games", type=int, default=1000, help="партий на итерацию (θ+ против θ−)")
    ap.add_argument("--eval-games", type=int, default=1000, help="партий на проверку против эталона")
    ap.add_argument("--eval-every", type=int, default=10)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--lr", type=float, default=2.0, help="шаг: Δθ_i = lr·k_decay·(2w−1)·Δ_i·c_i")
    ap.add_argument("--decay", type=float, default=30.0, help="k_decay = decay/(iter+decay)")
    ap.add_argument("--cscale", type=float, default=1.0, help="множитель возмущения c")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--resume", action="store_true")
    ap.add_argument("--check", help="json с θ: только матч против эталона")
    ap.add_argument("--vs", nargs=2, metavar=("ARGS_A", "ARGS_B"),
                    help="только матч: аргументы key=value для A и для B (строки), --games партий, --bot сборка")
    ap.add_argument("--bot", default=str(BOT), help="LocmBot.dll для --vs (сторона A)")
    ap.add_argument("--bot-b", default=None, help="LocmBot.dll для стороны B в --vs (по умолчанию тот же, что --bot)")
    ap.add_argument("--classes", default=str(CLASSES), help="каталог классов BotMatch")
    ap.add_argument("--out", default=str(OUT), help="каталог журнала/состояния")
    args = ap.parse_args()
    CLASSES = Path(args.classes)
    OUT = Path(args.out)
    OUT.mkdir(parents=True, exist_ok=True)

    if args.vs:
        a = f"dotnet {args.bot} warmup=0 {args.vs[0]}".strip()
        b = f"dotnet {args.bot_b or args.bot} warmup=0 {args.vs[1]}".strip()
        t0 = time.time()
        w, n = match(args.games, args.seed + 999, a, b, args.workers)
        print(f"A {w}/{n} = {100.0 * w / n:.1f}% ({time.time() - t0:.0f} s)\n  A: {args.vs[0]}\n  B: {args.vs[1]}")
        return

    if args.check:
        theta = json.load(open(args.check))
        w, n = match(args.games, args.seed + 777, cmd(theta), cmd(None), args.workers)
        print(f"{w}/{n} = {100.0 * w / n:.1f}% vs reference: {fmt(theta)}")
        return

    rng = random.Random(args.seed)
    state_path = OUT / "state.json"
    if args.resume and state_path.exists():
        st = json.load(open(state_path))
        theta, it, best = st["theta"], st["iter"], st["best"]
        rng.setstate(tuple(tuple(x) if isinstance(x, list) else x for x in st["rng"]))
        log(f"resume at iter {it}: {fmt(theta)}")
    else:
        theta = {k: v[0] for k, v in PARAMS.items()}
        it, best = 0, {"score": 0.5, "theta": dict(theta), "iter": 0}
        log(f"start: {fmt(theta)}")

    while it < args.iters:
        delta = {k: rng.choice((-1.0, 1.0)) for k in PARAMS}
        plus = clip({k: theta[k] + args.cscale * PARAMS[k][1] * delta[k] for k in PARAMS})
        minus = clip({k: theta[k] - args.cscale * PARAMS[k][1] * delta[k] for k in PARAMS})
        t0 = time.time()
        w, n = match(args.games, args.seed * 100000 + it, cmd(plus), cmd(minus), args.workers)
        wr = w / n
        k_decay = args.decay / (it + args.decay)
        for k in PARAMS:
            theta[k] += args.lr * k_decay * (2 * wr - 1) * delta[k] * PARAMS[k][1]
        clip(theta)
        it += 1
        log(f"iter {it}: plus {w}/{n} = {100 * wr:.1f}% ({time.time() - t0:.0f} s)  -> {fmt(theta)}")
        if it % args.eval_every == 0:
            t0 = time.time()
            w, n = match(args.eval_games, args.seed * 100000 + 50000 + it, cmd(theta), cmd(None), args.workers)
            score = w / n
            log(f"  eval vs reference: {w}/{n} = {100 * score:.1f}% ({time.time() - t0:.0f} s)")
            if score > best["score"]:
                best = {"score": score, "theta": dict(theta), "iter": it}
                json.dump(theta, open(OUT / "best.json", "w"), indent=1)
                log(f"  new best {100 * score:.1f}% saved")
        json.dump({"theta": theta, "iter": it, "best": best,
                   "rng": [list(x) if isinstance(x, tuple) else x for x in rng.getstate()]},
                  open(state_path, "w"))
    log(f"done: best {100 * best['score']:.1f}% at iter {best['iter']}: {fmt(best['theta'])}")


if __name__ == "__main__":
    main()
