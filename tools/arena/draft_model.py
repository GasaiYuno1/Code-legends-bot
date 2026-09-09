#!/usr/bin/env python3
"""Модель драфта с учётом состава колоды: логит-регрессия «победа ~ карты + агрегаты колоды» по пикам self-play.

Агрегаты: число карт по стоимости 0..7+ и их квадраты, число предметов и квадрат, число карт с G/W/L/C/D/B,
число карт с добором. Пик = максимум прироста логита при добавлении карты к уже набранной колоде.

  python3 tools/arena/draft_model.py build/selfplay-picks-200k.txt            # сравнение с аддитивной моделью на holdout
  python3 tools/arena/draft_model.py build/selfplay-picks-200k.txt --write    # + PackedDraftAgg в CardTable.cs
"""
import argparse
import re
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent.parent
N_CARDS = 160
PICKS_RE = re.compile(r"^picks(\d) bot ([AB]) won ([01]) :((?: \d+){30})$")
AGG_NAMES = (["cost%d" % b for b in range(8)] + ["cost%d_sq" % b for b in range(8)]
             + ["items", "items_sq", "guard", "ward", "lethal", "charge", "drain", "breakthrough", "draw"])


def load_cards():
    cards = {}
    for line in (ROOT / "referee" / "cardlist.txt").read_text(encoding="utf-8").splitlines():
        p = [x.strip() for x in line.split(";")]
        if len(p) < 10 or not p[0].isdigit():
            continue
        cards[int(p[0])] = dict(type=p[2], cost=int(p[3]), abil=p[6], draw=int(p[9]))
    return cards


def aggregates(counts, cards):
    """counts: массив 160 (число копий). Возвращает вектор агрегатов."""
    f = np.zeros(len(AGG_NAMES))
    for c in np.nonzero(counts)[0]:
        n = counts[c]
        card = cards[c + 1]
        b = min(card["cost"], 7)
        f[b] += n
        if card["type"] != "creature":
            f[16] += n
        a = card["abil"]
        f[18] += n * ("G" in a)
        f[19] += n * ("W" in a)
        f[20] += n * ("L" in a)
        f[21] += n * ("C" in a)
        f[22] += n * ("D" in a)
        f[23] += n * ("B" in a)
        f[24] += n * (card["draw"] > 0)
    for b in range(8):
        f[8 + b] = f[b] ** 2
    f[17] = f[16] ** 2
    return f


def parse(path, cards):
    X, A, y, side = [], [], [], []
    for line in Path(path).read_text().splitlines():
        m = PICKS_RE.match(line.strip())
        if not m:
            continue
        row = np.zeros(N_CARDS)
        for c in (int(x) for x in m.group(4).split()):
            if 1 <= c <= N_CARDS:
                row[c - 1] += 1
        X.append(row)
        A.append(aggregates(row, cards))
        y.append(int(m.group(3)))
        side.append(int(m.group(1)))
    return np.array(X), np.array(A), np.array(y, float), np.array(side, float)


def fit(F, y, side, l2=2.0, iters=4000, lr=0.5):
    n, d = F.shape
    mu = F.mean(axis=0)
    sd = F.std(axis=0) + 1e-9
    Z = (F - mu) / sd
    w = np.zeros(d)
    b = 0.0
    bs = 0.0
    for _ in range(iters):
        z = b + bs * side + Z @ w
        p = 1 / (1 + np.exp(-z))
        g = p - y
        w -= lr * (Z.T @ g / n + l2 * w / n)
        b -= lr * g.mean()
        bs -= lr * (g * side).mean()
    return w / sd, b - (w / sd) @ mu, bs


def logloss(F, y, side, w, b, bs):
    z = b + bs * side + F @ w
    p = 1 / (1 + np.exp(-z))
    return -np.mean(y * np.log(p + 1e-12) + (1 - y) * np.log(1 - p + 1e-12)), np.mean((p > 0.5) == (y > 0.5))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("picks")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--table", default=str(ROOT / "src" / "LocmBot" / "Draft" / "CardTable.cs"))
    ap.add_argument("--l2", type=float, default=2.0)
    args = ap.parse_args()
    cards = load_cards()
    X, A, y, side = parse(args.picks, cards)
    n = len(y)
    rng = np.random.default_rng(1)
    idx = rng.permutation(n)
    tr, te = idx[: int(0.85 * n)], idx[int(0.85 * n):]
    print(f"{n} decks; train {len(tr)}, holdout {len(te)}")
    # аддитивная
    w1, b1, s1 = fit(X[tr], y[tr], side[tr], args.l2)
    ll1, acc1 = logloss(X[te], y[te], side[te], w1, b1, s1)
    print(f"additive:        holdout logloss {ll1:.4f}, accuracy {acc1:.3f}")
    # с агрегатами
    F = np.hstack([X, A])
    w2, b2, s2 = fit(F[tr], y[tr], side[tr], args.l2)
    ll2, acc2 = logloss(F[te], y[te], side[te], w2, b2, s2)
    print(f"with aggregates: holdout logloss {ll2:.4f}, accuracy {acc2:.3f}")
    wa = w2[N_CARDS:]
    print("aggregate weights:")
    for name, v in zip(AGG_NAMES, wa):
        print(f"  {name:14s} {v:+.4f}")
    # кривая: оптимум по стоимости = -w_lin / (2 w_sq)
    print("implied optimal counts per cost bucket:", " ".join(
        f"{b}:{(-wa[b] / (2 * wa[8 + b]) if wa[8 + b] < 0 else float('inf')):.1f}" for b in range(8)))
    if args.write:
        # полная модель: карты (сотые доли) + агрегаты (десятитысячные)
        text = Path(args.table).read_text(encoding="utf-8")
        cards_packed = ",".join(str(int(round(v * 100))) for v in w2[:N_CARDS])
        agg_packed = ",".join(str(int(round(v * 10000))) for v in wa)
        text, n1 = re.subn(r'PackedSelf = "[^"]*"', f'PackedSelf = "{cards_packed}"', text)
        if 'PackedDraftAgg' in text:
            text, n2 = re.subn(r'PackedDraftAgg = "[^"]*"', f'PackedDraftAgg = "{agg_packed}"', text)
        else:
            text = text.replace('        public static double[] Rating => UseWinAdjusted ? _win : _pick;',
                                '        /// <summary>Веса агрегатов колоды (tools/arena/draft_model.py): cost0..7, их квадраты, items, items², G, W, L, C, D, B, draw; десятитысячные.</summary>\n'
                                f'        private const string PackedDraftAgg = "{agg_packed}";\n'
                                '        public static readonly double[] DraftAgg = UnpackScaled(PackedDraftAgg, 10000.0);\n\n'
                                '        public static double[] Rating => UseWinAdjusted ? _win : _pick;', 1)
            n2 = 1
        if n1 != 1 or n2 != 1:
            raise RuntimeError("table markers not found")
        Path(args.table).write_text(text, encoding="utf-8")
        print(f"PackedSelf + PackedDraftAgg -> {args.table}")


if __name__ == "__main__":
    sys.exit(main())
