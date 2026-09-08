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

        public double AttackW = 1.0;
        public double DefenseW = 0.8;
        public double GuardW = 0.8;
        public double GuardDefW = 0.25;      // Guard тем ценнее, чем толще
        public double WardW = 1.2;
        public double WardAtkW = 0.3;        // Ward на большой атаке — почти гарантированный размен
        public double LethalW = 1.5;
        public double DrainAtkW = 0.3;
        public double BreakthroughAtkW = 0.15;
        public double ChargeW = 0.2;
        public double HpW = 0.1;             // за 1 HP (0.4 давало 44% совпадения с ходами Legend, 0.1 — 49%)
        public double LowHpW = 0.8;          // дополнительно за 1 HP ниже LowHp
        public int LowHp = 10;
        public double HandCardW = 1.0;       // карта в руке (не разыгранная) — базовая ценность
        public double HandRatingW = 0.0;     // плюс доля рейтинга карты (CardRating): сильные карты и removal держать дороже
        public double OppDrawW = 1.5;        // каждая лишняя карта противника за пробитые руны
        public double MyDrawW = 1.2;         // мой лишний добор (эффекты карт)

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
            return v;
        }

        public double Health(int hp)
        {
            if (hp <= 0) return -WinScore;
            double v = hp * HpW;
            if (hp < LowHp) v -= (LowHp - hp) * LowHpW;
            return v;
        }

        public double Hand(PlayerState p)
        {
            double v = p.HandCount * HandCardW;
            if (HandRatingW != 0)
                for (int i = 0; i < p.HandKnown; i++) v += HandRatingW * CardRating.Rate(p.Hand[i]);
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
