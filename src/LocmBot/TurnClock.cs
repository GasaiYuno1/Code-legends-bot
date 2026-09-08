using System.Diagnostics;

namespace Locm
{
    /// <summary>Часы хода: запускаются сразу после получения первой строки ввода.</summary>
    public sealed class TurnClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public readonly int BudgetMs;

        public TurnClock(int budgetMs) { BudgetMs = budgetMs; }

        public long ElapsedMs => _sw.ElapsedMilliseconds;
        public bool TimeUp => _sw.ElapsedMilliseconds >= BudgetMs;
    }

    public static class TimeLimits
    {
        // Значения арбитра; держим запас на JIT/GC и задержки ввода-вывода.
        public const int FirstTurnMs = 1000;
        public const int TurnMs = 100;
        public const int SafetyMarginMs = 15;
    }
}
