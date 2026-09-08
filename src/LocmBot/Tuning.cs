using System;
using System.Globalization;
using System.IO;

namespace Locm
{
    /// <summary>
    /// Локальный тюнинг: строки вида key=value переопределяют веса оценки и поиска (на CodinGame аргументов нет).
    /// Ключи: hp, lowhp, lowhpat, atk, def, guard, guarddef, ward, wardatk, lethal, drain, hand, oppdraw, mydraw,
    /// reply, cand, table (0 — формульный рейтинг драфта).
    /// </summary>
    public static class Tuning
    {
        public static void Apply(string[] args, SearchBattle search, TextWriter log)
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
                    case "guarddef": e.GuardDefW = v; break;
                    case "ward": e.WardW = v; break;
                    case "wardatk": e.WardAtkW = v; break;
                    case "lethal": e.LethalW = v; break;
                    case "drain": e.DrainAtkW = v; break;
                    case "hand": e.HandCardW = v; break;
                    case "oppdraw": e.OppDrawW = v; break;
                    case "mydraw": e.MyDrawW = v; break;
                    case "reply": search.ReplyWeight = v; break;
                    case "cand": search.MaxCandidates = (int)v; break;
                    case "table": CardRating.UseTable = v != 0; break;
                    default:
                        if (log != null) log.WriteLine("unknown override: " + arg);
                        continue;
                }
                if (log != null) log.WriteLine("override " + key + "=" + v.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
