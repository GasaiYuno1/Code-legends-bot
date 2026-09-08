#!/usr/bin/env python3
"""Обучение весов оценки на исходах партий арены: логистическая регрессия «признаки позиции на начало хода
текущего игрока → выиграл ли он партию». Признаки повторяют слагаемые Evaluator (симметрично: мои минус
противника), поэтому результат можно подставить в Tuning как key=value.

Запуск: python3 tools/arena/learn_eval.py <каталог логов> [--max-files N] [--iters 300] [--l2 1e-3]
"""
import argparse
import glob
import math
import os
import random
import sys

FEATURES = ["atk", "def", "guard", "guarddef", "ward", "lethal", "drain", "breakthrough", "charge",
            "hp", "lowhp", "hand", "deck", "nextdraw"]


def creature_terms(cards, loc):
    f = dict.fromkeys(FEATURES, 0.0)
    for c in cards:
        p = c.split()
        if p[2] != loc:
            continue
        atk, dfn, ab = int(p[5]), int(p[6]), p[7]
        f["atk"] += atk
        f["def"] += dfn
        if "G" in ab:
            f["guard"] += 1
            f["guarddef"] += dfn
        if "W" in ab:
            f["ward"] += 1
        if "L" in ab:
            f["lethal"] += 1
        if "D" in ab:
            f["drain"] += atk
        if "B" in ab:
            f["breakthrough"] += atk
        if "C" in ab:
            f["charge"] += 1
    return f


def parse_log(path):
    lines = [l.rstrip("\n") for l in open(path, encoding="utf-8")]
    head = lines[0].split()
    player, winner = int(head[5]), int(head[13])
    label = 1.0 if winner == player else 0.0
    i = 1
    rows = []
    while i < len(lines):
        if lines[i].startswith("#") or not lines[i].strip():
            i += 1
            continue
        me = lines[i].split()
        opp = lines[i + 1].split()
        oh = lines[i + 2].split()
        i += 3 + int(oh[1])
        n = int(lines[i])
        cards = lines[i + 1:i + 1 + n]
        i += 1 + n
        if i < len(lines) and lines[i].startswith(">"):
            i += 1
        if int(me[1]) == 0 and n == 3:
            continue  # драфт
        if int(me[0]) <= 0 or int(opp[0]) <= 0:
            continue
        mine = creature_terms(cards, "1")
        theirs = creature_terms(cards, "-1")
        x = {}
        for k in ("atk", "def", "guard", "guarddef", "ward", "lethal", "drain", "breakthrough", "charge"):
            x[k] = mine[k] - theirs[k]
        my_hp, opp_hp = int(me[0]), int(opp[0])
        x["hp"] = my_hp - opp_hp
        x["lowhp"] = -max(0, 10 - my_hp) + max(0, 10 - opp_hp)
        x["hand"] = sum(1 for c in cards if c.split()[2] == "0") - int(oh[0])
        x["deck"] = int(me[2]) - int(opp[2])
        x["nextdraw"] = -(int(opp[4]) - 1)     # сколько лишних карт противник доберёт (руны, которые я пробил)
        rows.append(([x[k] for k in FEATURES], label))
    return rows


def fit(rows, iters, l2, lr=0.05):
    n, d = len(rows), len(FEATURES)
    # стандартизация
    means = [sum(r[0][j] for r in rows) / n for j in range(d)]
    stds = [math.sqrt(sum((r[0][j] - means[j]) ** 2 for r in rows) / n) or 1.0 for j in range(d)]
    X = [[(r[0][j] - means[j]) / stds[j] for j in range(d)] for r in rows]
    y = [r[1] for r in rows]
    w = [0.0] * d
    b = 0.0
    for it in range(iters):
        gw = [0.0] * d
        gb = 0.0
        loss = 0.0
        for xi, yi in zip(X, y):
            z = b + sum(wj * xj for wj, xj in zip(w, xi))
            p = 1.0 / (1.0 + math.exp(-z)) if z > -30 else 0.0
            e = p - yi
            loss -= yi * math.log(max(p, 1e-12)) + (1 - yi) * math.log(max(1 - p, 1e-12))
            for j in range(d):
                gw[j] += e * xi[j]
            gb += e
        for j in range(d):
            w[j] -= lr * (gw[j] / n + l2 * w[j])
        b -= lr * gb / n
        if it % 50 == 0 or it == iters - 1:
            print(f"  iter {it}: loss {loss / n:.4f}", file=sys.stderr)
    # веса в исходном масштабе признаков
    raw = [w[j] / stds[j] for j in range(d)]
    acc = 0
    for xi, yi in zip(X, y):
        z = b + sum(wj * xj for wj, xj in zip(w, xi))
        acc += 1 if (z > 0) == (yi > 0.5) else 0
    return raw, b, acc / n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("logs")
    ap.add_argument("--max-files", type=int, default=1200)
    ap.add_argument("--iters", type=int, default=300)
    ap.add_argument("--l2", type=float, default=1e-3)
    args = ap.parse_args()
    files = sorted(glob.glob(os.path.join(args.logs, "*.log")))
    random.Random(1).shuffle(files)
    files = files[: args.max_files]
    rows = []
    for f in files:
        rows.extend(parse_log(f))
    print(f"{len(files)} files, {len(rows)} positions", file=sys.stderr)
    raw, b, acc = fit(rows, args.iters, args.l2)
    print(f"train accuracy {acc:.3f}")
    scale = 1.0 / raw[FEATURES.index("atk")] if raw[FEATURES.index("atk")] else 1.0
    print("weights (raw / normalized to atk=1):")
    for k, v in zip(FEATURES, raw):
        print(f"  {k:<13} {v:+.4f}   {v * scale:+.3f}")
    # перевод в ключи Tuning (guard = вес за штуку, guarddef = за защиту Guard, ward = за штуку, lethal = за штуку,
    # drain/breakthrough = за атаку, charge = за штуку, hp = за 1 HP, lowhp = дополнительно ниже 10, hand = за карту,
    # oppdraw = штраф за лишний добор противника)
    n = dict(zip(FEATURES, [v * scale for v in raw]))
    keys = {
        "atk": n["atk"], "def": n["def"], "guard": n["guard"], "guarddef": n["guarddef"], "ward": n["ward"],
        "lethal": n["lethal"], "drain": n["drain"], "breakthrough": n["breakthrough"], "charge": n["charge"],
        "hp": n["hp"], "lowhp": n["lowhp"], "hand": n["hand"], "oppdraw": n["nextdraw"],
    }
    print("tuning: " + " ".join(f"{k}={v:.3f}" for k, v in keys.items()))


if __name__ == "__main__":
    main()
