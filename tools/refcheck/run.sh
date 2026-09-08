#!/usr/bin/env bash
# Логи случайных партий на настоящем движке арбитра (Java 17+), для сверки симулятора.
#
#   tools/refcheck/run.sh <путь к клону CodinGame/LegendsOfCodeAndMagic> [seed] [игр] [каталог вывода] [NORMAL|LESS_EASY|EASY|VERY_EASY]
#
# Затем: dotnet run --project tests/LocmBot.Tests -- replay <каталог вывода>/*.log
set -euo pipefail
REF=${1:?путь к репозиторию арбитра}
SEED=${2:-1}
GAMES=${3:-10}
OUT=${4:-build/refcheck}
DIFF=${5:-NORMAL}

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
CLASSES=$ROOT/build/refcheck-classes
ENGINE=$REF/src/main/java/com/codingame/game/engine
mkdir -p "$CLASSES" "$OUT"

javac -nowarn -encoding UTF-8 -d "$CLASSES" \
  "$HERE"/stubs/com/codingame/game/Player.java \
  "$HERE"/stubs/com/codingame/gameengine/core/MultiplayerGameManager.java \
  "$ENGINE"/Constants.java "$ENGINE"/Keywords.java "$ENGINE"/Card.java "$ENGINE"/CreatureOnBoard.java \
  "$ENGINE"/Action.java "$ENGINE"/ActionResult.java "$ENGINE"/Gamer.java "$ENGINE"/GameState.java \
  "$ENGINE"/DraftPhase.java "$ENGINE"/RefereeParams.java "$ENGINE"/InvalidActionHard.java "$ENGINE"/InvalidActionSoft.java \
  "$HERE"/RandomGames.java

java -cp "$CLASSES:$REF/src/main/resources" RandomGames "$SEED" "$GAMES" "$OUT" "$DIFF"
