using System;
using System.Globalization;
using System.IO;

namespace Locm
{
    /// <summary>
    /// Локальный тюнинг: строки вида key=value переопределяют веса оценки и поиска (на CodinGame аргументов нет).
    /// Ключи: hp, lowhp, lowhpat, atk, def, guard, guarddef, ward, wardatk, lethal, drain, hand, oppdraw, mydraw,
    /// handrating, reply, cand, table (0 — формульный рейтинг драфта), curve (8 чисел через запятую), curvew, maxitems, itempenalty, samecard.
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
                if (key == "curve")
                {
                    // curve=0.6,1.6,6.4,5.3,5.8,3.5,3.0,3.6 — целевая мана-кривая драфта
                    var parts = arg.Substring(eq + 1).Split(',');
                    if (parts.Length == 8)
                    {
                        var curve = new double[8];
                        bool ok = true;
                        for (int i = 0; i < 8; i++) ok &= double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out curve[i]);
                        if (ok) RatingDraft.TargetCurve = curve;
                    }
                    continue;
                }
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
                    case "breakthrough": e.BreakthroughAtkW = v; break;
                    case "charge": e.ChargeW = v; break;
                    case "hand": e.HandCardW = v; break;
                    case "handrating": e.HandRatingW = v; break;
                    case "fragile1": e.Fragile1W = v; break;
                    case "fragile2": e.Fragile2W = v; break;
                    case "blue": e.BlueHandW = v; break;
                    case "oppdraw": e.OppDrawW = v; break;
                    case "mydraw": e.MyDrawW = v; break;
                    case "reply": search.ReplyWeight = v; break;
                    case "cand": search.MaxCandidates = (int)v; break;
                    case "deep": search.DeepReplyCandidates = (int)v; break;
                    case "deepnodes": search.DeepReplyNodes = (int)v; break;
                    case "counter": search.CounterCandidates = (int)v; break;
                    case "counternodes": search.CounterNodes = (int)v; break;
                    case "table": CardRating.UseTable = v != 0; break;
                    case "curvew": RatingDraft.CurveW = v; break;
                    case "maxitems": RatingDraft.MaxItems = (int)v; break;
                    case "itempenalty": RatingDraft.ItemOverPenalty = v; break;
                    case "samecard": RatingDraft.SameCardPenalty = v; break;
                    default:
                        if (log != null) log.WriteLine("unknown override: " + arg);
                        continue;
                }
                if (log != null) log.WriteLine("override " + key + "=" + v.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
