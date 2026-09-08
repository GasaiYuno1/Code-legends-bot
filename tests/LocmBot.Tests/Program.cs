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
