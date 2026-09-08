#!/usr/bin/env python3
"""Обучение NetEval: MLP Count→Hidden(tanh)→1 (логит P(win)) на признаках из режима `features` тестового проекта.

Разбиение train/holdout — по файлам партий, чтобы позиции одной партии не попадали в обе части.
Печатает logloss/точность на holdout для логистической регрессии (Hidden=0) и сети, пишет NetWeights.cs.

Запуск: python3 tools/arena/train_net.py build/features-legend.tsv [ещё файлы...] [--hidden 32] [--epochs 40]
        [--out src/LocmBot/Battle/NetWeights.cs]
"""
import argparse
import math
import sys
from pathlib import Path

import numpy as np


def load(paths):
    games, ys, xs = [], [], []
    for p in paths:
        with open(p, encoding="utf-8") as f:
            for line in f:
                parts = line.rstrip("\n").split("\t")
                games.append(parts[0])
                ys.append(float(parts[1]))
                xs.append([float(v) for v in parts[2:]])
    return np.array(games), np.array(ys, dtype=np.float32), np.array(xs, dtype=np.float32)


def train(X, y, Xv, yv, hidden, epochs, lr, l2, batch, seed=1):
    rng = np.random.default_rng(seed)
    n, d = X.shape
    if hidden > 0:
        W1 = rng.normal(0, 1.0 / math.sqrt(d), (d, hidden)).astype(np.float32)
        b1 = np.zeros(hidden, dtype=np.float32)
        W2 = rng.normal(0, 1.0 / math.sqrt(hidden), (hidden, 1)).astype(np.float32)
    else:
        W1 = b1 = None
        W2 = np.zeros((d, 1), dtype=np.float32)
    b2 = np.zeros(1, dtype=np.float32)
    params = [p for p in (W1, b1, W2, b2) if p is not None]
    m = [np.zeros_like(p) for p in params]
    v = [np.zeros_like(p) for p in params]
    t = 0

    def forward(Xb):
        if hidden > 0:
            H = np.tanh(Xb @ W1 + b1)
            return H, H @ W2 + b2
        return None, Xb @ W2 + b2

    def evaluate(Xe, ye):
        _, z = forward(Xe)
        p = 1 / (1 + np.exp(-z[:, 0]))
        p = np.clip(p, 1e-7, 1 - 1e-7)
        ll = -np.mean(ye * np.log(p) + (1 - ye) * np.log(1 - p))
        acc = np.mean((p > 0.5) == (ye > 0.5))
        return ll, acc

    for ep in range(epochs):
        idx = rng.permutation(n)
        for start in range(0, n, batch):
            bi = idx[start:start + batch]
            Xb, yb = X[bi], y[bi]
            H, z = forward(Xb)
            p = 1 / (1 + np.exp(-z[:, 0]))
            g = ((p - yb) / len(bi)).astype(np.float32)[:, None]   # dL/dz
            grads = []
            if hidden > 0:
                gW2 = H.T @ g + l2 * W2
                gb2 = g.sum(0)
                gH = g @ W2.T * (1 - H * H)
                gW1 = Xb.T @ gH + l2 * W1
                gb1 = gH.sum(0)
                grads = [gW1, gb1, gW2, gb2]
            else:
                grads = [Xb.T @ g + l2 * W2, g.sum(0)]
            t += 1
            for i, (pmt, gr) in enumerate(zip(params, grads)):
                m[i] = 0.9 * m[i] + 0.1 * gr
                v[i] = 0.999 * v[i] + 0.001 * gr * gr
                mh = m[i] / (1 - 0.9 ** t)
                vh = v[i] / (1 - 0.999 ** t)
                pmt -= lr * mh / (np.sqrt(vh) + 1e-8)
        if ep % 10 == 9 or ep == epochs - 1:
            ll, acc = evaluate(Xv, yv)
            print(f"  epoch {ep + 1}: holdout logloss {ll:.4f} acc {acc:.3f}", file=sys.stderr)
    return (W1, b1, W2, b2), evaluate(Xv, yv)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--hidden", type=int, default=32)
    ap.add_argument("--epochs", type=int, default=40)
    ap.add_argument("--lr", type=float, default=1e-3)
    ap.add_argument("--l2", type=float, default=1e-5)
    ap.add_argument("--batch", type=int, default=256)
    ap.add_argument("--holdout", type=float, default=0.1)
    ap.add_argument("--out", default="src/LocmBot/Battle/NetWeights.cs")
    args = ap.parse_args()

    games, y, X = load(args.files)
    uniq = np.unique(games)
    rng = np.random.default_rng(7)
    hold = set(rng.choice(uniq, int(len(uniq) * args.holdout), replace=False).tolist())
    mask = np.array([g in hold for g in games])
    Xt, yt, Xv, yv = X[~mask], y[~mask], X[mask], y[mask]
    print(f"{len(X)} rows, {X.shape[1]} features, train {len(Xt)} / holdout {len(Xv)}; base rate {y.mean():.3f}", file=sys.stderr)

    print("logistic regression:", file=sys.stderr)
    _, (ll0, acc0) = train(Xt, yt, Xv, yv, 0, min(args.epochs, 20), 3e-3, args.l2, args.batch)
    print(f"net hidden={args.hidden}:", file=sys.stderr)
    (W1, b1, W2, b2), (ll1, acc1) = train(Xt, yt, Xv, yv, args.hidden, args.epochs, args.lr, args.l2, args.batch)
    print(f"holdout: logistic logloss {ll0:.4f} acc {acc0:.3f} | net logloss {ll1:.4f} acc {acc1:.3f}")

    vals = []
    for row in W1.T:          # [hidden][Count] построчно
        vals.extend(int(round(x * 10000)) for x in row)
    vals.extend(int(round(x * 10000)) for x in b1)
    vals.extend(int(round(x * 10000)) for x in W2[:, 0])
    vals.append(int(round(b2[0] * 10000)))
    packed = ",".join(map(str, vals))
    lines = [
        "// <auto-generated> Сгенерировано tools/arena/train_net.py. Не править руками. </auto-generated>",
        "",
        "namespace Locm",
        "{",
        f"    /// <summary>Веса NetEval: {X.shape[1]}→{args.hidden}→1, holdout logloss {ll1:.4f}, точность {acc1:.3f} ({len(X)} позиций).</summary>",
        "    public static class NetWeights",
        "    {",
        f"        public const int Hidden = {args.hidden};",
        f"        public const string Packed = \"{packed}\";",
        "    }",
        "}",
        "",
    ]
    Path(args.out).write_text("\n".join(lines), encoding="utf-8")
    print(f"weights -> {args.out} ({len(packed)} chars)")


if __name__ == "__main__":
    main()
