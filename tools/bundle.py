#!/usr/bin/env python3
"""Склеивает все .cs из src/LocmBot в один файл для вставки в IDE CodinGame.

Правила, на которые опирается склейка:
  * каждый файл использует блочный `namespace Locm { ... }` (не file-scoped);
  * все `using` стоят в начале файла — они собираются и выносятся наверх без дублей.

Запуск: python3 tools/bundle.py  ->  build/codingame.cs
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "LocmBot"
OUT = ROOT / "build" / "codingame.cs"

USING_RE = re.compile(r"^\s*using\s+[\w.]+\s*;\s*$")


def main() -> int:
    files = sorted(p for p in SRC.rglob("*.cs") if "/obj/" not in p.as_posix() and "/bin/" not in p.as_posix())
    if not files:
        print("no sources found", file=sys.stderr)
        return 1

    usings: list[str] = []
    bodies: list[str] = []
    for path in files:
        body_lines = []
        for line in path.read_text(encoding="utf-8").splitlines():
            if USING_RE.match(line):
                u = line.strip()
                if u not in usings:
                    usings.append(u)
            else:
                body_lines.append(line)
        rel = path.relative_to(ROOT).as_posix()
        bodies.append(f"// ===== {rel} =====\n" + "\n".join(body_lines).strip("\n") + "\n")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(sorted(usings)) + "\n\n" + "\n".join(bodies), encoding="utf-8")
    print(f"{OUT.relative_to(ROOT)}: {len(files)} files, {OUT.stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
