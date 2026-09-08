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
        /// <summary>Печатать только случаи «мой ход выигрывает, их — нет» (проверка, что леталы настоящие).</summary>
        public static bool DumpLethalOnly = false;

        /// <param name="limit">не больше стольких ходов;</param>
        /// <param name="step">брать каждый step-й ход (равномерная выборка по всем файлам).</param>
        public static int Run(string[] paths, int ms, string[] overrides, int limit, int step)
        {
            var search = new SearchBattle();
            Tuning.Apply(overrides, search, Console.Out);
            int turns = 0, same = 0, theirsBetter = 0, mineBetter = 0, theirsWin = 0, mineWin = 0;
            long maxMs = 0, sumMs = 0; int timedOut = 0, sampledTotal = 0, modelTurns = 0, modelMismatch = 0;
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
                search.ResetGame();
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (turns >= limit) break;
                    if (t.Input.LooksLikeDraft) { search.ObserveDraft(t.Input); continue; }
                    search.Opponent.ObserveBattle(t.Input);
                    var s = GameState.FromInput(t.Input);
                    if (s.IsOver) continue;
                    if (search.Opponent.DeckKnown)
                    {
                        modelTurns++;
                        if (search.Opponent.UnknownCount() != s.Players[1].HandCount + s.Players[1].DeckSize) modelMismatch++;
                    }
                    if (seen++ % step != 0) continue;

                    var theirs = GameAction.ParseSequence(t.Answer);
                    var afterTheirs = s.Clone();
                    afterTheirs.ApplySequence(theirs);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var mine = new List<GameAction>(search.Search(s, new TurnClock(ms)));
                    sw.Stop();
                    if (sw.ElapsedMilliseconds > maxMs) maxMs = sw.ElapsedMilliseconds;
                    sumMs += sw.ElapsedMilliseconds;
                    sampledTotal += search.SampledScored;
                    if (search.TimedOut) timedOut++;
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
                    bool lethalCase = afterMine.Winner == 0 && afterTheirs.Winner != 0;
                    if (dumped < DumpExamples && (DumpLethalOnly ? lethalCase : diff > 0.5))
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
            Console.WriteLine($"  time: avg {(double)sumMs / Math.Max(1, turns):F1} ms, max {maxMs} ms, timed out {timedOut}; sampled rescored {sampledTotal}");
            Console.WriteLine($"  opponent model: deck known on {modelTurns} turns, unknown-count mismatch {modelMismatch}");
            return 0;
        }

        /// <summary>
        /// Точность модели ответа: после моего фактического хода предсказываем атаки противника (DeepReply по OppEval)
        /// и сравниваем с его реальными атаками из следующего ввода (только существа, стоявшие у него до его хода).
        /// </summary>
        public static int ReplyCheck(string[] paths, string[] overrides, int limit, int step)
        {
            var search = new SearchBattle();
            Tuning.Apply(overrides, search, Console.Out);
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.log"));
                else files.Add(p);
            }
            files.Sort(StringComparer.Ordinal);
            var predicted = new List<GameAction>();
            int turns = 0, sameSet = 0, creatures = 0, targetMatch = 0, seen = 0;
            foreach (var f in files)
            {
                if (turns >= limit) break;
                var battle = new List<Replay.LoggedTurn>();
                search.ResetGame();
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (t.Input.LooksLikeDraft) search.ObserveDraft(t.Input);
                    else battle.Add(t);
                }
                for (int i = 0; i + 1 < battle.Count && turns < limit; i++)
                {
                    search.Opponent.ObserveBattle(battle[i].Input);
                    var s = GameState.FromInput(battle[i].Input);
                    if (s.IsOver) continue;
                    if (seen++ % step != 0) continue;
                    s.ApplySequence(GameAction.ParseSequence(battle[i].Answer));
                    if (s.IsOver || s.Opp.BoardCount == 0) continue;
                    // реальные атаки существ, стоявших до хода противника
                    var actual = new Dictionary<int, int>();
                    foreach (var oa in battle[i + 1].Input.OpponentActions)
                    {
                        GameAction a;
                        if (!GameAction.TryParse(oa.Action, out a) || a.Type != ActionType.Attack) continue;
                        if (s.Opp.FindCreature(a.Id) >= 0 && !actual.ContainsKey(a.Id)) actual[a.Id] = a.Target;
                    }
                    if (search.SampledCandidates > 0) search.PredictFullReply(s, 0, predicted);
                    else search.PredictReply(s, 0, predicted);
                    var pred = new Dictionary<int, int>();
                    foreach (var a in predicted)
                        if (a.Type == ActionType.Attack && s.Opp.FindCreature(a.Id) >= 0 && !pred.ContainsKey(a.Id)) pred[a.Id] = a.Target;
                    turns++;
                    bool same = pred.Count == actual.Count;
                    for (int k = 0; k < s.Opp.BoardCount; k++)
                    {
                        int id = s.Opp.Board[k].InstanceId;
                        int pt, at;
                        bool hp = pred.TryGetValue(id, out pt), ha = actual.TryGetValue(id, out at);
                        creatures++;
                        if (hp == ha && (!hp || pt == at)) targetMatch++; else same = false;
                    }
                    if (same) sameSet++;
                }
            }
            Console.WriteLine($"{files.Count} files, {turns} replies: whole reply predicted {Pct(sameSet, turns)}, per-creature action match {Pct(targetMatch, creatures)} ({creatures} creatures)");
            return 0;
        }

        /// <summary>
        /// sampdiff: на одних позициях — выбор поиска без модели руки (sampled=0) и с ней (overrides как есть);
        /// печатает позиции, где ходы различаются, с оценками обеих линий по обоим критериям, и кто из них совпал с ходом из лога.
        /// </summary>
        public static int SampledDiff(string[] paths, int ms, string[] overrides, int limit, int step, int dump)
        {
            var a = new SearchBattle();
            var b = new SearchBattle();
            Tuning.Apply(overrides, a, TextWriter.Null);
            Tuning.Apply(new[] { "sampled=0" }, a, TextWriter.Null);
            Tuning.Apply(overrides, b, Console.Out);
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.log"));
                else files.Add(p);
            }
            files.Sort(StringComparer.Ordinal);
            int turns = 0, differ = 0, aSame = 0, bSame = 0, seen = 0, dumped = 0;
            double sumDeepGap = 0, sumSampGap = 0;
            foreach (var f in files)
            {
                if (turns >= limit) break;
                a.ResetGame(); b.ResetGame();
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (turns >= limit) break;
                    if (t.Input.LooksLikeDraft) { a.ObserveDraft(t.Input); b.ObserveDraft(t.Input); continue; }
                    a.Opponent.ObserveBattle(t.Input);
                    b.Opponent.ObserveBattle(t.Input);
                    var s = GameState.FromInput(t.Input);
                    if (s.IsOver) continue;
                    if (seen++ % step != 0) continue;
                    var theirs = GameAction.ParseSequence(t.Answer);
                    var mineA = new List<GameAction>(a.Search(s, new TurnClock(ms)));
                    var mineB = new List<GameAction>(b.Search(s, new TurnClock(ms)));
                    turns++;
                    if (SameActions(mineA, mineB)) continue;
                    differ++;
                    bool sa = SameActions(theirs, mineA), sb = SameActions(theirs, mineB);
                    if (sa) aSame++;
                    if (sb) bSame++;
                    var afterA = s.Clone(); afterA.ApplySequence(mineA);
                    var afterB = s.Clone(); afterB.ApplySequence(mineB);
                    double deepA = Final(a, afterA), deepB = Final(a, afterB);
                    double sampA = b.SampledFinal(afterA, 0), sampB = b.SampledFinal(afterB, 0);
                    if (Math.Abs(deepA - deepB) < Evaluator.WinScore / 2) sumDeepGap += deepA - deepB;
                    if (Math.Abs(sampA - sampB) < Evaluator.WinScore / 2) sumSampGap += sampB - sampA;
                    if (dumped < dump)
                    {
                        dumped++;
                        Console.WriteLine($"==== {Path.GetFileName(f)} turn {turns}");
                        Console.Write(s.ToString());
                        Console.WriteLine($"   log:      {t.Answer}");
                        Console.WriteLine($"   no-model: {GameAction.Format(mineA)}   deep {deepA:F2} sampled {sampA:F2}{(sa ? "  <- log" : "")}");
                        Console.WriteLine($"   model:    {GameAction.Format(mineB)}   deep {deepB:F2} sampled {sampB:F2}{(sb ? "  <- log" : "")}");
                    }
                }
            }
            Console.WriteLine($"{files.Count} files, {turns} turns, {ms} ms per search: moves differ on {differ} ({Pct(differ, turns)})");
            Console.WriteLine($"  of those, log move = no-model {aSame}, = model {bSame}");
            Console.WriteLine($"  mean gap on differing turns: deep(no-model - model) {sumDeepGap / Math.Max(1, differ):F2}, sampled(model - no-model) {sumSampGap / Math.Max(1, differ):F2}");
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
