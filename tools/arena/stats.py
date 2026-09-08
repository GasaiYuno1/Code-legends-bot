#!/usr/bin/env python3
"""Статистика драфта по партиям арены (build/arena-draft.tsv от ArenaReplay) и таблица рейтинга карт.

Модель Брэдли–Терри для троек: сила карты s_c, вероятность выбрать c из {a,b,c} = s_c/(s_a+s_b+s_c);
подгонка MM-итерациями (Hunter 2004). Дополнительно: частота пика и винрейт колод с картой.

Запуск: python3 tools/arena/stats.py [draft.tsv] [--league 6] [--table src/LocmBot/Draft/CardTable.cs] [--out data/arena/card_stats.tsv]
"""
import argparse
import csv
import math
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools"))


def load_names():
    names = {}
    for line in (ROOT / "referee" / "cardlist.txt").read_text(encoding="utf-8").splitlines():
        p = [x.strip() for x in line.split(";")]
        if len(p) > 2 and p[0].isdigit():
            names[int(p[0])] = (p[1], p[2], int(p[3]))
    return names


def bradley_terry(triples, n_iter=200):
    """triples: список (offered[3], chosen). Возвращает силы s[card] (среднее геометрическое = 1)."""
    cards = sorted({c for t, _ in triples for c in t})
    s = {c: 1.0 for c in cards}
    wins = defaultdict(int)
    for _, ch in triples:
        wins[ch] += 1
    for _ in range(n_iter):
        denom = defaultdict(float)
        for t, _ in triples:
            inv = 1.0 / sum(s[c] for c in t)
            for c in t:
                denom[c] += inv
        new = {}
        for c in cards:
            # сглаживание: +1 победа и +3 «средних» предложения, чтобы редкие карты не улетали
            new[c] = (wins[c] + 1.0) / (denom[c] + 1.0)
        g = math.exp(sum(math.log(v) for v in new.values()) / len(new))
        s = {c: v / g for c, v in new.items()}
    return s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv", nargs="?", default="build/arena-draft.tsv")
    ap.add_argument("--league", type=int, default=6, help="учитывать пики игроков этой лиги и выше")
    ap.add_argument("--table", default="src/LocmBot/Draft/CardTable.cs")
    ap.add_argument("--out", default="data/arena/card_stats.tsv")
    ap.add_argument("--scale", type=float, default=2.0, help="std рейтинга в таблице")
    args = ap.parse_args()

    names = load_names()
    triples = []
    offers = defaultdict(int)
    picks = defaultdict(int)
    deck_games = defaultdict(int)
    deck_wins = defaultdict(int)
    games = set()
    decks = defaultdict(set)   # (gameId, player) -> cards
    won = {}
    with open(args.tsv, encoding="utf-8") as f:
        for row in csv.DictReader(f, delimiter="\t"):
            if int(row["league"]) < args.league:
                continue
            offered = (int(row["offered1"]), int(row["offered2"]), int(row["offered3"]))
            chosen = int(row["chosen"])
            triples.append((offered, chosen))
            for c in set(offered):
                offers[c] += 1
            picks[chosen] += 1
            key = (row["gameId"], row["player"])
            decks[key].add(chosen)
            won[key] = int(row["won"])
            games.add(row["gameId"])
    for key, cards in decks.items():
        for c in cards:
            deck_games[c] += 1
            deck_wins[c] += won[key]
    if not triples:
        print("no picks", file=sys.stderr)
        return 1

    s = bradley_terry(triples)
    logs = {c: math.log(v) for c, v in s.items()}
    mean = sum(logs.values()) / len(logs)
    std = math.sqrt(sum((v - mean) ** 2 for v in logs.values()) / len(logs)) or 1.0
    rating = {c: (v - mean) / std * args.scale for c, v in logs.items()}

    rows = []
    for c in sorted(names):
        name, typ, cost = names[c]
        rows.append({
            "baseId": c, "name": name, "type": typ, "cost": cost,
            "rating": round(rating.get(c, 0.0), 3),
            "offers": offers[c], "picks": picks[c],
            "pickRate": round(picks[c] / offers[c], 3) if offers[c] else "",
            "deckGames": deck_games[c],
            "deckWinRate": round(deck_wins[c] / deck_games[c], 3) if deck_games[c] else "",
        })
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()), delimiter="\t")
        w.writeheader()
        w.writerows(rows)

    ranked = sorted(rows, key=lambda r: -r["rating"])
    print(f"{len(games)} games, {len(triples)} picks, league >= {args.league}")
    print("top 15:")
    for r in ranked[:15]:
        print(f"  {r['baseId']:>3} {r['name']:<24} {r['type']:<9} cost {r['cost']} rating {r['rating']:+.2f} pick {r['pickRate']} win {r['deckWinRate']} (n={r['deckGames']})")
    print("bottom 8:")
    for r in ranked[-8:]:
        print(f"  {r['baseId']:>3} {r['name']:<24} {r['type']:<9} cost {r['cost']} rating {r['rating']:+.2f} pick {r['pickRate']} win {r['deckWinRate']} (n={r['deckGames']})")

    if args.table:
        # компактно: рейтинги как целые сотые в одной строке через запятую (склейка CodinGame ограничена ~100k символов)
        values = ",".join(str(int(round(rating.get(c, 0.0) * 100))) for c in sorted(names))
        lines = [
            "// <auto-generated> Сгенерировано tools/arena/stats.py по пикам Legend-игроков арены. Не править руками. </auto-generated>",
            "",
            "namespace Locm",
            "{",
            "    /// <summary>Рейтинг карт по драфту сильных игроков (Брэдли–Терри по предложенным тройкам), индекс — baseId.</summary>",
            "    public static class CardTable",
            "    {",
            f"        public const int Games = {len(games)};",
            f"        public const int Picks = {len(triples)};",
            "",
            "        // сотые доли рейтинга для baseId 1..160",
            f"        private const string Packed = \"{values}\";",
            "",
            "        public static readonly double[] Rating = Unpack();",
            "",
            "        private static double[] Unpack()",
            "        {",
            "            var parts = Packed.Split(',');",
            "            var r = new double[parts.Length + 1];",
            "            for (int i = 0; i < parts.Length; i++) r[i + 1] = int.Parse(parts[i]) / 100.0;",
            "            return r;",
            "        }",
            "    }",
            "}",
            "",
        ]
        Path(args.table).write_text("\n".join(lines), encoding="utf-8")
        print(f"table -> {args.table}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
