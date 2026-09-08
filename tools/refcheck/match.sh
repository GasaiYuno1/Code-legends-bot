#!/usr/bin/env bash
# Матчи бот против бота на настоящем движке арбитра (мини-арбитр BotMatch.java, Java 17+).
#
#   tools/refcheck/match.sh <клон LegendsOfCodeAndMagic> <игр> <seed> "<команда бота A>" "<команда бота B>" [каталог логов]
#
# Команда бота — как на CodinGame, stdin/stdout; специальное имя random — встроенный случайный агент.
# Пример: tools/refcheck/match.sh ../legendsofcodeandmagic 20 1 "dotnet src/LocmBot/bin/Release/net8.0/LocmBot.dll" random build/match
set -euo pipefail
REF=${1:?путь к репозиторию арбитра}
GAMES=${2:-10}
SEED=${3:-1}
CMD_A=${4:?команда бота A}
CMD_B=${5:?команда бота B}
LOGDIR=${6:-}

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
CLASSES=$ROOT/build/refcheck-classes
ENGINE=$REF/src/main/java/com/codingame/game/engine
mkdir -p "$CLASSES"

javac -nowarn -encoding UTF-8 -d "$CLASSES" \
  "$HERE"/stubs/com/codingame/game/Player.java \
  "$HERE"/stubs/com/codingame/gameengine/core/MultiplayerGameManager.java \
  "$ENGINE"/Constants.java "$ENGINE"/Keywords.java "$ENGINE"/Card.java "$ENGINE"/CreatureOnBoard.java \
  "$ENGINE"/Action.java "$ENGINE"/ActionResult.java "$ENGINE"/Gamer.java "$ENGINE"/GameState.java \
  "$ENGINE"/DraftPhase.java "$ENGINE"/RefereeParams.java "$ENGINE"/InvalidActionHard.java "$ENGINE"/InvalidActionSoft.java \
  "$HERE"/BotMatch.java

cd "$ROOT"
java -cp "$CLASSES:$REF/src/main/resources" BotMatch "$GAMES" "$SEED" "$CMD_A" "$CMD_B" $LOGDIR
