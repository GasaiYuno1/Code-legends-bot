using System;
using System.Collections.Generic;

namespace Locm
{
    /// <summary>
    /// Перебор своего хода на симуляторе: DFS по последовательностям действий.
    /// Любой префикс последовательности — допустимый ход, поэтому лучшее состояние ищется среди всех
    /// посещённых узлов. Отсечения: фазы (призывы → предметы → атаки; призыв после атак — только если
    /// освободилось место на столе), дедупликация состояний по хешу, дети упорядочены по оценке
    /// (первая найденная линия — «жадная»), контроль времени по часам хода.
    /// </summary>
    public sealed class SearchBattle : IBattleStrategy
    {
        public const int MaxDepth = 20;

        private struct Child
        {
            public GameAction Action;
            public double Score;
        }

        public readonly Evaluator Eval;
        private readonly GameState[] _pool = new GameState[MaxDepth + 2];
        private readonly List<GameAction>[] _legal = new List<GameAction>[MaxDepth + 1];
        private readonly Child[][] _children = new Child[MaxDepth + 1][];
        private readonly HashSet<ulong> _visited = new HashSet<ulong>();
        private readonly GameAction[] _line = new GameAction[MaxDepth + 1];
        private readonly GameAction[] _bestLine = new GameAction[MaxDepth + 1];
        private readonly List<GameAction> _answer = new List<GameAction>();
        private int _bestLen;
        private double _best;
        private int _rootBoard;
        private TurnClock _clock;
        private bool _stop;
        private long _nodes;
        private int _battleTurn = -1;
        private bool _second;
        private bool _sideKnown;

        /// <summary>Статистика последнего хода.</summary>
        public long Nodes => _nodes;
        public double BestScore => _best;
        public bool TimedOut { get; private set; }

        public SearchBattle() : this(new Evaluator()) { }

        public SearchBattle(Evaluator eval)
        {
            Eval = eval;
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new GameState();
            for (int i = 0; i < _legal.Length; i++)
            {
                _legal[i] = new List<GameAction>(64);
                _children[i] = new Child[128];
            }
        }

        public string PlayTurn(TurnInput input, TurnClock clock)
        {
            _battleTurn++;
            if (!_sideKnown)
            {
                _second = GameState.IsSecondPlayer(input);
                _sideKnown = true;
            }
            _pool[0].Load(input, GameState.RefereeTurn(_battleTurn, _second));
            return GameAction.Format(Search(_pool[0], clock));
        }

        /// <summary>Прогрев JIT: поиск на синтетической позиции; берём не больше 400 мс и оставляем запас на ответ.</summary>
        public void WarmUp(TurnClock clock)
        {
            long budget = Math.Min(400, clock.RemainingMs - 400);
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
            _clock = clock;
            _stop = false;
            TimedOut = false;
            _nodes = 0;
            _visited.Clear();
            if (!ReferenceEquals(root, _pool[0])) _pool[0].CopyFrom(root);
            _rootBoard = _pool[0].Me.BoardCount;
            _best = Eval.Score(_pool[0], _pool[0].Current);
            _bestLen = 0;
            _visited.Add(_pool[0].Hash());
            if (!_pool[0].IsOver) Dfs(0);

            _answer.Clear();
            for (int i = 0; i < _bestLen; i++) _answer.Add(_bestLine[i]);
            return _answer;
        }

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
                    if (child.Winner == me) _stop = true;   // победа найдена — лучше не бывает
                    continue;
                }
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
                if ((++_nodes & 31) == 0 && _clock.TimeUp)
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
