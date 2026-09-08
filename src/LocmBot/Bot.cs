using System;
using System.Collections.Generic;
using System.IO;

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
        private readonly List<Card> _picked = new List<Card>();
        private int _turn;

        public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log)
        {
            _draft = draft;
            _battle = battle;
            _log = log;
        }

        public void Run(TextReader input, TextWriter output)
        {
            while (true)
            {
                string first = input.ReadLine();
                if (first == null) return;
                var clock = new TurnClock((_turn == 0 || _turn == DraftTurns ? TimeLimits.FirstTurnMs : TimeLimits.TurnMs) - TimeLimits.SafetyMarginMs);

                TurnInput turn;
                try
                {
                    turn = InputParser.ReadTurn(first, input);
                }
                catch (Exception e)
                {
                    _log.WriteLine("Input parse error: " + e.Message);
                    output.WriteLine("PASS");
                    output.Flush();
                    _turn++;
                    continue;
                }

                string answer = PlayTurn(turn, clock);
                output.WriteLine(answer);
                output.Flush();
                _log.WriteLine($"turn {_turn} done in {clock.ElapsedMs} ms: {answer}");
                _turn++;
            }
        }

        public string PlayTurn(TurnInput turn, TurnClock clock)
        {
            bool isDraft = _turn < DraftTurns && turn.LooksLikeDraft;
            if (isDraft)
            {
                int idx = _draft.Pick(turn, _picked);
                if (idx < 0 || idx > 2) idx = 0;
                _picked.Add(turn.Cards[idx]);
                return "PICK " + idx;
            }

            string actions = _battle.PlayTurn(turn, clock);
            return string.IsNullOrWhiteSpace(actions) ? "PASS" : actions;
        }
    }
}
