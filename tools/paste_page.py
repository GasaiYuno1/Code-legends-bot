#!/usr/bin/env python3
"""Страница «скопировать код бота» для телефона: одна кнопка кладёт dist/codingame.cs в буфер обмена.

Запуск: python3 tools/paste_page.py [выходной файл]   (по умолчанию build/paste.html)
Страница публикуется как артефакт claude.ai; на CodinGame файлы с телефона не загрузить, а буфер обмена — можно.
"""
import html
import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "dist" / "codingame.cs"
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "build" / "paste.html"


def git(*args: str) -> str:
    try:
        return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()
    except Exception:
        return "?"


def main() -> None:
    code = SRC.read_text(encoding="utf-8")
    chars = len(code)
    lines = code.count("\n") + (0 if code.endswith("\n") else 1)
    last_line = code.rstrip("\n").splitlines()[-1]
    commit = git("rev-parse", "--short", "HEAD")
    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    today = date.today().isoformat()

    page = f"""<title>Склейка locm-bot</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Sora:wght@600;700&family=Manrope:wght@400;500;600&family=JetBrains+Mono:wght@400&display=swap">
<style>
  :root {{
    --bg: #f2f4f7;
    --surface: #ffffff;
    --ink: #17202b;
    --muted: #5b6776;
    --line: #d5dbe3;
    --accent: #2c5bd6;
    --accent-ink: #ffffff;
    --accent-soft: #e4ebfb;
    --ok: #1e7f4f;
    --ok-soft: #dff3e7;
    --code-bg: #fbfcfe;
    --shadow: 0 10px 30px rgba(23, 32, 43, 0.12);
  }}
  @media (prefers-color-scheme: dark) {{
    :root:not([data-theme="light"]) {{
      --bg: #0f141a;
      --surface: #171e27;
      --ink: #e7ebf1;
      --muted: #97a3b3;
      --line: #2a3441;
      --accent: #7ea2ff;
      --accent-ink: #0d1424;
      --accent-soft: #1d2a45;
      --ok: #58c78d;
      --ok-soft: #173327;
      --code-bg: #111820;
      --shadow: 0 10px 30px rgba(0, 0, 0, 0.45);
    }}
  }}
  :root[data-theme="dark"] {{
    --bg: #0f141a;
    --surface: #171e27;
    --ink: #e7ebf1;
    --muted: #97a3b3;
    --line: #2a3441;
    --accent: #7ea2ff;
    --accent-ink: #0d1424;
    --accent-soft: #1d2a45;
    --ok: #58c78d;
    --ok-soft: #173327;
    --code-bg: #111820;
    --shadow: 0 10px 30px rgba(0, 0, 0, 0.45);
  }}
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0;
    background: var(--bg);
    color: var(--ink);
    font-family: "Manrope", "Segoe UI", Roboto, system-ui, sans-serif;
    font-size: 16px;
    line-height: 1.5;
    -webkit-text-size-adjust: 100%;
  }}
  .page {{
    max-width: 720px;
    margin: 0 auto;
    padding: 24px 16px 132px;
    display: flex;
    flex-direction: column;
    gap: 20px;
  }}
  header {{ display: flex; flex-direction: column; gap: 6px; }}
  .eyebrow {{
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--muted);
  }}
  h1 {{
    margin: 0;
    font-family: "Sora", "Manrope", system-ui, sans-serif;
    font-size: 28px;
    font-weight: 700;
    line-height: 1.15;
    text-wrap: balance;
  }}
  .meta {{
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
    gap: 10px 16px;
    padding: 14px 16px;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: 10px;
  }}
  .meta div {{ display: flex; flex-direction: column; gap: 2px; }}
  .meta dt {{ font-size: 12px; color: var(--muted); letter-spacing: 0.04em; text-transform: uppercase; }}
  .meta dd {{ margin: 0; font-weight: 600; font-variant-numeric: tabular-nums; }}
  .meta code {{ font-family: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace; font-size: 14px; }}
  ol.steps {{
    margin: 0;
    padding: 0 0 0 26px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }}
  ol.steps li {{ padding-left: 4px; }}
  ol.steps li::marker {{ font-family: "Sora", sans-serif; font-weight: 700; color: var(--accent); }}
  .check {{
    padding: 12px 16px;
    border-left: 3px solid var(--accent);
    background: var(--accent-soft);
    border-radius: 0 8px 8px 0;
    font-size: 15px;
  }}
  .check code {{ font-family: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace; font-size: 13px; }}
  .codebox {{ display: flex; flex-direction: column; gap: 8px; }}
  .codebox .bar {{ display: flex; justify-content: space-between; align-items: baseline; gap: 12px; }}
  .codebox label {{ font-weight: 600; }}
  .ghost {{
    appearance: none;
    border: 1px solid var(--line);
    background: var(--surface);
    color: var(--ink);
    border-radius: 8px;
    padding: 8px 12px;
    font: inherit;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
  }}
  .ghost:focus-visible, .primary:focus-visible {{ outline: 3px solid var(--accent); outline-offset: 2px; }}
  textarea {{
    width: 100%;
    height: 46vh;
    min-height: 260px;
    resize: vertical;
    padding: 12px;
    border: 1px solid var(--line);
    border-radius: 10px;
    background: var(--code-bg);
    color: var(--ink);
    font-family: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace;
    font-size: 12px;
    line-height: 1.45;
    white-space: pre;
    overflow: auto;
    tab-size: 4;
  }}
  .dock {{
    position: fixed;
    left: 0; right: 0; bottom: 0;
    padding: 12px 16px calc(12px + env(safe-area-inset-bottom));
    background: linear-gradient(to top, var(--bg) 70%, transparent);
  }}
  .dock .inner {{ max-width: 720px; margin: 0 auto; display: flex; flex-direction: column; gap: 8px; }}
  .primary {{
    appearance: none;
    width: 100%;
    min-height: 56px;
    border: 0;
    border-radius: 14px;
    background: var(--accent);
    color: var(--accent-ink);
    font-family: "Sora", "Manrope", system-ui, sans-serif;
    font-size: 17px;
    font-weight: 700;
    letter-spacing: 0.01em;
    cursor: pointer;
    box-shadow: var(--shadow);
    transition: transform 120ms ease, background 200ms ease;
  }}
  .primary:active {{ transform: scale(0.985); }}
  .primary.done {{ background: var(--ok); color: #ffffff; }}
  .status {{
    min-height: 20px;
    text-align: center;
    font-size: 14px;
    color: var(--muted);
  }}
  .status.ok {{ color: var(--ok); font-weight: 600; }}
  @media (prefers-reduced-motion: reduce) {{
    .primary {{ transition: none; }}
    .primary:active {{ transform: none; }}
  }}
</style>

<main class="page">
  <header>
    <div class="eyebrow">CodinGame · Legends of Code &amp; Magic · C#</div>
    <h1>Код бота одним нажатием</h1>
  </header>

  <dl class="meta">
    <div><dt>Ветка</dt><dd><code>{html.escape(branch)}</code></dd></div>
    <div><dt>Коммит</dt><dd><code>{html.escape(commit)}</code></dd></div>
    <div><dt>Собрано</dt><dd>{today}</dd></div>
    <div><dt>Размер</dt><dd>{chars:,} символов, {lines:,} строк</dd></div>
  </dl>

  <ol class="steps">
    <li>Нажми «Скопировать код» внизу экрана.</li>
    <li>Открой задачу в IDE CodinGame, выбери язык C#, очисти редактор.</li>
    <li>Вставь и нажми «Play my code» (или «Submit» для арены).</li>
  </ol>

  <div class="check">
    Проверка, что вставилось целиком: последняя строка файла — <code>{html.escape(last_line)}</code>,
    всего {chars:,} символов. Комментариев и не-ASCII символов в файле нет.
  </div>

  <section class="codebox">
    <div class="bar">
      <label for="code">Исходник dist/codingame.cs</label>
      <button class="ghost" type="button" id="select">Выделить всё</button>
    </div>
    <textarea id="code" readonly spellcheck="false" autocomplete="off" autocorrect="off" autocapitalize="off" aria-label="Исходный код бота">{html.escape(code)}</textarea>
  </section>
</main>

<div class="dock">
  <div class="inner">
    <button class="primary" type="button" id="copy">Скопировать код</button>
    <div class="status" id="status" aria-live="polite"></div>
  </div>
</div>

<script>
(function () {{
  var ta = document.getElementById('code');
  var btn = document.getElementById('copy');
  var status = document.getElementById('status');
  var expected = {chars};

  function report(ok, text) {{
    status.textContent = text;
    status.className = 'status' + (ok ? ' ok' : '');
    if (ok) {{
      btn.classList.add('done');
      btn.textContent = 'Скопировано';
      setTimeout(function () {{ btn.classList.remove('done'); btn.textContent = 'Скопировать код'; }}, 4000);
    }}
  }}

  function legacyCopy() {{
    ta.focus();
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    var ok = false;
    try {{ ok = document.execCommand('copy'); }} catch (e) {{ ok = false; }}
    return ok;
  }}

  btn.addEventListener('click', function () {{
    var text = ta.value;
    if (navigator.clipboard && navigator.clipboard.writeText) {{
      navigator.clipboard.writeText(text).then(function () {{
        report(true, 'В буфере ' + text.length.toLocaleString('ru-RU') + ' символов' + (text.length === expected ? '' : ' (ожидалось ' + expected + ')'));
      }}, function () {{
        report(legacyCopy(), legacyCopy() ? 'Скопировано через выделение' : 'Браузер не дал доступ к буферу: нажми «Выделить всё» и скопируй вручную');
      }});
    }} else {{
      var ok = legacyCopy();
      report(ok, ok ? 'Скопировано через выделение' : 'Нажми «Выделить всё» и скопируй вручную');
    }}
  }});

  document.getElementById('select').addEventListener('click', function () {{
    ta.focus();
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    report(false, 'Выделено: теперь «Копировать» в меню выделения');
  }});
}})();
</script>
"""
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(page, encoding="utf-8")
    print(f"{OUT}: {OUT.stat().st_size} bytes, code {chars} chars")


if __name__ == "__main__":
    main()
