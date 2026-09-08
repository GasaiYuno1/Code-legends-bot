using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Locm
{
    /// <summary>
    /// Оркестратор: определяет фазу, вызывает стратегию, выводит ответ.
    /// Не зависит от консоли — принимает TextReader/TextWriter, чтобы гоняться в тестах и локальном арбитре.
    /// </summary>
    public sealed class Bot
    {
        public const int DraftTurns = 30;

        private readonly IDraftStrategy _draft;
        private readonly IBattleStrategy _battle;
        private readonly TextWriter _log;
        private readonly TextWriter _dump;
        private readonly List<Card> _picked = new List<Card>();
        private int _turn;

        public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log) : this(draft, battle, log, null) { }

        /// <param name="dump">Если задан — сюда пишется ввод каждого хода как есть и строка "> ответ":
        /// такой лог можно прогнать через симулятор (dotnet run --project tests/LocmBot.Tests -- replay файл).</param>
        public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log, TextWriter dump)
        {
            _draft = draft;
            _battle = battle;
            _log = log;
            _dump = dump;
        }

        public void Run(TextReader input, TextWriter output)
        {
            var recorder = _dump != null ? new RecordingReader(input) : null;
            if (recorder != null) input = recorder;

            while (true)
            {
                string first = input.ReadLine();
                if (first == null) return;
                var clock = new TurnClock((_turn == 0 || _turn == DraftTurns ? TimeLimits.FirstTurnMs : TimeLimits.TurnMs) - TimeLimits.SafetyMarginMs);

                string answer;
                try
                {
                    TurnInput turn = InputParser.ReadTurn(first, input);
                    answer = PlayTurn(turn, clock);
                }
                catch (Exception e)
                {
                    _log.WriteLine("Turn error: " + e.Message);
                    answer = "PASS";
                }

                output.WriteLine(answer);
                output.Flush();
                if (recorder != null)
                {
                    _dump.Write(recorder.Take());
                    _dump.WriteLine("> " + answer);
                    _dump.Flush();
                }
                _log.WriteLine($"turn {_turn} done in {clock.ElapsedMs} ms: {answer}");
                _turn++;
            }
        }

        public string PlayTurn(TurnInput turn, TurnClock clock)
        {
            bool isDraft = _turn < DraftTurns && turn.LooksLikeDraft;
            if (isDraft)
            {
                // первый ход драфта даёт 1000 мс — прогреваем JIT боевого поиска, чтобы не платить за него в бою
                if (_turn == 0)
                {
                    try { _battle.WarmUp(clock); }
                    catch (Exception e) { _log.WriteLine("WarmUp error: " + e.Message); }
                }
                int idx = _draft.Pick(turn, _picked);
                if (idx < 0 || idx > 2) idx = 0;
                _picked.Add(turn.Cards[idx]);
                return "PICK " + idx;
            }

            string actions = _battle.PlayTurn(turn, clock);
            return string.IsNullOrWhiteSpace(actions) ? "PASS" : actions;
        }

        /// <summary>Читает строки из внутреннего ридера и запоминает их для дампа.</summary>
        private sealed class RecordingReader : TextReader
        {
            private readonly TextReader _inner;
            private readonly StringBuilder _buf = new StringBuilder();

            public RecordingReader(TextReader inner) { _inner = inner; }

            public override string ReadLine()
            {
                string line = _inner.ReadLine();
                if (line != null) _buf.Append(line).Append('\n');
                return line;
            }

            public override int Read() => _inner.Read();
            public override int Peek() => _inner.Peek();

            public string Take()
            {
                string s = _buf.ToString();
                _buf.Clear();
                return s;
            }
        }
    }
}
