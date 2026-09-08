#!/usr/bin/env python3
"""Статистика карт из self-play: ценность карты по исходам партий, а не по тому, что берут Legend-игроки.

Бот драфтит с ε-разведкой (draftexplore=ε: с этой вероятностью пик случайный), мини-арбитр BotMatch печатает пики
обоих игроков и исход; логит-регрессия «победа ~ состав колоды (число копий карты) + сторона» с L2 даёт вклад
каждой карты. Результат: data/arena/selfplay_cards.tsv и константа PackedSelf в src/LocmBot/Draft/CardTable.cs
(в боте включается selfw=<вес>: рейтинг = таблица пиков + selfw·вклад).

  python3 tools/arena/selfplay_cards.py --games 20000 --workers 4 --explore 0.25       # сыграть и обучить
  python3 tools/arena/selfplay_cards.py --fit build/selfplay-picks.txt                # только регрессия по сохранённым пикам
"""
import argparse
import math
import re
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent.parent
BOT = ROOT / "build" / "bot-ref2" / "LocmBot.dll"
CLASSES = ROOT / "build" / "refcheck-classes"
RESOURCES = Path("/home/user/codingame/legendsofcodeandmagic/src/main/resources")
PICKS_RE = re.compile(r"^picks(\d) bot ([AB]) won ([01]) :((?: \d+){30})$")
N_CARDS = 160


def play(games, workers, seed, bot, extra):
    per = [games // workers + (1 if i < games % workers else 0) for i in range(workers)]
    cmd = f"dotnet {bot} warmup=0 {extra}".strip()
    procs = []
    for i, n in enumerate(per):
        c = ["java", "-cp", f"{CLASSES}:{RESOURCES}", "BotMatch", str(n), str(seed * 1000 + i), cmd, cmd]
        procs.append(subprocess.Popen(c, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True))
    lines = []
    for p in procs:
        out, _ = p.communicate()
        for line in out.splitlines():
            if line.startswith("picks"):
                lines.append(line)
    return lines


def parse(lines):
    X, y, side = [], [], []
    for line in lines:
        m = PICKS_RE.match(line.strip())
        if not m:
            continue
        player = int(m.group(1))
        won = int(m.group(3))
        cards = [int(x) for x in m.group(4).split()]
        row = np.zeros(N_CARDS)
        for c in cards:
            if 1 <= c <= N_CARDS:
                row[c - 1] += 1
        X.append(row)
        y.append(won)
        side.append(player)
    return np.array(X), np.array(y, dtype=float), np.array(side, dtype=float)


def fit(X, y, side, l2=1.0, iters=3000, lr=0.05):
    """Логит-регрессия: P(win) = σ(b + b_side·side + Σ w_c·count_c); w — вклад карты. Градиентный спуск с L2 на w."""
    n, d = X.shape
    w = np.zeros(d)
    b = 0.0
    bs = 0.0
    for _ in range(iters):
        z = b + bs * side + X @ w
        p = 1.0 / (1.0 + np.exp(-z))
        g = p - y
        gw = X.T @ g / n + l2 * w / n
        gb = g.mean()
        gbs = (g * side).mean()
        w -= lr * gw * 10
        b -= lr * gb
        bs -= lr * gbs
    z = b + bs * side + X @ w
    p = 1.0 / (1.0 + np.exp(-z))
    ll = -np.mean(y * np.log(p + 1e-12) + (1 - y) * np.log(1 - p + 1e-12))
    acc = np.mean((p > 0.5) == (y > 0.5))
    return w, b, bs, ll, acc


def write_table(w, table):
    text = table.read_text(encoding="utf-8")
    values = ",".join(str(int(round(v * 100))) for v in w)
    new, n = re.subn(r'PackedSelf = "[^"]*"', f'PackedSelf = "{values}"', text)
    if n != 1:
        raise RuntimeError("PackedSelf not found in " + str(table))
    table.write_text(new, encoding="utf-8")


def main():
    global CLASSES
    ap = argparse.ArgumentParser()
    ap.add_argument("--games", type=int, default=20000)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--explore", type=float, default=0.25)
    ap.add_argument("--seed", type=int, default=5)
    ap.add_argument("--bot", default=str(BOT))
    ap.add_argument("--extra", default="", help="дополнительные key=value обоим ботам")
    ap.add_argument("--picks", default=str(ROOT / "build" / "selfplay-picks.txt"), help="куда дописывать строки picks")
    ap.add_argument("--fit", help="только регрессия по этому файлу пиков")
    ap.add_argument("--l2", type=float, default=2.0)
    ap.add_argument("--out", default=str(ROOT / "data" / "arena" / "selfplay_cards.tsv"))
    ap.add_argument("--table", default=str(ROOT / "src" / "LocmBot" / "Draft" / "CardTable.cs"))
    ap.add_argument("--no-table", action="store_true")
    ap.add_argument("--classes", default=str(CLASSES), help="каталог классов BotMatch")
    args = ap.parse_args()
    CLASSES = Path(args.classes)

    if args.fit:
        lines = Path(args.fit).read_text().splitlines()
    else:
        t0 = time.time()
        lines = play(args.games, args.workers, args.seed, args.bot, f"draftexplore={args.explore} {args.extra}")
        Path(args.picks).parent.mkdir(parents=True, exist_ok=True)
        with open(args.picks, "a") as f:
            f.write("\n".join(lines) + "\n")
        print(f"{len(lines)} decks in {time.time() - t0:.0f} s -> {args.picks}")
        lines = Path(args.picks).read_text().splitlines()

    X, y, side = parse(lines)
    print(f"{len(y)} decks, win rate {y.mean():.3f}, first player wins {y[side == 0].mean():.3f}")
    w, b, bs, ll, acc = fit(X, y, side, l2=args.l2)
    print(f"fit: logloss {ll:.4f}, accuracy {acc:.3f}, intercept {b:.3f}, second-player {bs:.3f}")
    counts = X.sum(axis=0)
    # сравнение с таблицей пиков
    m = re.search(r'Packed = "([^"]+)"', Path(args.table).read_text(encoding="utf-8"))
    pick = np.array([int(x) / 100 for x in m.group(1).split(",")]) if m else np.zeros(N_CARDS)
    corr = np.corrcoef(w, pick)[0, 1] if m else float("nan")
    print(f"correlation with Legend pick rating: {corr:.3f}; w std {w.std():.3f}, pick std {pick.std():.3f}")
    order = np.argsort(-w)
    with open(args.out, "w") as f:
        f.write("baseId\tdecks\tself_w\tpick_rating\n")
        for i in range(N_CARDS):
            f.write(f"{i + 1}\t{int(counts[i])}\t{w[i]:.4f}\t{pick[i]:.2f}\n")
    print("top 10 by self-play:", " ".join(f"{i + 1}({w[i]:+.2f}/{pick[i]:+.1f})" for i in order[:10]))
    print("bottom 10:", " ".join(f"{i + 1}({w[i]:+.2f}/{pick[i]:+.1f})" for i in order[-10:]))
    if not args.no_table:
        write_table(w, Path(args.table))
        print(f"PackedSelf -> {args.table}")


if __name__ == "__main__":
    sys.exit(main())
