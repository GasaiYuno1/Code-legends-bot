#!/usr/bin/env python3
"""Склеивает все .cs из src/LocmBot в один файл для вставки в IDE CodinGame.

Правила, на которые опирается склейка:
  * каждый файл использует блочный `namespace Locm { ... }` (не file-scoped);
  * все `using` стоят в начале файла — они собираются и выносятся наверх без дублей;
  * строки, начинающиеся с `//` (включая `///`), выбрасываются ради лимита размера CodinGame.

Запуск: python3 tools/bundle.py  ->  dist/codingame.cs (файл отслеживается git — пересобирать перед пушем)
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "LocmBot"
OUT = ROOT / "dist" / "codingame.cs"

USING_RE = re.compile(r"^\s*using\s+[\w.]+\s*;\s*$")
COMMENT_LINE_RE = re.compile(r"^\s*//")  # строки-комментарии (в т.ч. /// doc) выбрасываем: у CodinGame лимит ~100k символов


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
            elif COMMENT_LINE_RE.match(line):
                continue
            else:
                body_lines.append(line)
        rel = path.relative_to(ROOT).as_posix()
        bodies.append(f"// ===== {rel} =====\n" + "\n".join(body_lines).strip("\n") + "\n")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(sorted(usings)) + "\n\n" + "\n".join(bodies), encoding="utf-8")
    text = OUT.read_text(encoding="utf-8")
    print(f"{OUT.relative_to(ROOT)}: {len(files)} files, {OUT.stat().st_size} bytes, {len(text)} chars (CodinGame limit ~100k)")
    return 0 if len(text) < 100_000 else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
