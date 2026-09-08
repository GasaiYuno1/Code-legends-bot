using System;
using System.Globalization;
using System.IO;

namespace Locm
{
    public static class Program
    {
        // true — дублировать в stderr весь ввод и ответ каждого хода (лог для сверки симулятора, см. tests/LocmBot.Tests replay).
        private const bool DumpInput = false;

        public static void Main(string[] args)
        {
            // Буферизованный вывод: один WriteLine + Flush на ход.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var search = new SearchBattle();
            ApplyOverrides(args, search, stderr);
            var bot = new Bot(new RatingDraft(), search, stderr, DumpInput ? stderr : null);
            bot.Run(Console.In, stdout);
        }

        /// <summary>
        /// Локальный тюнинг: аргументы вида key=value переопределяют веса (на CodinGame аргументов нет).
        /// Ключи: hp, lowhp, lowhpat, atk, def, guard, ward, lethal, hand, oppdraw, reply, cand.
        /// </summary>
        private static void ApplyOverrides(string[] args, SearchBattle search, TextWriter log)
        {
            var e = search.Eval;
            foreach (var arg in args)
            {
                int eq = arg.IndexOf('=');
                if (eq <= 0) continue;
                string key = arg.Substring(0, eq).ToLowerInvariant();
                double v;
                if (!double.TryParse(arg.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                switch (key)
                {
                    case "hp": e.HpW = v; break;
                    case "lowhp": e.LowHpW = v; break;
                    case "lowhpat": e.LowHp = (int)v; break;
                    case "atk": e.AttackW = v; break;
                    case "def": e.DefenseW = v; break;
                    case "guard": e.GuardW = v; break;
                    case "ward": e.WardW = v; break;
                    case "lethal": e.LethalW = v; break;
                    case "hand": e.HandCardW = v; break;
                    case "oppdraw": e.OppDrawW = v; break;
                    case "reply": search.ReplyWeight = v; break;
                    case "cand": search.MaxCandidates = (int)v; break;
                    default: log.WriteLine("unknown override: " + arg); continue;
                }
                log.WriteLine("override " + key + "=" + v.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
