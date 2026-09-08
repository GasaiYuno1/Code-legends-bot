using System;
using System.Collections.Generic;

namespace Locm
{
    public interface IDraftStrategy
    {
        /// <summary>Возвращает индекс (0..2) выбранной карты из предложенной тройки.</summary>
        int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked);
    }

    /// <summary>Заглушка этапа 0: всегда первая карта.</summary>
    public sealed class FirstCardDraft : IDraftStrategy
    {
        public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked) => 0;
    }

    /// <summary>
    /// Драфт по статическому рейтингу (CardRating) с поправкой на мана-кривую и состав колоды (вариант D2):
    /// недобор карт нужной стоимости даёт бонус, перебор — штраф; предметов не больше лимита.
    /// </summary>
    public sealed class RatingDraft : IDraftStrategy
    {
        /// <summary>Желаемое число карт по стоимости 0..7+ в колоде из 30.</summary>
        public readonly int[] TargetCurve = { 0, 4, 7, 6, 5, 4, 2, 2 };
        public double CurveW = 0.6;          // бонус/штраф за карту недобора/перебора
        public int MaxItems = 8;
        public double ItemOverPenalty = 3.0;
        public int MaxSameCard = 2;          // третья копия одной карты — штраф
        public double SameCardPenalty = 1.5;

        private readonly int[] _curve = new int[8];

        public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked)
        {
            Array.Clear(_curve, 0, _curve.Length);
            int items = 0;
            foreach (var c in alreadyPicked)
            {
                _curve[Bucket(c.Cost)]++;
                if (c.IsItem) items++;
            }

            int best = 0;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < input.Cards.Count && i < 3; i++)
            {
                double s = Score(input.Cards[i], alreadyPicked, items);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = i;
                }
            }
            return best;
        }

        public double Score(Card c, IReadOnlyList<Card> alreadyPicked, int items)
        {
            double s = CardRating.Rate(c);
            int b = Bucket(c.Cost);
            s += (TargetCurve[b] - _curve[b]) * CurveW;
            if (c.IsItem && items >= MaxItems) s -= ItemOverPenalty;
            int copies = 0;
            foreach (var p in alreadyPicked) if (p.Number == c.Number) copies++;
            if (copies >= MaxSameCard) s -= SameCardPenalty;
            return s;
        }

        private static int Bucket(int cost) => cost < 7 ? cost : 7;
    }
}
