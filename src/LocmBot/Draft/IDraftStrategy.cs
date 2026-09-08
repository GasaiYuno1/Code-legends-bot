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
        /// <summary>Желаемое число карт по стоимости 0..7+ в колоде из 30 (по средней колоде Legend: 0.6 1.6 6.4 5.3 5.8 3.5 3.0 3.6).</summary>
        public static double[] TargetCurve = { 0.6, 1.6, 6.4, 5.3, 5.8, 3.5, 3.0, 3.6 };
        // Совпадение с пиками Legend (88k пиков): 0 → 86.5%, 0.1 → 86.4%, 0.6 → 75.7%. Малая поправка почти бесплатна и страхует от колод из одних 6+.
        public static double CurveW = 0.1;
        public static int MaxItems = 8;
        public static double ItemOverPenalty = 3.0;
        public static int MaxSameCard = 2;          // третья копия одной карты — штраф
        public static double SameCardPenalty = 0.0; // Legend третью копию не избегает: со штрафом 1.5 совпадение с их пиками падает на 2%
        /// <summary>Разведка для статистики из self-play: с этой вероятностью пик случайный (0 — выключено).</summary>
        public static double Explore = 0.0;
        private static readonly Random _rng = new Random();

        private readonly int[] _curve = new int[8];

        public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked)
        {
            if (Explore > 0 && _rng.NextDouble() < Explore) return _rng.Next(Math.Min(3, input.Cards.Count));
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
            // недобор считается относительно прогресса драфта: на k-м пике ожидаем Target*k/30 карт в корзине
            double expected = TargetCurve[b] * alreadyPicked.Count / 30.0;
            s += (expected - _curve[b]) * CurveW;
            if (c.IsItem && items >= MaxItems) s -= ItemOverPenalty;
            int copies = 0;
            foreach (var p in alreadyPicked) if (p.Number == c.Number) copies++;
            if (copies >= MaxSameCard) s -= SameCardPenalty;
            return s;
        }

        private static int Bucket(int cost) => cost < 7 ? cost : 7;
    }
}
