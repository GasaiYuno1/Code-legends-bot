# locm-bot

Бот для CodinGame — Legends of Code & Magic.

- `docs/01-rules-and-options.md` — правила игры (по коду официального арбитра) и варианты реализации.
- `src/LocmBot/Sim/` — симулятор правил боя (точная копия логики арбитра), `tests/LocmBot.Tests` — тесты и сверка с логами арбитра (`-- replay`).
- `tools/refcheck/` — генерация логов случайных партий настоящим Java-движком арбитра; `tests/fixtures/` — такие логи, на которых симулятор сверяется в тестах.
- `referee/cardlist.txt` — полный список 160 карт из официального арбитра
  (`baseId ; name ; type ; cost ; attack ; defense ; abilities ; myHealthChange ; oppHealthChange ; cardDraw ; text`).

Официальный арбитр: https://github.com/CodinGame/LegendsOfCodeAndMagic
