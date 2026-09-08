using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Locm.Tests
{
    /// <summary>Итог прогона одного лога через симулятор.</summary>
    public sealed class ReplayResult
    {
        public string Name;
        public int BattleTurns;          // ходов боя в логе
        public int CheckedTurns;         // сколько циклов «мой ход + ход противника» сверено
        public int IllegalSkipped;       // сколько моих действий симулятор счёл нелегальными (арбитр их тоже пропускает)
        public readonly List<string> Mismatches = new List<string>();
        public bool Ok => Mismatches.Count == 0;
    }

    /// <summary>
    /// Сверка симулятора с арбитром по логу партии: последовательность блоков «ввод арбитра как есть»
    /// + строка "> ответ бота". Для каждой пары соседних ходов: применяем свои действия, завершаем ход,
    /// применяем действия противника (они перечислены в следующем вводе), завершаем его ход и сравниваем
    /// полученное состояние с тем, что прислал арбитр. Строки, начинающиеся с '#', и пустые игнорируются.
    /// </summary>
    public static class Replay
    {
        public sealed class LoggedTurn
        {
            public TurnInput Input;
            public string Answer;
        }

        public static List<LoggedTurn> Parse(string text)
        {
            var clean = new StringBuilder();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                clean.Append(line).Append('\n');
            }

            var reader = new StringReader(clean.ToString());
            var turns = new List<LoggedTurn>();
            while (true)
            {
                var input = InputParser.ReadTurn(reader);
                if (input == null) break;
                string answer = reader.ReadLine();
                if (answer == null || !answer.TrimStart().StartsWith(">"))
                    throw new FormatException($"turn {turns.Count}: expected '> answer' line, got '{answer}'");
                turns.Add(new LoggedTurn { Input = input, Answer = answer.TrimStart().Substring(1).Trim() });
            }
            return turns;
        }

        public static ReplayResult RunFile(string path)
        {
            var r = Run(File.ReadAllText(path));
            r.Name = Path.GetFileName(path);
            return r;
        }

        public static ReplayResult Run(string text)
        {
            var r = new ReplayResult();
            var battle = new List<LoggedTurn>();
            foreach (var t in Parse(text))
                if (!t.Input.LooksLikeDraft) battle.Add(t);
            r.BattleTurns = battle.Count;
            if (battle.Count == 0) return r;

            bool second = GameState.IsSecondPlayer(battle[0].Input);
            int myBonus = second ? 1 : 0;
            int oppBonus = second ? 0 : 1;

            for (int i = 0; i + 1 < battle.Count; i++)
            {
                var cur = battle[i];
                var next = battle[i + 1];
                var state = GameState.FromInput(cur.Input, GameState.RefereeTurn(i, second));
                state.Players[0].BonusManaTurns = myBonus;
                state.Players[1].BonusManaTurns = oppBonus;
                // мана противника на конец его прошлого хода — нужна для логики бонуса второго игрока
                state.Players[1].Mana = cur.Input.Opponent.Mana - SpentMana(cur.Input.OpponentActions);

                if (state.IsOver)
                {
                    r.Mismatches.Add($"turn {i}: game is over at turn start, but the log continues");
                    continue;
                }

                // мой ход
                r.IllegalSkipped += state.ApplySequence(GameAction.ParseSequence(cur.Answer));
                state.EndTurn();

                // ход противника: его действия перечислены в следующем вводе
                bool broken = false;
                foreach (var oa in next.Input.OpponentActions)
                {
                    if (state.IsOver)
                    {
                        r.Mismatches.Add($"turn {i}: opponent acted ({oa}) but simulator says the game is over");
                        broken = true;
                        break;
                    }
                    var a = GameAction.Parse(oa.Action);
                    if (a.Type == ActionType.Summon || a.Type == ActionType.Use)
                        state.Me.RevealHandCard(CardDb.Get(oa.CardNumber).WithInstance(a.Id, Location.MyHand));
                    if (!state.TryApply(a))
                    {
                        r.Mismatches.Add($"turn {i}: opponent action '{oa}' is illegal in simulator\n{state}");
                        broken = true;
                        break;
                    }
                }
                if (broken) continue;
                state.EndTurn();

                myBonus = state.Players[0].BonusManaTurns;
                oppBonus = state.Players[1].BonusManaTurns;

                // Если я погиб на собственном доборе, арбитр всё равно присылает мне этот ввод (HP <= 0) —
                // сверяем его как обычно; дальше лога быть не должно (проверка в начале следующей итерации).
                if (state.IsOver && state.Players[0].Health > 0)
                {
                    r.Mismatches.Add($"turn {i}: simulator says the game is over (winner P{state.Winner}), but the log continues\n{state}");
                    continue;
                }

                var expected = GameState.FromInput(next.Input);
                int before = r.Mismatches.Count;
                Compare(i, state, expected, r.Mismatches);
                if (r.Mismatches.Count > before)
                    r.Mismatches.Add($"turn {i}: simulated state:\n{state}expected:\n{expected}");
                r.CheckedTurns++;
            }
            return r;
        }

        private static int SpentMana(List<OpponentAction> actions)
        {
            int spent = 0;
            foreach (var oa in actions)
            {
                GameAction a;
                if (!GameAction.TryParse(oa.Action, out a)) continue;
                if (a.Type == ActionType.Summon || a.Type == ActionType.Use) spent += CardDb.Get(oa.CardNumber).Cost;
            }
            return spent;
        }

        private static void Compare(int turn, GameState sim, GameState exp, List<string> log)
        {
            for (int p = 0; p < 2; p++)
            {
                string who = p == 0 ? "me" : "opp";
                var s = sim.Players[p];
                var e = exp.Players[p];
                Field(turn, who, "health", s.Health, e.Health, log);
                Field(turn, who, "maxMana", s.MaxMana, e.MaxMana, log);
                Field(turn, who, "deck", s.DeckSize, e.DeckSize, log);
                Field(turn, who, "rune", s.NextRune, e.NextRune, log);
                Field(turn, who, "draw", s.DrawShown, e.DrawShown, log);
                Field(turn, who, "handCount", s.HandCount, e.HandCount, log);
                if (p == 0)
                {
                    // известные карты моей руки идут первыми и в том же порядке; добранные — в конце
                    for (int k = 0; k < s.HandKnown; k++)
                    {
                        string sl = s.Hand[k].ToInputLine(Location.MyHand);
                        string el = k < e.HandKnown ? e.Hand[k].ToInputLine(Location.MyHand) : "<none>";
                        if (sl != el) log.Add($"turn {turn}: me hand[{k}]: sim '{sl}' vs referee '{el}'");
                    }
                }
                Field(turn, who, "boardCount", s.BoardCount, e.BoardCount, log);
                int n = Math.Min(s.BoardCount, e.BoardCount);
                for (int k = 0; k < n; k++)
                {
                    string sl = s.Board[k].ToInputLine(p == 1);
                    string el = e.Board[k].ToInputLine(p == 1);
                    if (sl != el) log.Add($"turn {turn}: {who} board[{k}]: sim '{sl}' vs referee '{el}'");
                }
            }
        }

        private static void Field(int turn, string who, string name, int sim, int exp, List<string> log)
        {
            if (sim != exp) log.Add($"turn {turn}: {who} {name}: sim {sim} vs referee {exp}");
        }

        /// <summary>Каталог tests/fixtures — ищем вверх от бинарника и от текущего каталога.</summary>
        public static string FixturesDir()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "tests", "fixtures");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }
}
