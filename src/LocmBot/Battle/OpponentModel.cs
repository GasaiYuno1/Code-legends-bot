using System;
using System.Collections.Generic;

namespace Locm
{
    /// <summary>
    /// Модель неизвестных карт противника. Драфт общий: оба игрока выбирают из одних и тех же троек, поэтому его
    /// колода — ровно одна карта из каждой тройки. Показанные карты (SUMMON/USE в его действиях) приписываются тройкам
    /// (паросочетание), пики остальных троек сэмплируются по таблице Legend-пиков (softmax рейтинга, температура
    /// откалибрована по 71k пикам: P ∝ exp(2·r)), рука — случайные карты из ещё не показанных.
    /// Без троек (логи без драфта, прогрев) — запасной вариант: карты из общего пула по рейтингу.
    /// </summary>
    public sealed class OpponentModel
    {
        public const int DeckSize = 30;
        public const double PickTemperature = 0.5;

        private readonly int[][] _triples = new int[DeckSize][];
        private readonly double[][] _cdf = new double[DeckSize][];
        private readonly List<int> _revealed = new List<int>(64);
        private readonly int[] _match = new int[DeckSize];       // индекс показанной карты, приписанной тройке, или -1
        private readonly bool[] _visited = new bool[DeckSize];
        private readonly int[] _unknown = new int[DeckSize];
        private bool _matchDirty = true;
        private double[] _poolCdf;

        /// <summary>Сколько троек драфта известно (30 — вся колода).</summary>
        public int Triples { get; private set; }
        /// <summary>Сколько карт противник показал (SUMMON/USE).</summary>
        public int Revealed => _revealed.Count;
        /// <summary>Показанные карты, которые не удалось приписать ни одной тройке (ошибка модели или ввода).</summary>
        public int Unmatched { get { EnsureMatched(); return _unmatched; } }
        private int _unmatched;
        /// <summary>true — рука сэмплируется из колоды по тройкам, false — из общего пула.</summary>
        public bool DeckKnown => Triples >= DeckSize;

        public OpponentModel()
        {
            for (int i = 0; i < DeckSize; i++) { _triples[i] = new int[3]; _cdf[i] = new double[3]; }
            Reset();
        }

        public void Reset()
        {
            Triples = 0;
            _revealed.Clear();
            _unmatched = 0;
            _matchDirty = true;
        }

        /// <summary>Ход драфта: запомнить тройку и вероятности пика каждой карты.</summary>
        public void ObserveDraft(TurnInput input)
        {
            if (Triples >= DeckSize || input.Cards.Count < 3) return;
            var t = _triples[Triples];
            var cdf = _cdf[Triples];
            double sum = 0;
            for (int j = 0; j < 3; j++)
            {
                t[j] = input.Cards[j].Number;
                double r = CardTable.Picks > 0 && CardDb.Contains(t[j]) ? CardTable.Rating[t[j]] : 0.0;
                sum += Math.Exp(r / PickTemperature);
                cdf[j] = sum;
            }
            for (int j = 0; j < 3; j++) cdf[j] /= sum;
            Triples++;
            _matchDirty = true;
        }

        /// <summary>Ход боя: карты, которые противник сыграл в свой прошлый ход.</summary>
        public void ObserveBattle(TurnInput input)
        {
            for (int i = 0; i < input.OpponentActions.Count; i++)
            {
                var a = input.OpponentActions[i];
                if (a.Action.StartsWith("SUMMON") || a.Action.StartsWith("USE"))
                {
                    _revealed.Add(a.CardNumber);
                    _matchDirty = true;
                }
            }
        }

        /// <summary>Сэмпл руки из n карт (instanceId с instanceBase); возвращает сколько карт положено.</summary>
        public int Sample(ref ulong rng, Card[] hand, int n, int instanceBase)
        {
            if (!DeckKnown) return SampleFromPool(ref rng, hand, n, instanceBase);
            EnsureMatched();
            int u = 0;
            for (int i = 0; i < Triples; i++)
            {
                if (_match[i] >= 0) continue;
                double x = NextDouble(ref rng);
                var cdf = _cdf[i];
                int j = x < cdf[0] ? 0 : (x < cdf[1] ? 1 : 2);
                _unknown[u++] = _triples[i][j];
            }
            int take = Math.Min(n, u);
            for (int i = 0; i < take; i++)
            {
                int j = i + (int)(NextDouble(ref rng) * (u - i));
                if (j >= u) j = u - 1;
                int tmp = _unknown[i]; _unknown[i] = _unknown[j]; _unknown[j] = tmp;
                hand[i] = CardDb.Get(_unknown[i]).WithInstance(instanceBase + i, Location.MyHand);
            }
            return take;
        }

        /// <summary>Сколько карт противника ещё неизвестно (рука + колода) по модели; для сверки с вводом.</summary>
        public int UnknownCount()
        {
            if (!DeckKnown) return -1;
            EnsureMatched();
            int u = 0;
            for (int i = 0; i < Triples; i++) if (_match[i] < 0) u++;
            return u;
        }

        private void EnsureMatched()
        {
            if (!_matchDirty) return;
            _matchDirty = false;
            for (int i = 0; i < DeckSize; i++) _match[i] = -1;
            int matched = 0;
            for (int k = 0; k < _revealed.Count; k++)
            {
                Array.Clear(_visited, 0, _visited.Length);
                if (TryMatch(k)) matched++;
            }
            _unmatched = _revealed.Count - matched;
        }

        // паросочетание Куна: показанная карта k → тройка, содержащая её
        private bool TryMatch(int k)
        {
            int card = _revealed[k];
            for (int i = 0; i < Triples; i++)
            {
                if (_visited[i]) continue;
                var t = _triples[i];
                if (t[0] != card && t[1] != card && t[2] != card) continue;
                _visited[i] = true;
                if (_match[i] < 0 || TryMatch(_match[i]))
                {
                    _match[i] = k;
                    return true;
                }
            }
            return false;
        }

        private int SampleFromPool(ref ulong rng, Card[] hand, int n, int instanceBase)
        {
            if (_poolCdf == null)
            {
                _poolCdf = new double[CardDb.Count];
                double acc = 0;
                for (int i = 0; i < CardDb.Count; i++)
                {
                    double r = CardTable.Picks > 0 ? CardTable.Rating[i + 1] : 0.0;
                    acc += Math.Exp(r / 2.0);
                    _poolCdf[i] = acc;
                }
            }
            for (int i = 0; i < n; i++)
            {
                double u = NextDouble(ref rng) * _poolCdf[CardDb.Count - 1];
                int lo = 0, hi = CardDb.Count - 1;
                while (lo < hi) { int mid = (lo + hi) >> 1; if (_poolCdf[mid] < u) lo = mid + 1; else hi = mid; }
                hand[i] = CardDb.Get(lo + 1).WithInstance(instanceBase + i, Location.MyHand);
            }
            return n;
        }

        private static double NextDouble(ref ulong rng)
        {
            rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17;
            return (rng >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
