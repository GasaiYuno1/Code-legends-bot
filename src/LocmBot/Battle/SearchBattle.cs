using System;
using System.Collections.Generic;

namespace Locm
{
    /// <summary>
    /// Перебор своего хода на симуляторе в два этапа.
    /// Этап 1 — DFS по последовательностям действий со статической оценкой: фазы призывы → предметы → атаки
    /// (призыв после атак — только в освободившийся слот), дедупликация состояний по хешу, дети по убыванию
    /// оценки, стоп по часам. Любой префикс — допустимый ход, поэтому кандидатами становятся все посещённые
    /// узлы; хранятся MaxCandidates лучших по статической оценке.
    /// Этап 2 — кандидаты по убыванию статики переоцениваются через жадный ответ противника (ReplyScore):
    /// его ход начинается (мана, добор), затем каждое его существо по очереди бьёт лучшую для него цель.
    /// Итог = смесь статики и оценки после ответа.
    /// </summary>
    public sealed class SearchBattle : IBattleStrategy
    {
        public const int MaxDepth = 20;

        private struct Child
        {
            public GameAction Action;
            public double Score;
        }

        private sealed class Candidate
        {
            public readonly GameState State = new GameState();
            public readonly GameAction[] Line = new GameAction[MaxDepth + 1];
            public int Length;
            public double Static;
        }

        public readonly Evaluator Eval;
        /// <summary>Оценка, которой противник выбирает ответ в модели (по умолчанию — копия моей; калибруется по его реальным ответам).</summary>
        public Evaluator OppEval;
        /// <summary>Оценивать листья (после моего хода и после ответа противника) обучаемой сетью NetEval вместо линейной оценки.</summary>
        public bool UseNet = false;
        /// <summary>Масштаб логита сети в единицах линейной оценки (важен только для сравнения с WinScore).</summary>
        public double NetScale = 10.0;
        /// <summary>true — сеть добавляется к линейной оценке (поправка), false — заменяет её.</summary>
        public bool NetAdditive = false;
        /// <summary>true — сеть только на глубоком этапе (как обучалась: листья после полного ответа лучших по статике), без смеси со статикой.</summary>
        public bool NetDeepOnly = true;
        private readonly NetEval _net = new NetEval();
        /// <summary>Доля оценки «после ответа противника» в итоговой (остальное — статика).</summary>
        public double ReplyWeight = 0.95;
        /// <summary>Сколько лучших состояний переоценивать ответом противника.</summary>
        public int MaxCandidates = 1024;
        /// <summary>Доля бюджета времени на этап 1.</summary>
        public double Phase1Share = 0.6;
        /// <summary>Сколько лучших (после жадной переоценки) кандидатов переоценить полным перебором атак противника; 0 — выключено.</summary>
        public int DeepReplyCandidates = 32;
        /// <summary>Лимит узлов перебора атак противника на одного кандидата.</summary>
        public int DeepReplyNodes = 400;
        /// <summary>Сколько лучших кандидатов после глубокого ответа переоценить полным ходом противника с сэмплированной рукой; 0 — выключено.</summary>
        public int SampledCandidates = 0;
        /// <summary>Число образцов руки противника (одинаковые для всех кандидатов хода).</summary>
        public int SampledHands = 2;
        /// <summary>Лимит узлов полного хода противника на один образец.</summary>
        public int SampledNodes = 400;
        /// <summary>Лимит узлов точной проверки летала атаками (только в лицо и по Guard); 0 — только эвристика.</summary>
        public int ExactLethalNodes = 0;   // 200: точный перебор атак в лицо/по Guard подтверждает эвристику; self-play 388:412 (шум) — оставлена эвристика, проверенная ареной
        /// <summary>Сколько лучших кандидатов после глубокого ответа проверить на риск летала по картам противника; 0 — выключено.</summary>
        public int LethalRiskCandidates = 0;
        /// <summary>Число образцов руки противника для риска летала.</summary>
        public int LethalRiskHands = 4;
        /// <summary>Лимит узлов поиска летала на образец (только действия, ведущие к урону в лицо).</summary>
        public int LethalRiskNodes = 300;
        /// <summary>Штраф за долю образцов с леталом (1.0 = летал во всех образцах).</summary>
        public double LethalRiskW = 30.0;
        /// <summary>Бюджет прогрева JIT на первом ходу драфта, мс (0 — без прогрева; для быстрых локальных матчей).</summary>
        public int WarmUpMs = 400;
        /// <summary>Сколько лучших кандидатов после глубокого ответа оценить ещё и моим следующим ходом (3 полухода); 0 — выключено.</summary>
        public int CounterCandidates = 0;   // 8: совпадение с Legend 55.4% → 47.4%, self-play 16:14, до 94 мс — выключено (эффект горизонта: на листьях моего хода нет ответа противника)
        /// <summary>Лимит узлов перебора моего следующего хода на одного кандидата.</summary>
        public int CounterNodes = 1500;

        private readonly GameState[] _pool = new GameState[MaxDepth + 2];
        private readonly List<GameAction>[] _legal = new List<GameAction>[MaxDepth + 1];
        private readonly Child[][] _children = new Child[MaxDepth + 1][];
        private readonly HashSet<ulong> _visited = new HashSet<ulong>();
        private readonly GameAction[] _line = new GameAction[MaxDepth + 1];
        private readonly GameAction[] _bestLine = new GameAction[MaxDepth + 1];
        private readonly List<GameAction> _answer = new List<GameAction>();
        private Candidate[] _heap;            // min-heap по Static
        private int _heapCount;
        private readonly GameState _scratch = new GameState();
        private readonly GameState _tmp = new GameState();
        private readonly GameState _replyBest = new GameState();
        private readonly GameState _sampledBest = new GameState();
        private readonly Card[][] _hands = new Card[8][];
        private readonly int[] _handLen = new int[8];
        private int _preparedHands;
        private ulong _rng;
        /// <summary>Модель колоды/руки противника (тройки драфта + показанные карты).</summary>
        public readonly OpponentModel Opponent = new OpponentModel();
        private int _sampledPlayer;
        private Evaluator _sampledEval;
        private bool _lethalOnly;
        public int RiskScored { get; private set; }
        private readonly GameState[] _cPool = new GameState[MaxDepth + 2];
        private readonly List<GameAction>[] _cLegal = new List<GameAction>[MaxDepth + 1];
        private readonly HashSet<ulong> _cVisited = new HashSet<ulong>();
        private int _cNodes;
        private int _cRootBoard;
        private double _cBest;
        private readonly GameAction[] _cLine = new GameAction[MaxDepth + 2];
        private readonly GameAction[] _cBestLine = new GameAction[MaxDepth + 2];
        private int _cBestLen;
        private readonly GameAction[] _fullBestLine = new GameAction[MaxDepth + 2];
        private int _fullBestLen;
        private readonly GameState[] _oppPool = new GameState[GameState.MaxBoard + 2];
        private readonly List<GameAction>[] _oppLegal = new List<GameAction>[GameState.MaxBoard + 2];
        private readonly HashSet<ulong> _oppVisited = new HashSet<ulong>();
        private readonly double[] _final = new double[4096];
        private int _oppNodes;
        private readonly GameAction[] _oppLine = new GameAction[GameState.MaxBoard + 2];
        private readonly GameAction[] _oppBestLine = new GameAction[GameState.MaxBoard + 2];
        private int _oppBestLen;
        private double _oppBest;
        private double _oppBestMine;
        private int _oppMe;
        private readonly int[] _order = new int[GameState.MaxBoard];
        private readonly int[] _ids = new int[GameState.MaxBoard];
        private int _bestLen;
        private double _best;
        private int _rootBoard;
        private TurnClock _clock;
        private long _phase1Deadline;
        private bool _stop;
        private bool _won;
        private long _nodes;
        private int _battleTurn = -1;
        private bool _second;
        private bool _sideKnown;

        /// <summary>Статистика последнего хода.</summary>
        public long Nodes => _nodes;
        public int Candidates { get; private set; }
        public int Rescored { get; private set; }
        public int DeepRescored { get; private set; }
        public int CounterScored { get; private set; }
        public int SampledScored { get; private set; }
        private readonly int[] _order2 = new int[4096];
        public double BestScore => _best;
        public bool TimedOut { get; private set; }

        public SearchBattle() : this(new Evaluator()) { }

        public SearchBattle(Evaluator eval)
        {
            Eval = eval;
            OppEval = eval;
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new GameState();
            for (int i = 0; i < _legal.Length; i++)
            {
                _legal[i] = new List<GameAction>(64);
                _children[i] = new Child[128];
            }
            for (int i = 0; i < _oppPool.Length; i++)
            {
                _oppPool[i] = new GameState();
                _oppLegal[i] = new List<GameAction>(64);
            }
            for (int i = 0; i < _cPool.Length; i++) _cPool[i] = new GameState();
            for (int i = 0; i < _cLegal.Length; i++) _cLegal[i] = new List<GameAction>(64);
        }

        public string PlayTurn(TurnInput input, TurnClock clock)
        {
            _battleTurn++;
            Opponent.ObserveBattle(input);
            if (!_sideKnown)
            {
                _second = GameState.IsSecondPlayer(input);
                _sideKnown = true;
            }
            _pool[0].Load(input, GameState.RefereeTurn(_battleTurn, _second));
            return GameAction.Format(Search(_pool[0], clock));
        }

        public void ObserveDraft(TurnInput input) => Opponent.ObserveDraft(input);

        /// <summary>Новая партия (compare/self-play в одном процессе): сброс модели противника и счётчика ходов.</summary>
        public void ResetGame()
        {
            Opponent.Reset();
            _battleTurn = -1;
            _sideKnown = false;
        }

        /// <summary>Прогрев JIT: поиск на синтетической позиции; берём не больше 400 мс и оставляем запас на ответ.</summary>
        public void WarmUp(TurnClock clock)
        {
            long budget = Math.Min(WarmUpMs, clock.RemainingMs - 400);
            if (budget < 20) return;
            var input = InputParser.ReadTurn(new System.IO.StringReader(WarmUpPosition));
            _pool[0].Load(input, 10);
            Search(_pool[0], new TurnClock((int)budget));
            _battleTurn = -1;
            _sideKnown = false;
        }

        /// <summary>Лучшая найденная последовательность действий для текущего игрока состояния root (root не меняется).</summary>
        public List<GameAction> Search(GameState root, TurnClock clock)
        {
            EnsureHeap();
            _clock = clock;
            _phase1Deadline = clock.ElapsedMs + (long)(clock.RemainingMs * Phase1Share);
            _stop = false;
            _won = false;
            TimedOut = false;
            _nodes = 0;
            _heapCount = 0;
            Rescored = 0;
            DeepRescored = 0;
            CounterScored = 0;
            SampledScored = 0;
            RiskScored = 0;
            _visited.Clear();
            if (!ReferenceEquals(root, _pool[0])) _pool[0].CopyFrom(root);
            int me = _pool[0].Current;
            _rootBoard = _pool[0].Me.BoardCount;
            _best = Eval.Score(_pool[0], me);
            _bestLen = 0;
            _visited.Add(_pool[0].Hash());

            if (!_pool[0].IsOver)
            {
                _line[0] = GameAction.Pass;
                AddCandidate(_pool[0], _best, 0);
                Dfs(0);
            }
            Candidates = _heapCount;

            if (!_won && !_pool[0].IsOver) Rescore(me);

            _answer.Clear();
            for (int i = 0; i < _bestLen; i++) _answer.Add(_bestLine[i]);
            return _answer;
        }

        // ---------------------------------------------------------------- этап 1

        private void Dfs(int depth)
        {
            if (depth >= MaxDepth) return;
            var s = _pool[depth];
            var child = _pool[depth + 1];
            int me = s.Current;
            var legal = _legal[depth];
            s.LegalActions(legal);
            var kids = _children[depth];
            int n = 0;
            ActionType last = depth == 0 ? ActionType.Pass : _line[depth - 1].Type;

            for (int i = 0; i < legal.Count && n < kids.Length; i++)
            {
                GameAction a = legal[i];
                if (a.IsPass || !AllowedAfter(last, a.Type, s)) continue;
                child.CopyFrom(s);
                child.Apply(a);
                if (!_visited.Add(child.Hash())) continue;

                double v = Eval.Score(child, me);
                _line[depth] = a;
                if (v > _best)
                {
                    _best = v;
                    _bestLen = depth + 1;
                    Array.Copy(_line, _bestLine, _bestLen);
                }
                if (child.IsOver)
                {
                    if (child.Winner == me)
                    {
                        _stop = true;   // победа найдена — лучше не бывает
                        _won = true;
                        _best = v;
                        _bestLen = depth + 1;
                        Array.Copy(_line, _bestLine, _bestLen);
                        return;
                    }
                    continue;
                }
                AddCandidate(child, v, depth + 1);
                kids[n].Action = a;
                kids[n].Score = v;
                n++;
            }
            if (_stop) return;

            // дети по убыванию оценки (n мало — сортировка вставками)
            for (int i = 1; i < n; i++)
            {
                Child k = kids[i];
                int j = i - 1;
                while (j >= 0 && kids[j].Score < k.Score) { kids[j + 1] = kids[j]; j--; }
                kids[j + 1] = k;
            }

            for (int i = 0; i < n; i++)
            {
                if ((++_nodes & 31) == 0 && _clock.ElapsedMs >= _phase1Deadline)
                {
                    TimedOut = true;
                    _stop = true;
                }
                if (_stop) return;
                child.CopyFrom(s);
                child.Apply(kids[i].Action);
                _line[depth] = kids[i].Action;
                Dfs(depth + 1);
            }
        }

        /// <summary>Фазы хода: призывы, затем предметы, затем атаки. Призыв после атаки — только в освободившийся слот.</summary>
        private bool AllowedAfter(ActionType last, ActionType next, GameState s)
        {
            switch (next)
            {
                case ActionType.Summon:
                    if (last == ActionType.Pass || last == ActionType.Summon) return true;
                    return s.Me.BoardCount < _rootBoard;
                case ActionType.Use:
                    return last != ActionType.Attack;
                default:
                    return true;
            }
        }

        // ---------------------------------------------------------------- кандидаты (min-heap по статике)

        private void EnsureHeap()
        {
            if (_heap != null && _heap.Length == MaxCandidates) return;
            _heap = new Candidate[MaxCandidates];
            for (int i = 0; i < _heap.Length; i++) _heap[i] = new Candidate();
            _heapCount = 0;
        }

        private void AddCandidate(GameState state, double score, int lineLen)
        {
            Candidate c;
            if (_heapCount < _heap.Length)
            {
                c = _heap[_heapCount++];
                Fill(c, state, score, lineLen);
                SiftUp(_heapCount - 1);
            }
            else
            {
                if (score <= _heap[0].Static) return;
                c = _heap[0];
                Fill(c, state, score, lineLen);
                SiftDown(0);
            }
        }

        private void Fill(Candidate c, GameState state, double score, int lineLen)
        {
            c.State.CopyFrom(state);
            c.Static = score;
            c.Length = lineLen;
            Array.Copy(_line, c.Line, lineLen);
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_heap[parent].Static <= _heap[i].Static) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _heapCount && _heap[l].Static < _heap[m].Static) m = l;
                if (r < _heapCount && _heap[r].Static < _heap[m].Static) m = r;
                if (m == i) return;
                Swap(m, i);
                i = m;
            }
        }

        private void Swap(int a, int b)
        {
            Candidate t = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = t;
        }

        // ---------------------------------------------------------------- этап 2

        private void Rescore(int me)
        {
            // кандидаты по убыванию статики: heap → отсортированный массив (n ≤ MaxCandidates)
            int n = _heapCount;
            Array.Sort(_heap, 0, n, StaticDesc.Instance);

            double bestFinal = double.NegativeInfinity;
            int bestIdx = -1;
            int scored = 0;
            for (int i = 0; i < n; i++)
            {
                if ((i & 7) == 7 && _clock.TimeUp)
                {
                    TimedOut = true;
                    break;
                }
                Candidate c = _heap[i];
                double reply = ReplyScore(c.State, me);
                double stat = UseNet ? Leaf(c.State, me) : c.Static;
                double final = ReplyWeight * reply + (1 - ReplyWeight) * stat;
                if (i < _final.Length) _final[i] = final;
                scored = i + 1;
                Rescored++;
                if (final > bestFinal)
                {
                    bestFinal = final;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return;   // ни одного не успели — остаётся лучшее по статике

            // уровень 2: лучшие по жадной оценке переоцениваем полным перебором атак противника
            if (DeepReplyCandidates > 0 && !_clock.TimeUp)
            {
                int m = Math.Min(scored, Math.Min(DeepReplyCandidates, _final.Length));
                // индексы m лучших по _final (частичная сортировка выбором — m мало)
                var order = _order2;
                for (int i = 0; i < scored && i < order.Length; i++) order[i] = i;
                int total = Math.Min(scored, order.Length);
                for (int i = 0; i < m; i++)
                {
                    int best = i;
                    for (int j = i + 1; j < total; j++) if (_final[order[j]] > _final[order[best]]) best = j;
                    int t = order[i]; order[i] = order[best]; order[best] = t;
                }
                bestFinal = double.NegativeInfinity;
                bestIdx = -1;
                int deepDone = 0;
                for (int i = 0; i < m; i++)
                {
                    if (_clock.TimeUp) { TimedOut = true; break; }
                    Candidate c = _heap[order[i]];
                    _deepPhase = true;
                    double deep = DeepReplyScore(c.State, me);
                    _deepPhase = false;
                    bool netOnly = UseNet && NetEval.Available && NetDeepOnly;
                    double final = netOnly ? deep : ReplyWeight * deep + (1 - ReplyWeight) * (UseNet ? Leaf(c.State, me) : c.Static);
                    _final[order[i]] = final;
                    DeepRescored++;
                    deepDone = i + 1;
                    if (final > bestFinal)
                    {
                        bestFinal = final;
                        bestIdx = order[i];
                    }
                }
                if (bestIdx < 0) return;

                // уровень 3б: риск летала по картам противника — доля образцов его руки, в которых он выигрывает следующим ходом
                if (LethalRiskCandidates > 0 && LethalRiskHands > 0 && !_clock.TimeUp && !_heap[bestIdx].State.IsOver
                    && _heap[bestIdx].State.Players[1 - me].HandCount + 1 > 0)
                {
                    int k = Math.Min(deepDone, LethalRiskCandidates);
                    for (int i = 0; i < k; i++)
                    {
                        int best = i;
                        for (int j = i + 1; j < deepDone; j++) if (_final[order[j]] > _final[order[best]]) best = j;
                        int t = order[i]; order[i] = order[best]; order[best] = t;
                    }
                    PrepareHands(_heap[order[0]].State, me, LethalRiskHands);
                    double bestR = double.NegativeInfinity;
                    int bestRIdx = -1;
                    for (int i = 0; i < k; i++)
                    {
                        if (_clock.TimeUp) { TimedOut = true; break; }
                        Candidate c = _heap[order[i]];
                        double f = _final[order[i]];
                        if (f > -Evaluator.WinScore / 2 && f < Evaluator.WinScore / 2)
                        {
                            f -= LethalRiskW * LethalRisk(c.State, me);
                            _final[order[i]] = f;
                            RiskScored++;
                        }
                        if (f > bestR)
                        {
                            bestR = f;
                            bestRIdx = order[i];
                        }
                    }
                    if (bestRIdx >= 0)
                    {
                        bestFinal = bestR;
                        bestIdx = bestRIdx;
                    }
                }

                // уровень 3а: лучшие после глубокого ответа — полный ход противника с сэмплированной рукой (expectimax)
                if (SampledCandidates > 0 && SampledHands > 0 && !_clock.TimeUp && !_heap[bestIdx].State.IsOver)
                {
                    int k = Math.Min(deepDone, SampledCandidates);
                    for (int i = 0; i < k; i++)
                    {
                        int best = i;
                        for (int j = i + 1; j < deepDone; j++) if (_final[order[j]] > _final[order[best]]) best = j;
                        int t = order[i]; order[i] = order[best]; order[best] = t;
                    }
                    PrepareHands(_heap[order[0]].State, me, SampledHands);
                    double bestS = double.NegativeInfinity;
                    int bestSIdx = -1;
                    for (int i = 0; i < k; i++)
                    {
                        if (_clock.TimeUp) { TimedOut = true; break; }
                        Candidate c = _heap[order[i]];
                        double sampled = SampledReplyScore(c.State, me);
                        double final = ReplyWeight * sampled + (1 - ReplyWeight) * (UseNet ? Leaf(c.State, me) : c.Static);
                        SampledScored++;
                        if (final > bestS)
                        {
                            bestS = final;
                            bestSIdx = order[i];
                        }
                    }
                    if (bestSIdx >= 0)
                    {
                        bestFinal = bestS;
                        bestIdx = bestSIdx;
                    }
                }

                // уровень 3: лучшие после глубокого ответа — ещё и мой следующий ход (моя рука и мана известны)
                if (CounterCandidates > 0 && !_clock.TimeUp && !_heap[bestIdx].State.IsOver)
                {
                    int k = Math.Min(deepDone, CounterCandidates);
                    for (int i = 0; i < k; i++)
                    {
                        int best = i;
                        for (int j = i + 1; j < deepDone; j++) if (_final[order[j]] > _final[order[best]]) best = j;
                        int t = order[i]; order[i] = order[best]; order[best] = t;
                    }
                    double bestCounter = double.NegativeInfinity;
                    int bestCounterIdx = -1;
                    for (int i = 0; i < k; i++)
                    {
                        if (_clock.TimeUp) { TimedOut = true; break; }
                        Candidate c = _heap[order[i]];
                        DeepReplyScore(c.State, me);           // заново: заполняет _replyBest
                        double counter = CounterScore(me);
                        double final = ReplyWeight * counter + (1 - ReplyWeight) * (UseNet ? Leaf(c.State, me) : c.Static);
                        CounterScored++;
                        if (final > bestCounter)
                        {
                            bestCounter = final;
                            bestCounterIdx = order[i];
                        }
                    }
                    if (bestCounterIdx >= 0)
                    {
                        bestFinal = bestCounter;
                        bestIdx = bestCounterIdx;
                    }
                }
            }
            Candidate b = _heap[bestIdx];
            _best = bestFinal;
            _bestLen = b.Length;
            Array.Copy(b.Line, _bestLine, b.Length);
            _heapCount = 0;
        }

        private sealed class StaticDesc : IComparer<Candidate>
        {
            public static readonly StaticDesc Instance = new StaticDesc();
            public int Compare(Candidate a, Candidate b) => b.Static.CompareTo(a.Static);
        }

        /// <summary>
        /// Оценка позиции после жадного ответа противника: его ход начинается (мана, добор, готовность существ),
        /// сначала быстрая проверка летала по сумме атак минус защита моих Guard, затем каждое его существо
        /// по убыванию атаки бьёт цель, максимизирующую его оценку (или не бьёт). Карты с его руки не учитываются.
        /// </summary>
        public double ReplyScore(GameState after, int me)
        {
            if (after.IsOver) return Leaf(after, me);
            var s = _scratch;
            s.CopyFrom(after);
            s.EndTurn();
            if (s.IsOver) return Leaf(s, me);

            int opp = s.Current;
            var o = s.Players[opp];
            var p = s.Players[me];

            int totalAttack = 0;
            for (int i = 0; i < o.BoardCount; i++) totalAttack += o.Board[i].Attack;
            int guardDefense = 0;
            for (int i = 0; i < p.BoardCount; i++)
                if (p.Board[i].Has(Abilities.Guard)) guardDefense += p.Board[i].Defense;
            // эвристика ошибается при Ward/Lethal/Breakthrough у стражей — подтверждаем точным перебором атак в лицо и по Guard
            if (totalAttack - guardDefense >= p.Health && (ExactLethalNodes == 0 || AttackLethal(after, me, ExactLethalNodes))) return -Evaluator.WinScore;

            // порядок: по убыванию атаки
            int n = o.BoardCount;
            for (int i = 0; i < n; i++) _order[i] = i;
            for (int i = 1; i < n; i++)
            {
                int k = _order[i];
                int j = i - 1;
                while (j >= 0 && o.Board[_order[j]].Attack < o.Board[k].Attack) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = k;
            }
            for (int i = 0; i < n; i++) _ids[i] = o.Board[_order[i]].InstanceId;

            for (int i = 0; i < n && !s.IsOver; i++)
            {
                int ai = o.FindCreature(_ids[i]);
                if (ai < 0 || !o.Board[ai].CanAttack) continue;
                int id = _ids[i];
                double bestSc = OppEval.Score(s, opp);
                int bestTarget = int.MinValue;
                bool guards = p.HasGuard();
                if (!guards)
                {
                    double sc = TryAttack(s, id, GameAction.Face, opp);
                    if (sc > bestSc) { bestSc = sc; bestTarget = GameAction.Face; }
                }
                for (int t = 0; t < p.BoardCount; t++)
                {
                    if (guards && !p.Board[t].Has(Abilities.Guard)) continue;
                    int tid = p.Board[t].InstanceId;
                    double sc = TryAttack(s, id, tid, opp);
                    if (sc > bestSc) { bestSc = sc; bestTarget = tid; }
                }
                if (bestTarget != int.MinValue) s.Apply(GameAction.Attack(id, bestTarget));
            }
            return Leaf(s, me);
        }

        /// <summary>
        /// Оценка после лучшего для противника ответа атаками: полный перебор последовательностей его атак
        /// (с дедупликацией по хешу и лимитом узлов), он может остановиться в любой момент. Карты его руки не учитываются.
        /// </summary>
        public double DeepReplyScore(GameState after, int me)
        {
            if (after.IsOver) return Leaf(after, me);
            var s = _oppPool[0];
            s.CopyFrom(after);
            s.EndTurn();
            if (s.IsOver) return Leaf(s, me);
            int opp = s.Current;
            // точный летал атаками (перебор по OppEval с лимитом узлов может его не найти)
            if (ExactLethalNodes > 0 && AttackLethal(after, me, ExactLethalNodes))
            {
                _replyBest.CopyFrom(_sampledBest);
                _oppBestLen = Math.Min(_cBestLen, _oppBestLine.Length);
                Array.Copy(_cBestLine, _oppBestLine, _oppBestLen);
                return Leaf(_sampledBest, me);
            }
            _oppVisited.Clear();
            _oppVisited.Add(s.Hash());
            _oppNodes = 0;
            _oppMe = me;
            _oppBest = OppEval.Score(s, opp);
            _oppBestMine = Leaf(s, me);
            _replyBest.CopyFrom(s);
            _oppBestLen = 0;
            OppDfs(0, opp);
            return _oppBestMine;
        }

        /// <summary>
        /// Для обучения: кандидаты после моего хода (лучшие topK по статике из этапа 1), копии состояний.
        /// </summary>
        public List<GameState> CollectCandidates(GameState root, TurnClock clock, int topK)
        {
            _clock = clock;
            _phase1Deadline = clock.ElapsedMs + clock.RemainingMs;
            _stop = false;
            _won = false;
            TimedOut = false;
            _nodes = 0;
            _heapCount = 0;
            EnsureHeap();
            _visited.Clear();
            _pool[0].CopyFrom(root);
            int me = _pool[0].Current;
            _rootBoard = _pool[0].Me.BoardCount;
            _best = Eval.Score(_pool[0], me);
            _bestLen = 0;
            _visited.Add(_pool[0].Hash());
            _line[0] = GameAction.Pass;
            AddCandidate(_pool[0], _best, 0);
            Dfs(0);
            int n = _heapCount;
            Array.Sort(_heap, 0, n, StaticDesc.Instance);
            var result = new List<GameState>();
            for (int i = 0; i < n && i < topK; i++)
            {
                if (_heap[i].State.IsOver) continue;
                result.Add(_heap[i].State.Clone());
            }
            _heapCount = 0;
            return result;
        }

        /// <summary>Для обучения: состояние после лучшего (для противника) ответа атаками на моё состояние after; null — партия окончена.</summary>
        public GameState ReplyState(GameState after, int me)
        {
            if (after.IsOver) return null;
            DeepReplyScore(after, me);
            if (_replyBest.IsOver) return null;
            return _replyBest.Clone();
        }

        /// <summary>Оценка листа: терминал — ±WinScore, иначе сеть (если включена) или линейная оценка.</summary>
        public double Leaf(GameState s, int me) => Leaf(s, me, !NetDeepOnly);

        private bool _deepPhase;

        public double Leaf(GameState s, int me, bool allowNet)
        {
            if (s.IsOver || !UseNet || !NetEval.Available || !(allowNet || _deepPhase)) return Eval.Score(s, me);
            double net = NetScale * _net.Logit(s, me);
            return NetAdditive ? Eval.Score(s, me) + net : net;
        }

        /// <summary>Предсказанные атаки противника (линия лучшего для него ответа по OppEval) после моего хода.</summary>
        public void PredictReply(GameState after, int me, List<GameAction> into)
        {
            into.Clear();
            DeepReplyScore(after, me);
            for (int i = 0; i < _oppBestLen; i++) into.Add(_oppBestLine[i]);
        }

        /// <summary>Итоговая оценка позиции после моего хода по модели с сэмплированной рукой (для диагностики).</summary>
        public double SampledFinal(GameState after, int me)
        {
            PrepareHands(after, me, SampledHands);
            double st = UseNet ? Leaf(after, me) : Eval.Score(after, me);
            return ReplyWeight * SampledReplyScore(after, me) + (1 - ReplyWeight) * st;
        }

        /// <summary>Предсказанный полный ход противника (призывы/предметы/атаки) по первому образцу его руки.</summary>
        public void PredictFullReply(GameState after, int me, List<GameAction> into)
        {
            into.Clear();
            if (after.IsOver) return;
            PrepareHands(after, me, SampledHands);
            SampledReplyScore(after, me);
            for (int i = 0; i < _fullBestLen; i++) into.Add(_fullBestLine[i]);
        }

        // ---------------------------------------------------------------- полный ход противника с сэмплированной рукой

        /// <summary>Образцы руки противника на этот ход: карты по силе из таблицы Legend-пиков; одни и те же для всех кандидатов.</summary>
        private void PrepareHands(GameState anyCandidate, int me, int hands)
        {
            _rng = (ulong)anyCandidate.Players[1 - me].HandCount * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL + (ulong)anyCandidate.Turn;
            int n = Math.Min(8, anyCandidate.Players[1 - me].HandCount + 1);   // +1: карта, которую он доберёт
            _preparedHands = Math.Min(hands, _hands.Length);
            for (int k = 0; k < _preparedHands; k++)
            {
                if (_hands[k] == null || _hands[k].Length != n) _hands[k] = new Card[n];
                int got = Opponent.Sample(ref _rng, _hands[k], n, 1000 + k * 16);
                _handLen[k] = got;
            }
        }

        /// <summary>
        /// Среднее по образцам руки: противник получает сэмплированную руку, делает полный ход (призывы → предметы → атаки,
        /// ограниченный перебор по OppEval), лист оценивается моей оценкой.
        /// </summary>
        public double SampledReplyScore(GameState after, int me)
        {
            if (after.IsOver) return Leaf(after, me);
            double sum = 0;
            int samples = 0;
            for (int k = 0; k < _preparedHands && _hands[k] != null; k++)
            {
                var s = _cPool[0];
                s.CopyFrom(after);
                s.EndTurn();
                if (s.IsOver) { sum += Leaf(s, me); samples++; continue; }
                int opp = s.Current;
                var o = s.Players[opp];
                o.HandKnown = 0;
                int give = Math.Min(o.HandCount, _handLen[k]);
                for (int i = 0; i < give; i++) o.Hand[o.HandKnown++] = _hands[k][i];
                _cVisited.Clear();
                _cVisited.Add(s.Hash());
                _cNodes = 0;
                _cRootBoard = o.BoardCount;
                _sampledPlayer = opp;
                _sampledEval = OppEval;
                _cBest = OppEval.Score(s, opp);
                _cBestLen = 0;
                _sampledBest.CopyFrom(s);
                FullDfs(0, ActionType.Pass, opp, SampledNodes);
                if (k == 0) { _fullBestLen = _cBestLen; Array.Copy(_cBestLine, _fullBestLine, _cBestLen); }
                sum += Leaf(_sampledBest, me);
                samples++;
            }
            return samples == 0 ? DeepReplyScore(after, me) : sum / samples;
        }

        /// <summary>
        /// Риск летала по картам: доля подготовленных образцов руки противника, в которых после моего хода он выигрывает
        /// своим следующим ходом. Поиск только по действиям, ведущим к урону в лицо (Charge/урон при призыве, предметы в лицо
        /// и по моим Guard, баффы готовых атаковать, атаки в лицо и по Guard); атаки с существующего стола без карт
        /// уже учтены глубоким ответом, здесь важны карты.
        /// </summary>
        public double LethalRisk(GameState after, int me)
        {
            if (after.IsOver) return after.Winner == me ? 0.0 : 1.0;
            int lethal = 0, samples = 0;
            for (int k = 0; k < _preparedHands && _hands[k] != null; k++)
            {
                var s = _cPool[0];
                s.CopyFrom(after);
                s.EndTurn();
                samples++;
                if (s.IsOver) { if (s.Winner != me) lethal++; continue; }
                int opp = s.Current;
                var o = s.Players[opp];
                o.HandKnown = 0;
                int give = Math.Min(o.HandCount, _handLen[k]);
                for (int i = 0; i < give; i++) o.Hand[o.HandKnown++] = _hands[k][i];
                _cVisited.Clear();
                _cVisited.Add(s.Hash());
                _cNodes = 0;
                _cRootBoard = o.BoardCount;
                _sampledPlayer = opp;
                _sampledEval = OppEval;
                _cBest = OppEval.Score(s, opp);
                _cBestLen = 0;
                _sampledBest.CopyFrom(s);
                _lethalOnly = true;
                FullDfs(0, ActionType.Pass, opp, LethalRiskNodes);
                _lethalOnly = false;
                if (_sampledBest.IsOver && _sampledBest.Winner == opp)
                {
                    lethal++;
                    _fullBestLen = _cBestLen;
                    Array.Copy(_cBestLine, _fullBestLine, _cBestLen);
                }
            }
            return samples == 0 ? 0.0 : (double)lethal / samples;
        }

        /// <summary>
        /// Точная проверка летала атаками с его стола (без карт): перебор только атак в лицо и по моим Guard,
        /// с дедупликацией и лимитом узлов. Состояние after — после моего хода, до его EndTurn.
        /// </summary>
        public bool AttackLethal(GameState after, int me, int nodeCap)
        {
            if (after.IsOver) return after.Winner != me;
            var s = _cPool[0];
            s.CopyFrom(after);
            s.EndTurn();
            if (s.IsOver) return s.Winner != me;
            int opp = s.Current;
            var o = s.Players[opp];
            o.HandKnown = 0;
            _cVisited.Clear();
            _cVisited.Add(s.Hash());
            _cNodes = 0;
            _cRootBoard = o.BoardCount;
            _sampledPlayer = opp;
            _sampledEval = OppEval;
            _cBest = OppEval.Score(s, opp);
            _cBestLen = 0;
            _sampledBest.CopyFrom(s);
            _lethalOnly = true;
            FullDfs(0, ActionType.Pass, opp, nodeCap);
            _lethalOnly = false;
            return _sampledBest.IsOver && _sampledBest.Winner == opp;
        }

        /// <summary>Эвристика уровня 1: сумма его атак минус защита моих Guard ≥ моё HP (после его EndTurn).</summary>
        public bool GreedyLethal(GameState after, int me)
        {
            if (after.IsOver) return after.Winner != me;
            var s = _cPool[0];
            s.CopyFrom(after);
            s.EndTurn();
            if (s.IsOver) return s.Winner != me;
            var o = s.Players[s.Current];
            var p = s.Players[me];
            int totalAttack = 0;
            for (int i = 0; i < o.BoardCount; i++) totalAttack += o.Board[i].Attack;
            int guardDefense = 0;
            for (int i = 0; i < p.BoardCount; i++)
                if (p.Board[i].Has(Abilities.Guard)) guardDefense += p.Board[i].Defense;
            return totalAttack - guardDefense >= p.Health;
        }

        /// <summary>Диагностика: подготовленные образцы руки противника и последняя найденная линия летала.</summary>
        public string DescribeRisk()
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < _preparedHands; k++)
            {
                sb.Append("   hand ").Append(k).Append(':');
                for (int i = 0; i < _handLen[k]; i++) sb.Append(' ').Append(_hands[k][i].Number).Append('/').Append(_hands[k][i].Cost).Append('m');
                sb.AppendLine();
            }
            sb.Append("   lethal line:");
            for (int i = 0; i < _fullBestLen; i++) sb.Append(' ').Append(_fullBestLine[i]).Append(';');
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Риск летала по картам с подготовкой образцов руки (для тестов и диагностики).</summary>
        public double LethalRiskScore(GameState after, int me)
        {
            PrepareHands(after, me, LethalRiskHands);
            return LethalRisk(after, me);
        }

        /// <summary>Действие игрока player, которое может приблизить летал (для _lethalOnly).</summary>
        private static bool LethalUseful(GameState s, GameAction a, int player)
        {
            var p = s.Players[player];
            var enemy = s.Players[1 - player];
            switch (a.Type)
            {
                case ActionType.Summon:
                {
                    int h = p.FindHand(a.Id);
                    if (h < 0) return false;
                    var c = p.Hand[h];
                    return (c.Abilities & Abilities.Charge) != 0 || c.OpponentHealthChange < 0;
                }
                case ActionType.Attack:
                {
                    if (a.Target < 0) return true;
                    int t = enemy.FindCreature(a.Target);
                    return t >= 0 && enemy.Board[t].Has(Abilities.Guard);
                }
                case ActionType.Use:
                {
                    int h = p.FindHand(a.Id);
                    if (h < 0) return false;
                    var c = p.Hand[h];
                    if (c.Type == CardType.GreenItem)
                    {
                        int t = p.FindCreature(a.Target);
                        return t >= 0 && p.Board[t].CanAttack && !p.Board[t].HasAttacked;
                    }
                    if (a.Target < 0) return c.Type == CardType.BlueItem;
                    int e = enemy.FindCreature(a.Target);
                    return e >= 0 && enemy.Board[e].Has(Abilities.Guard);
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Ограниченный перебор полного хода игрока player (фазы как в основном поиске), лучший узел по _sampledEval → _sampledBest.
        /// Дети по убыванию оценки — при лимите узлов это важнее ширины; массивы _children свободны после фазы 1.
        /// </summary>
        private void FullDfs(int depth, ActionType last, int player, int nodeCap)
        {
            if (depth + 1 >= _cPool.Length || depth >= _children.Length) return;
            var s = _cPool[depth];
            var child = _cPool[depth + 1];
            var legal = _cLegal[depth];
            s.LegalActions(legal);
            var kids = _children[depth];
            int n = 0;
            for (int i = 0; i < legal.Count && n < kids.Length; i++)
            {
                GameAction a = legal[i];
                if (a.IsPass || !CounterAllowed(last, a.Type, s)) continue;
                if (_lethalOnly && !LethalUseful(s, a, player)) continue;
                if (_cNodes >= nodeCap) break;
                child.CopyFrom(s);
                child.Apply(a);
                _cNodes++;
                if (!_cVisited.Add(child.Hash())) continue;
                double v = _sampledEval.Score(child, player);
                if (v > _cBest)
                {
                    _cBest = v;
                    _sampledBest.CopyFrom(child);
                    _cLine[depth] = a;
                    _cBestLen = depth + 1;
                    Array.Copy(_cLine, _cBestLine, _cBestLen);
                }
                if (child.IsOver)
                {
                    if (child.Winner == player) { _cNodes = nodeCap; return; }
                    continue;
                }
                kids[n].Action = a;
                kids[n].Score = v;
                n++;
            }
            for (int i = 1; i < n; i++)
            {
                Child k = kids[i];
                int j = i - 1;
                while (j >= 0 && kids[j].Score < k.Score) { kids[j + 1] = kids[j]; j--; }
                kids[j + 1] = k;
            }
            for (int i = 0; i < n; i++)
            {
                if (_cNodes >= nodeCap) return;
                child.CopyFrom(s);
                child.Apply(kids[i].Action);
                _cLine[depth] = kids[i].Action;
                FullDfs(depth + 1, kids[i].Action.Type, player, nodeCap);
            }
        }

        /// <summary>
        /// Оценка после моего следующего хода из состояния _replyBest (лучший для противника ответ): его ход
        /// завершается (мой добор неизвестен — только счётчик), затем ограниченный перебор моего хода
        /// (те же фазы, что в основном поиске), лучший узел по статике.
        /// </summary>
        public double CounterScore(int me)
        {
            var s = _cPool[0];
            s.CopyFrom(_replyBest);
            if (s.IsOver) return Eval.Score(s, me);
            s.EndTurn();
            if (s.IsOver) return Eval.Score(s, me);
            if (s.Current != me) return Eval.Score(s, me);
            _cVisited.Clear();
            _cVisited.Add(s.Hash());
            _cNodes = 0;
            _cRootBoard = s.Me.BoardCount;
            _cBest = Eval.Score(s, me);
            CounterDfs(0, ActionType.Pass, me);
            return _cBest;
        }

        private void CounterDfs(int depth, ActionType last, int me)
        {
            if (depth + 1 >= _cPool.Length) return;
            var s = _cPool[depth];
            var child = _cPool[depth + 1];
            var legal = _cLegal[depth];
            s.LegalActions(legal);
            for (int i = 0; i < legal.Count; i++)
            {
                GameAction a = legal[i];
                if (a.IsPass || !CounterAllowed(last, a.Type, s)) continue;
                if (_cNodes >= CounterNodes) return;
                child.CopyFrom(s);
                child.Apply(a);
                _cNodes++;
                if (!_cVisited.Add(child.Hash())) continue;
                double v = Eval.Score(child, me);
                if (v > _cBest) _cBest = v;
                if (child.IsOver)
                {
                    if (child.Winner == me) { _cNodes = CounterNodes; return; }
                    continue;
                }
                CounterDfs(depth + 1, a.Type, me);
            }
        }

        private bool CounterAllowed(ActionType last, ActionType next, GameState s)
        {
            switch (next)
            {
                case ActionType.Summon:
                    if (last == ActionType.Pass || last == ActionType.Summon) return true;
                    return s.Players[s.Current].BoardCount < _cRootBoard;
                case ActionType.Use:
                    return last != ActionType.Attack;
                default:
                    return true;
            }
        }

        private void OppDfs(int depth, int opp)
        {
            if (depth + 1 >= _oppPool.Length) return;
            var s = _oppPool[depth];
            var child = _oppPool[depth + 1];
            var legal = _oppLegal[depth];
            s.LegalActions(legal);
            for (int i = 0; i < legal.Count; i++)
            {
                GameAction a = legal[i];
                if (a.Type != ActionType.Attack) continue;
                if (_oppNodes >= DeepReplyNodes) return;
                child.CopyFrom(s);
                child.Apply(a);
                _oppNodes++;
                if (!_oppVisited.Add(child.Hash())) continue;
                _oppLine[depth] = a;
                double v = OppEval.Score(child, opp);
                if (v > _oppBest)
                {
                    _oppBest = v;
                    _oppBestMine = Leaf(child, _oppMe);   // итог всегда моей оценкой (листовой)
                    _replyBest.CopyFrom(child);
                    _oppBestLen = depth + 1;
                    Array.Copy(_oppLine, _oppBestLine, _oppBestLen);
                }
                if (child.IsOver) continue;
                OppDfs(depth + 1, opp);
            }
        }

        private double TryAttack(GameState s, int id, int target, int opp)
        {
            _tmp.CopyFrom(s);
            _tmp.Apply(GameAction.Attack(id, target));
            return OppEval.Score(_tmp, opp);
        }

        // Средняя позиция: 5 карт в руке, по 3 существа на столе, предметы — чтобы JIT собрал все ветки.
        private const string WarmUpPosition =
            "22 8 15 20 1\n" +
            "19 7 14 15 1\n" +
            "4 0\n" +
            "11\n" +
            "18 12 0 0 4 7 4 ------ 0 0 0\n" +
            "148 15 0 2 2 0 -2 BCDGLW 0 0 0\n" +
            "124 17 0 1 3 2 1 --D--- 0 0 0\n" +
            "158 19 0 3 3 0 -4 ------ 0 0 0\n" +
            "53 21 0 0 4 1 1 -C--L- 0 0 0\n" +
            "3 9 1 0 1 2 2 ------ 0 0 0\n" +
            "104 11 1 0 4 4 1 --D--W 0 0 0\n" +
            "110 13 1 0 5 0 9 ---G-- 0 0 0\n" +
            "7 8 -1 0 2 2 2 -----W 0 0 0\n" +
            "115 10 -1 0 8 5 5 ---G-W 0 0 0\n" +
            "20 14 -1 0 5 8 2 ------ 0 0 0\n";
    }
}
