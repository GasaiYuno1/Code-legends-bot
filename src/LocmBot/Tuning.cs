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
            foreach (var arg in args)
            {
                int eq = arg.IndexOf('=');
                if (eq <= 0) continue;
                string key = arg.Substring(0, eq).ToLowerInvariant();
                var e = search.Eval;
                if (key.StartsWith("o_"))
                {
                    // веса модели противника: o_hp=0.4 и т.п.
                    if (ReferenceEquals(search.OppEval, search.Eval)) search.OppEval = Clone(search.Eval);
                    e = search.OppEval;
                    key = key.Substring(2);
                }
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
                    case "midhp": e.MidHpW = v; break;
                    case "midhpat": e.MidHp = (int)v; break;
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
                    case "sampled": search.SampledCandidates = (int)v; break;
                    case "hands": search.SampledHands = (int)v; break;
                    case "samplednodes": search.SampledNodes = (int)v; break;
                    case "warmup": search.WarmUpMs = (int)v; break;
                    case "exactlethal": search.ExactLethalNodes = (int)v; break;
                    case "risk": search.LethalRiskCandidates = (int)v; break;
                    case "riskw": search.LethalRiskW = v; break;
                    case "riskhands": search.LethalRiskHands = (int)v; break;
                    case "risknodes": search.LethalRiskNodes = (int)v; break;
                    case "net": search.UseNet = v != 0; break;
                    case "netscale": search.NetScale = v; break;
                    case "netadd": search.NetAdditive = v != 0; break;
                    case "netdeep": search.NetDeepOnly = v != 0; break;
                    case "counternodes": search.CounterNodes = (int)v; break;
                    case "table": CardRating.UseTable = v != 0; break;
                    case "curvew": RatingDraft.CurveW = v; break;
                    case "draftwin": CardTable.UseWinAdjusted = v != 0; break;
                    case "maxitems": RatingDraft.MaxItems = (int)v; break;
                    case "itempenalty": RatingDraft.ItemOverPenalty = v; break;
                    case "samecard": RatingDraft.SameCardPenalty = v; break;
                    default:
                        if (log != null) log.WriteLine("unknown override: " + arg);
                        continue;
                }
                if (log != null) log.WriteLine("override " + (ReferenceEquals(e, search.Eval) ? "" : "o_") + key + "=" + v.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static Evaluator Clone(Evaluator e)
        {
            return new Evaluator
            {
                AttackW = e.AttackW, DefenseW = e.DefenseW, GuardW = e.GuardW, GuardDefW = e.GuardDefW, WardW = e.WardW, WardAtkW = e.WardAtkW,
                LethalW = e.LethalW, DrainAtkW = e.DrainAtkW, BreakthroughAtkW = e.BreakthroughAtkW, ChargeW = e.ChargeW,
                Fragile1W = e.Fragile1W, Fragile2W = e.Fragile2W, BlueHandW = e.BlueHandW, HpW = e.HpW, LowHpW = e.LowHpW, LowHp = e.LowHp, MidHpW = e.MidHpW, MidHp = e.MidHp,
                HandCardW = e.HandCardW, HandRatingW = e.HandRatingW, OppDrawW = e.OppDrawW, MyDrawW = e.MyDrawW,
            };
        }
    }
}
