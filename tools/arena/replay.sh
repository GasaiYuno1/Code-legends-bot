#!/usr/bin/env bash
# Воспроизводит партии арены движком арбитра: логи в формате replay + таблица драфта.
#
#   tools/arena/replay.sh <клон LegendsOfCodeAndMagic> [games.txt] [каталог логов] [draft.tsv]
set -euo pipefail
REF=${1:?путь к репозиторию арбитра}
GAMES=${2:-data/arena/games.txt}
LOGDIR=${3:-build/arena-logs}
TSV=${4:-build/arena-draft.tsv}

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
CLASSES=$ROOT/build/refcheck-classes
ENGINE=$REF/src/main/java/com/codingame/game/engine
mkdir -p "$CLASSES"

javac -nowarn -encoding UTF-8 -d "$CLASSES" \
  "$ROOT"/tools/refcheck/stubs/com/codingame/game/Player.java \
  "$ROOT"/tools/refcheck/stubs/com/codingame/gameengine/core/MultiplayerGameManager.java \
  "$ENGINE"/Constants.java "$ENGINE"/Keywords.java "$ENGINE"/Card.java "$ENGINE"/CreatureOnBoard.java \
  "$ENGINE"/Action.java "$ENGINE"/ActionResult.java "$ENGINE"/Gamer.java "$ENGINE"/GameState.java \
  "$ENGINE"/DraftPhase.java "$ENGINE"/RefereeParams.java "$ENGINE"/InvalidActionHard.java "$ENGINE"/InvalidActionSoft.java \
  "$ROOT"/tools/refcheck/ArenaReplay.java

cd "$ROOT"
java -cp "$CLASSES:$REF/src/main/resources" ArenaReplay "$GAMES" "$LOGDIR" "$TSV"
