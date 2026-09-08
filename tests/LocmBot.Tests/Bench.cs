using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Locm.Tests
{
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
