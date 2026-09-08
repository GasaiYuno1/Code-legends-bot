using System;
using System.IO;
using System.Reflection;

namespace Locm.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "replay")
                return ReplayMain(args);
            if (args.Length > 0 && args[0] == "candidates")
            {
                // candidates <логи> <out.bin> [topK=32] [step=1] [ms=30]
                int topK = 32, step = 1, ms = 30;
                for (int i = 3; i < args.Length; i++)
                {
                    if (args[i].StartsWith("topk=")) topK = int.Parse(args[i].Substring(5));
                    else if (args[i].StartsWith("step=")) step = int.Parse(args[i].Substring(5));
                    else if (args[i].StartsWith("ms=")) ms = int.Parse(args[i].Substring(3));
                }
                return CandidateExport.Run(args[1], args[2], topK, step, ms);
            }
            if (args.Length > 0 && args[0] == "features")
                return FeatureExport.Run(args[1], args.Length > 2 ? args[2] : "build/features.tsv");
            if (args.Length > 0 && args[0] == "bench")
                return Bench.Run(args.Length > 1 ? args[1] : Replay.FixturesDir(), args.Length > 2 ? int.Parse(args[2]) : 30, args.Length > 3 ? int.Parse(args[3]) : 300);
            if (args.Length > 0 && args[0] == "replycheck")
            {
                // replycheck <логи> [limit=N] [step=K] [key=value | o_key=value]
                int limit = int.MaxValue, step = 1;
                var paths = new System.Collections.Generic.List<string>();
                var overrides = new System.Collections.Generic.List<string>();
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i].StartsWith("limit=")) limit = int.Parse(args[i].Substring(6));
                    else if (args[i].StartsWith("step=")) step = int.Parse(args[i].Substring(5));
                    else if (args[i].Contains("=")) overrides.Add(args[i]);
                    else paths.Add(args[i]);
                }
                return Compare.ReplyCheck(paths.ToArray(), overrides.ToArray(), limit, step);
            }
            if (args.Length > 0 && args[0] == "draftcheck")
            {
                var paths = new System.Collections.Generic.List<string>();
                var overrides = new System.Collections.Generic.List<string>();
                bool verbose = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "-v") verbose = true;
                    else if (args[i].Contains("=")) overrides.Add(args[i]);
                    else paths.Add(args[i]);
                }
                return DraftCheck.Run(paths.ToArray(), verbose, overrides.ToArray());
            }
            if (args.Length > 0 && args[0] == "sampdiff")
            {
                // sampdiff <логи> [мс] [limit=N] [step=K] [dump=N] [key=value ...] — выбор без модели руки против выбора с ней
                int ms = 50, limit = int.MaxValue, step = 1, dump = 0;
                var paths = new System.Collections.Generic.List<string>();
                var overrides = new System.Collections.Generic.List<string>();
                for (int i = 1; i < args.Length; i++)
                {
                    int v;
                    if (int.TryParse(args[i], out v)) ms = v;
                    else if (args[i].StartsWith("limit=")) limit = int.Parse(args[i].Substring(6));
                    else if (args[i].StartsWith("step=")) step = int.Parse(args[i].Substring(5));
                    else if (args[i].StartsWith("dump=")) dump = int.Parse(args[i].Substring(5));
                    else if (args[i].Contains("=")) overrides.Add(args[i]);
                    else paths.Add(args[i]);
                }
                return Compare.SampledDiff(paths.ToArray(), ms, overrides.ToArray(), limit, step, dump);
            }
            if (args.Length > 0 && args[0] == "compare")
            {
                // compare <файлы|каталоги> [мс на поиск] [limit=N] [step=K] [key=value ...]
                int ms = 50, limit = int.MaxValue, step = 1;
                var paths = new System.Collections.Generic.List<string>();
                var overrides = new System.Collections.Generic.List<string>();
                for (int i = 1; i < args.Length; i++)
                {
                    int v;
                    if (int.TryParse(args[i], out v)) ms = v;
                    else if (args[i].StartsWith("limit=")) limit = int.Parse(args[i].Substring(6));
                    else if (args[i].StartsWith("step=")) step = int.Parse(args[i].Substring(5));
                    else if (args[i].StartsWith("dump=")) Compare.DumpExamples = int.Parse(args[i].Substring(5));
                    else if (args[i] == "dumplethal") Compare.DumpLethalOnly = true;
                    else if (args[i].Contains("=")) overrides.Add(args[i]);
                    else paths.Add(args[i]);
                }
                return Compare.Run(paths.ToArray(), ms, overrides.ToArray(), limit, step);
            }

            string filter = args.Length > 0 ? args[0] : null;
            return Runner.RunAll(Assembly.GetExecutingAssembly(), filter);
        }

        /// <summary>replay &lt;файлы или каталоги&gt; — сверка симулятора с логами арбитра (см. Replay.cs).</summary>
        private static int ReplayMain(string[] args)
        {
            int files = 0, turns = 0, bad = 0;
            for (int i = 1; i < args.Length; i++)
            {
                string[] paths = Directory.Exists(args[i]) ? Directory.GetFiles(args[i], "*.log") : new[] { args[i] };
                Array.Sort(paths, StringComparer.Ordinal);
                foreach (var path in paths)
                {
                    var r = Replay.RunFile(path);
                    files++;
                    turns += r.CheckedTurns;
                    Console.WriteLine($"{(r.Ok ? "  ok  " : "  FAIL")} {r.Name}: {r.BattleTurns} battle turns, {r.CheckedTurns} checked, {r.IllegalSkipped} illegal skipped, {r.Mismatches.Count} mismatches");
                    if (!r.Ok)
                    {
                        bad++;
                        for (int k = 0; k < Math.Min(r.Mismatches.Count, 12); k++) Console.WriteLine("        " + r.Mismatches[k].Replace("\n", "\n        "));
                    }
                }
            }
            Console.WriteLine($"\n{files} files, {turns} turns checked, {bad} files with mismatches");
            return bad == 0 ? 0 : 1;
        }
    }
}
