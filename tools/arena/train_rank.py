#!/usr/bin/env python3
"""Обучение оценки листа по решениям экспертов (listwise): для каждого хода среди кандидатов поиска
модель должна дать максимальный балл тому, который выбрал эксперт. Loss = -log softmax(score_expert).
Данные — бинарный файл режима `candidates` тестового проекта: [Count][turnId, isExpert, f1..fN]*.

Запуск: python3 tools/arena/train_rank.py build/cands-top.bin [--hidden 32] [--epochs 30] [--out src/LocmBot/Battle/NetWeights.cs]
"""
import argparse
import math
import sys
from pathlib import Path

import numpy as np


def load(path):
    n_feat = int(np.fromfile(path, dtype=np.int32, count=1)[0])
    raw = np.fromfile(path, dtype=np.float32, offset=4)
    rows = raw.reshape(-1, n_feat + 2)
    turn = rows[:, 0].astype(np.int64)
    expert = rows[:, 1]
    X = rows[:, 2:]
    return turn, expert, X, n_feat


def groups_of(turn):
    # индексы начала/конца групп (строки одного хода идут подряд)
    change = np.flatnonzero(np.diff(turn)) + 1
    starts = np.concatenate([[0], change])
    ends = np.concatenate([change, [len(turn)]])
    return starts, ends


def init(d, hidden, rng):
    if hidden > 0:
        W1 = rng.normal(0, 1.0 / math.sqrt(d), (d, hidden)).astype(np.float32)
        b1 = np.zeros(hidden, dtype=np.float32)
        W2 = rng.normal(0, 1.0 / math.sqrt(hidden), (hidden, 1)).astype(np.float32)
        return [W1, b1, W2, np.zeros(1, dtype=np.float32)]
    return [np.zeros((d, 1), dtype=np.float32), np.zeros(1, dtype=np.float32)]


def forward(params, X):
    if len(params) == 4:
        W1, b1, W2, b2 = params
        H = np.tanh(X @ W1 + b1)
        return H, (H @ W2 + b2)[:, 0]
    W, b = params
    return None, (X @ W + b)[:, 0]


def evaluate(params, X, expert, starts, ends):
    _, s = forward(params, X)
    top1 = top3 = 0
    ll = 0.0
    for a, b in zip(starts, ends):
        sc = s[a:b]
        e = int(np.argmax(expert[a:b]))
        order = np.argsort(-sc)
        rank = int(np.flatnonzero(order == e)[0])
        top1 += rank == 0
        top3 += rank < 3
        m = sc.max()
        ll -= (sc[e] - m) - math.log(np.exp(sc - m).sum())
    n = len(starts)
    return ll / n, top1 / n, top3 / n


def train(params, X, expert, starts, ends, Xv, ev, sv, endv, epochs, lr, l2, rng, group_batch=64):
    m = [np.zeros_like(p) for p in params]
    v = [np.zeros_like(p) for p in params]
    t = 0
    n_groups = len(starts)
    for ep in range(epochs):
        perm = rng.permutation(n_groups)
        for gb in range(0, n_groups, group_batch):
            gidx = perm[gb:gb + group_batch]
            idx = np.concatenate([np.arange(starts[g], ends[g]) for g in gidx])
            Xb = X[idx]
            H, s = forward(params, Xb)
            # градиент listwise loss по баллам: softmax внутри группы минус индикатор эксперта
            g = np.zeros_like(s)
            pos = 0
            for gi in gidx:
                k = ends[gi] - starts[gi]
                sc = s[pos:pos + k]
                p = np.exp(sc - sc.max())
                p /= p.sum()
                g[pos:pos + k] = (p - expert[starts[gi]:ends[gi]]) / len(gidx)
                pos += k
            g = g.astype(np.float32)[:, None]
            if len(params) == 4:
                W1, b1, W2, b2 = params
                gW2 = H.T @ g + l2 * W2
                gb2 = g.sum(0)
                gH = g @ W2.T * (1 - H * H)
                gW1 = Xb.T @ gH + l2 * W1
                gb1 = gH.sum(0)
                grads = [gW1, gb1, gW2, gb2]
            else:
                grads = [Xb.T @ g + l2 * params[0], g.sum(0)]
            t += 1
            for i, gr in enumerate(grads):
                m[i] = 0.9 * m[i] + 0.1 * gr
                v[i] = 0.999 * v[i] + 0.001 * gr * gr
                params[i] -= lr * (m[i] / (1 - 0.9 ** t)) / (np.sqrt(v[i] / (1 - 0.999 ** t)) + 1e-8)
        if ep % 5 == 4 or ep == epochs - 1:
            ll, t1, t3 = evaluate(params, Xv, ev, sv, endv)
            print(f"  epoch {ep + 1}: holdout loss {ll:.3f} top1 {t1:.3f} top3 {t3:.3f}", file=sys.stderr)
    return params


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("data")
    ap.add_argument("--hidden", type=int, default=32)
    ap.add_argument("--epochs", type=int, default=30)
    ap.add_argument("--lr", type=float, default=1e-3)
    ap.add_argument("--l2", type=float, default=1e-5)
    ap.add_argument("--holdout", type=float, default=0.1)
    ap.add_argument("--out", default="src/LocmBot/Battle/NetWeights.cs")
    args = ap.parse_args()

    turn, expert, X, d = load(args.data)
    starts, ends = groups_of(turn)
    rng = np.random.default_rng(3)
    n_groups = len(starts)
    hold = rng.random(n_groups) < args.holdout
    def subset(mask):
        idx = np.concatenate([np.arange(starts[g], ends[g]) for g in np.flatnonzero(mask)])
        Xs, es, ts = X[idx], expert[idx], turn[idx]
        s, e = groups_of(ts)
        return Xs, es, s, e
    Xt, et, st, ent = subset(~hold)
    Xv, ev, sv, env = subset(hold)
    print(f"{len(X)} rows, {n_groups} turns, {d} features; train turns {len(st)}, holdout turns {len(sv)}", file=sys.stderr)
    base = evaluate(init(d, 0, rng), Xv, ev, sv, env)
    print(f"baseline (order of current static eval): top1 {base[1]:.3f} top3 {base[2]:.3f}", file=sys.stderr)

    print("linear ranking:", file=sys.stderr)
    lin = train(init(d, 0, rng), Xt, et, st, ent, Xv, ev, sv, env, min(args.epochs, 15), 3e-3, args.l2, rng)
    print(f"net hidden={args.hidden}:", file=sys.stderr)
    params = train(init(d, args.hidden, rng), Xt, et, st, ent, Xv, ev, sv, env, args.epochs, args.lr, args.l2, rng)
    ll, t1, t3 = evaluate(params, Xv, ev, sv, env)
    lll, lt1, lt3 = evaluate(lin, Xv, ev, sv, env)
    print(f"holdout: linear top1 {lt1:.3f} top3 {lt3:.3f} | net top1 {t1:.3f} top3 {t3:.3f}")

    W1, b1, W2, b2 = params
    vals = []
    for row in W1.T:
        vals.extend(int(round(x * 10000)) for x in row)
    vals.extend(int(round(x * 10000)) for x in b1)
    vals.extend(int(round(x * 10000)) for x in W2[:, 0])
    vals.append(int(round(b2[0] * 10000)))
    packed = ",".join(map(str, vals))
    Path(args.out).write_text("\n".join([
        "// <auto-generated> Сгенерировано tools/arena/train_rank.py (ранжирование ходов экспертов). Не править руками. </auto-generated>",
        "",
        "namespace Locm",
        "{",
        f"    /// <summary>Веса NetEval: {d}→{args.hidden}→1, обучены ранжированием ходов топ-игроков; holdout top1 {t1:.3f}, top3 {t3:.3f} ({n_groups} ходов).</summary>",
        "    public static class NetWeights",
        "    {",
        f"        public const int Hidden = {args.hidden};",
        f"        public const string Packed = \"{packed}\";",
        "    }",
        "}",
        "",
    ]), encoding="utf-8")
    print(f"weights -> {args.out} ({len(packed)} chars)")


if __name__ == "__main__":
    main()
