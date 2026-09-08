#!/usr/bin/env python3
"""Скачивает партии арены CodinGame (LOCM) через внутренний API и пишет компактные записи.

Для каждой партии сохраняются сиды арбитра (по ним движок воспроизводит драфт и колоды),
пики обоих игроков (baseId), действия по ходам и победитель. Формат data/arena/games.txt —
текстовый, чтобы его без библиотек читал tools/refcheck/ArenaReplay.java:

    game <gameId> winner <0|1> agents <agentId0> <agentId1> leagues <l0> <l1> names <n0> <n1>
    seeds <draftChoicesSeed> <shufflePlayer0Seed> <shufflePlayer1Seed>
    picks0 <30 baseId>
    picks1 <30 baseId>
    turn <0|1> <действия через ; или PASS>
    ...
    (пустая строка)

Запуск: python3 tools/arena/fetch.py [--league 6] [--agents 30] [--max-games 300] [--out data/arena/games.txt]
        python3 tools/arena/fetch.py --handle <publicHandle из URL профиля> --out build/user/games.txt   # бои одного игрока
Повторный запуск дописывает только новые партии. Лиги: 6 Legend, 5 Gold, 4 Silver, 3 Bronze.
"""
import argparse
import json
import re
import sys
import time
import urllib.request
from pathlib import Path

API = "https://www.codingame.com/services/"
PUZZLE = "legends-of-code-magic"
PICK_RE = re.compile(r"Player \$(\d) chose .*?\(#(\d+)\)")
ACTION_RE = re.compile(r"Player \$(\d) performed action: (.+)")


def post(path, body, retries=3):
    data = json.dumps(body).encode()
    for attempt in range(retries):
        try:
            req = urllib.request.Request(API + path, data=data, headers={
                "Content-Type": "application/json",
                "User-Agent": "locm-bot arena stats (personal research)",
            })
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode())
        except Exception as e:  # noqa: BLE001
            if attempt == retries - 1:
                raise
            time.sleep(2 * (attempt + 1))


def leaderboard():
    return post("Leaderboards/getFilteredPuzzleLeaderboard",
                [PUZZLE, None, "global", {"active": False, "column": "", "filter": ""}])["users"]


def last_battles(agent_id):
    return post("gamesPlayersRanking/findLastBattlesByAgentId", [agent_id, None]) or []


def game_result(game_id):
    return post("gameResult/findByGameId", [game_id, None])


def parse_seeds(referee_input):
    seeds = {}
    for line in (referee_input or "").splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            seeds[k.strip()] = v.strip()
    return seeds


def extract(game, leagues):
    """Компактная запись партии или None, если её нельзя воспроизвести."""
    seeds = parse_seeds(game.get("refereeInput"))
    for k in ("draftChoicesSeed", "shufflePlayer0Seed", "shufflePlayer1Seed"):
        if k not in seeds:
            return None
    agents = sorted(game["agents"], key=lambda a: a["index"])
    for a in agents:
        a["agentId"] = a.get("agentId", -1)
    if len(agents) != 2:
        return None
    scores = game.get("scores") or []
    if len(scores) != 2 or scores[0] == scores[1]:
        return None
    winner = 0 if scores[0] > scores[1] else 1

    picks = [[], []]
    turns = []          # [player, [actions]]
    in_battle = False
    for f in game["frames"]:
        summary = f.get("summary") or ""
        agent = f.get("agentId", -1)
        for m in PICK_RE.finditer(summary):
            picks[int(m.group(1))].append(int(m.group(2)))
        if len(picks[0]) == 30 and len(picks[1]) == 30 and not in_battle and agent in (0, 1) and "chose" not in summary:
            in_battle = True
        if not in_battle or agent not in (0, 1):
            continue
        if not turns or turns[-1][0] != agent:
            turns.append([agent, []])
        for m in ACTION_RE.finditer(summary):
            if int(m.group(1)) == agent:
                turns[-1][1].append(m.group(2).strip())
    if len(picks[0]) != 30 or len(picks[1]) != 30 or not turns:
        return None
    return {
        "gameId": game["gameId"],
        "winner": winner,
        "agents": [a["agentId"] for a in agents],
        # у Босса лиги нет профиля codingamer
        "names": [((a.get("codingamer") or {}).get("pseudo") or "Boss").replace(" ", "_") for a in agents],
        "leagues": [leagues.get(a["agentId"], -1) for a in agents],
        "seeds": seeds,
        "picks": picks,
        "turns": turns,
    }


def format_record(r):
    lines = [
        f"game {r['gameId']} winner {r['winner']} agents {r['agents'][0]} {r['agents'][1]} "
        f"leagues {r['leagues'][0]} {r['leagues'][1]} names {r['names'][0]} {r['names'][1]}",
        f"seeds {r['seeds']['draftChoicesSeed']} {r['seeds']['shufflePlayer0Seed']} {r['seeds']['shufflePlayer1Seed']}",
        "picks0 " + " ".join(map(str, r["picks"][0])),
        "picks1 " + " ".join(map(str, r["picks"][1])),
    ]
    for p, actions in r["turns"]:
        lines.append(f"turn {p} " + (";".join(actions) if actions else "PASS"))
    return "\n".join(lines) + "\n\n"


def known_games(path):
    ids = set()
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.startswith("game "):
                ids.add(int(line.split()[1]))
    return ids


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--league", type=int, default=6)
    ap.add_argument("--agents", type=int, default=30)
    ap.add_argument("--max-games", type=int, default=300)
    ap.add_argument("--out", default="data/arena/games.txt")
    ap.add_argument("--delay", type=float, default=0.15)
    ap.add_argument("--handle", default=None, help="публичный handle игрока (из URL профиля): скачать его последние бои вместо топа лиги")
    args = ap.parse_args()

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    have = known_games(out)

    users = leaderboard()
    leagues = {u["agentId"]: u["league"]["divisionIndex"] for u in users}
    if args.handle:
        picked = [u for u in users if (u.get("codingamer") or {}).get("publicHandle") == args.handle]
        if not picked:
            print("handle not found in the top-1000 leaderboard", file=sys.stderr)
            return
        u = picked[0]
        print(f"{u['pseudo']}: rank {u['rank']}, league {u['league']['divisionIndex']} #{u['localRank']}, agent {u['agentId']}", file=sys.stderr)
    else:
        picked = [u for u in users if u["league"]["divisionIndex"] == args.league][: args.agents]
        print(f"leaderboard: {len(users)} users, league {args.league}: {sum(1 for u in users if u['league']['divisionIndex'] == args.league)}, using {len(picked)} agents", file=sys.stderr)

    game_ids = []
    seen = set(have)
    for u in picked:
        for b in last_battles(u["agentId"]):
            gid = b.get("gameId")
            if gid and b.get("done") and gid not in seen:
                seen.add(gid)
                game_ids.append(gid)
        time.sleep(args.delay)
    print(f"new games to fetch: {len(game_ids)} (already have {len(have)})", file=sys.stderr)

    written = skipped = 0
    with out.open("a", encoding="utf-8") as f:
        for gid in game_ids[: args.max_games]:
            try:
                rec = extract(game_result(gid), leagues)
            except Exception as e:  # noqa: BLE001
                print(f"game {gid}: error {e}", file=sys.stderr)
                skipped += 1
                continue
            if rec is None:
                skipped += 1
            else:
                f.write(format_record(rec))
                f.flush()
                written += 1
            if (written + skipped) % 25 == 0:
                print(f"  {written} written, {skipped} skipped", file=sys.stderr)
            time.sleep(args.delay)
    print(f"done: {written} written, {skipped} skipped -> {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
