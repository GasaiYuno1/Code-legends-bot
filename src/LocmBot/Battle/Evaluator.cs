using System;

namespace Locm
{
    /// <summary>
    /// Линейная оценка позиции с точки зрения игрока me: стол (attack/defense + бонусы за способности),
    /// здоровье (нелинейно — дорожим низким HP), карты в руке, лишние доборы от пробитых рун.
    /// Веса — первое приближение по постмортемам; тюнинг self-play — этап 4.
    /// </summary>
    public sealed class Evaluator
    {
        public const double WinScore = 1e6;

        public double AttackW = 0.957;
        public double DefenseW = 0.657;
        public double GuardW = 0.792;
        public double GuardDefW = 0.007;      // Guard тем ценнее, чем толще
        public double WardW = 1.114;
        public double WardAtkW = 0.48;        // Ward на большой атаке — почти гарантированный размен
        public double LethalW = 2.078;
        public double DrainAtkW = 0.222;
        public double BreakthroughAtkW = 0.055;
        public double ChargeW = 0.195;
        public double Fragile1W = 0.013;       // штраф существу с защитой 1 (умирает от чего угодно)
        public double Fragile2W = 0.0;       // штраф существу с защитой 2
        public double BlueHandW = 0.146;       // синий предмет с уроном в руке — запас на летал
        public double HpW = 0.102;             // за 1 HP. SPSA self-play (120 итераций по 1000 партий) — лучший вектор 58.4% против прежних дефолтов. Арена (A/B, только этот вес): 0.1 → 65.5% и 17-е место, 0.3 → 59.1% и 20-е; офлайн-метрики (совпадение с Legend 54.0% против 55.4%) здесь ошиблись
        public double LowHpW = 0.968;          // дополнительно за 1 HP ниже LowHp
        public int LowHp = 10;
        public double MidHpW = 0.047;          // дополнительно за 1 HP ниже MidHp (зона, где начинается гонка)
        public int MidHp = 20;
        public double HandCardW = 1.363;       // карта в руке (не разыгранная) — базовая ценность
        public double HandRatingW = 0.094;     // плюс доля рейтинга карты (CardRating): сильные карты и removal держать дороже
        public double OppDrawW = 1.489;        // каждая лишняя карта противника за пробитые руны
        public double MyDrawW = 1.51;         // мой лишний добор (эффекты карт)

        public double Creature(in Creature c)
        {
            double v = c.Attack * AttackW + c.Defense * DefenseW;
            Abilities a = c.Abilities;
            if ((a & Abilities.Guard) != 0) v += GuardW + c.Defense * GuardDefW;
            if ((a & Abilities.Ward) != 0) v += WardW + c.Attack * WardAtkW;
            if ((a & Abilities.Lethal) != 0) v += LethalW;
            if ((a & Abilities.Drain) != 0) v += c.Attack * DrainAtkW;
            if ((a & Abilities.Breakthrough) != 0) v += c.Attack * BreakthroughAtkW;
            if ((a & Abilities.Charge) != 0) v += ChargeW;
            if (c.Defense <= 1) v -= Fragile1W;
            else if (c.Defense == 2) v -= Fragile2W;
            return v;
        }

        public double Health(int hp)
        {
            if (hp <= 0) return -WinScore;
            double v = hp * HpW;
            if (hp < MidHp) v -= (MidHp - hp) * MidHpW;
            if (hp < LowHp) v -= (LowHp - hp) * LowHpW;
            return v;
        }

        public double Hand(PlayerState p)
        {
            double v = p.HandCount * HandCardW;
            if (HandRatingW != 0 || BlueHandW != 0)
            {
                for (int i = 0; i < p.HandKnown; i++)
                {
                    if (HandRatingW != 0) v += HandRatingW * CardRating.Rate(p.Hand[i]);
                    if (BlueHandW != 0 && p.Hand[i].Type == CardType.BlueItem && p.Hand[i].Defense < 0) v += BlueHandW;
                }
            }
            return v;
        }

        public double Score(GameState s, int me)
        {
            if (s.IsOver) return s.Winner == me ? WinScore : -WinScore;
            var p = s.Players[me];
            var o = s.Players[1 - me];
            double v = 0;
            for (int i = 0; i < p.BoardCount; i++) v += Creature(in p.Board[i]);
            for (int i = 0; i < o.BoardCount; i++) v -= Creature(in o.Board[i]);
            v += Health(p.Health) - Health(o.Health);
            v += Hand(p) - Hand(o);
            v -= Math.Max(0, o.NextTurnDraw - 1) * OppDrawW;
            v += Math.Max(0, p.NextTurnDraw - 1) * MyDrawW;
            return v;
        }
    }
}
