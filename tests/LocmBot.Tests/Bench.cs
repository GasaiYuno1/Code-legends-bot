using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Locm.Tests
{
    /// <summary>Выгрузка обучающих данных для NetEval: строки "label\tf1\tf2..." для позиций начала хода и после моего хода.</summary>
    public static class FeatureExport
    {
        public static int Run(string dir, string outPath)
        {
            var files = Directory.GetFiles(dir, "*.log");
            Array.Sort(files, StringComparer.Ordinal);
            var f = new float[NetFeatures.Count];
            int rows = 0;
            using (var w = new StreamWriter(outPath))
            {
                foreach (var file in files)
                {
                    string head = File.ReadLines(file).GetEnumerator().MoveNextOrDefault();
                    var h = head.Split(' ');
                    int player = int.Parse(h[5]), winner = int.Parse(h[13]);
                    int label = winner == player ? 1 : 0;
                    string game = Path.GetFileName(file);
                    foreach (var t in Replay.Parse(File.ReadAllText(file)))
                    {
                        if (t.Input.LooksLikeDraft) continue;
                        var s = GameState.FromInput(t.Input);
                        if (s.IsOver) continue;
                        Write(w, game, label, s, f);
                        rows++;
                        s.ApplySequence(GameAction.ParseSequence(t.Answer));
                        if (s.IsOver) continue;
                        Write(w, game, label, s, f);
                        rows++;
                    }
                }
            }
            Console.WriteLine($"{files.Length} files, {rows} rows -> {outPath}");
            return 0;
        }

        private static void Write(StreamWriter w, string game, int label, GameState s, float[] f)
        {
            NetFeatures.Extract(s, 0, f);
            w.Write(game);
            w.Write('\t');
            w.Write(label);
            for (int i = 0; i < f.Length; i++)
            {
                w.Write('\t');
                w.Write(f[i].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            }
            w.Write('\n');
        }

        private static string MoveNextOrDefault(this IEnumerator<string> e) => e.MoveNext() ? e.Current : "";
    }

    /// <summary>Пропускная способность поиска: узлов в миллисекунду на реальных позициях (для решения о C++).</summary>
    public static class Bench
    {
        public static int Run(string dir, int positions, int ms)
        {
            var files = Directory.GetFiles(dir, "*.log");
            Array.Sort(files, StringComparer.Ordinal);
            var search = new SearchBattle();
            search.DeepReplyCandidates = 0;
            long nodes = 0, elapsed = 0;
            int done = 0, finished = 0;
            var heavy = new List<GameState>();
            foreach (var f in files)
            {
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (t.Input.LooksLikeDraft) continue;
                    var s = GameState.FromInput(t.Input);
                    if (s.IsOver || s.Me.HandKnown + s.Me.BoardCount + s.Opp.BoardCount < 9) continue;
                    heavy.Add(s);
                    if (heavy.Count >= positions) break;
                }
                if (heavy.Count >= positions) break;
            }
            foreach (var s in heavy)
            {
                var sw = Stopwatch.StartNew();
                search.Search(s, new TurnClock(ms));
                sw.Stop();
                nodes += search.Nodes;
                elapsed += sw.ElapsedMilliseconds;
                done++;
                if (!search.TimedOut) finished++;
            }
            Console.WriteLine($"{done} heavy positions, {nodes} phase-1 nodes in {elapsed} ms = {(double)nodes / Math.Max(1, elapsed):F0} nodes/ms; finished exhaustively: {finished}/{done}");
            return 0;
        }
    }
}
