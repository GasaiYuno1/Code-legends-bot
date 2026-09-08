using System;
using System.Collections.Generic;
using System.IO;

namespace Locm.Tests
{
    /// <summary>
    /// Сравнение моего поиска с реальными ходами из логов (партии арены): на каждой позиции считаем ход из лога
    /// и ход SearchBattle, оба оцениваем итоговой оценкой бота (статика + ответ противника).
    /// «Их лучше» по моей оценке — поиск не нашёл ход; «мой лучше» — оценка не согласна с сильным игроком.
    /// </summary>
    public static class Compare
    {
        public static int Run(string[] paths, int ms, string[] overrides) => Run(paths, ms, overrides, int.MaxValue, 1);

        /// <summary>Сколько примеров расхождений печатать (позиция, их ход, мой ход, оценки).</summary>
        public static int DumpExamples = 0;

        /// <param name="limit">не больше стольких ходов;</param>
        /// <param name="step">брать каждый step-й ход (равномерная выборка по всем файлам).</param>
        public static int Run(string[] paths, int ms, string[] overrides, int limit, int step)
        {
            var search = new SearchBattle();
            Tuning.Apply(overrides, search, Console.Out);
            int turns = 0, same = 0, theirsBetter = 0, mineBetter = 0, theirsWin = 0, mineWin = 0;
            double sumDiff = 0;
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.log"));
                else files.Add(p);
            }
            files.Sort(StringComparer.Ordinal);

            int seen = 0, dumped = 0;
            foreach (var f in files)
            {
                if (turns >= limit) break;
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (turns >= limit) break;
                    if (t.Input.LooksLikeDraft) continue;
                    var s = GameState.FromInput(t.Input);
                    if (s.IsOver) continue;
                    if (seen++ % step != 0) continue;

                    var theirs = GameAction.ParseSequence(t.Answer);
                    var afterTheirs = s.Clone();
                    afterTheirs.ApplySequence(theirs);
                    var mine = new List<GameAction>(search.Search(s, new TurnClock(ms)));
                    var afterMine = s.Clone();
                    afterMine.ApplySequence(mine);

                    double scoreTheirs = Final(search, afterTheirs);
                    double scoreMine = Final(search, afterMine);
                    turns++;
                    if (afterTheirs.Winner == 0) theirsWin++;
                    if (afterMine.Winner == 0) mineWin++;
                    if (SameActions(theirs, mine)) same++;
                    double diff = scoreMine - scoreTheirs;
                    if (Math.Abs(diff) < Evaluator.WinScore / 2) sumDiff += diff;
                    if (diff > 0.5) mineBetter++;
                    else if (diff < -0.5) theirsBetter++;
                    if (diff > 0.5 && dumped < DumpExamples)
                    {
                        dumped++;
                        Console.WriteLine($"==== {Path.GetFileName(f)} turn {turns}: my {scoreMine:F2} vs their {scoreTheirs:F2}");
                        Console.Write(s.ToString());
                        Console.WriteLine("   their: " + t.Answer);
                        Console.WriteLine("   mine:  " + GameAction.Format(mine));
                    }
                }
            }

            Console.WriteLine($"{files.Count} files, {turns} turns, {ms} ms per search");
            Console.WriteLine($"  same move:                {same,6} ({Pct(same, turns)})");
            Console.WriteLine($"  their move better (>0.5): {theirsBetter,6} ({Pct(theirsBetter, turns)})  <- search misses");
            Console.WriteLine($"  my move better (>0.5):    {mineBetter,6} ({Pct(mineBetter, turns)})  <- eval disagrees with Legend");
            Console.WriteLine($"  lethal found: theirs {theirsWin}, mine {mineWin}");
            Console.WriteLine($"  mean(my - their) on non-lethal turns: {sumDiff / Math.Max(1, turns):F2}");
            return 0;
        }

        private static double Final(SearchBattle search, GameState after)
        {
            double st = search.Eval.Score(after, 0);
            double rp = search.ReplyScore(after, 0);
            return search.ReplyWeight * rp + (1 - search.ReplyWeight) * st;
        }

        private static bool SameActions(List<GameAction> a, List<GameAction> b)
        {
            var sa = new List<string>();
            var sb = new List<string>();
            foreach (var x in a) if (!x.IsPass) sa.Add(x.ToString());
            foreach (var x in b) if (!x.IsPass) sb.Add(x.ToString());
            sa.Sort(StringComparer.Ordinal);
            sb.Sort(StringComparer.Ordinal);
            return string.Join(";", sa) == string.Join(";", sb);
        }

        private static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("F1") + "%";
    }
}
