using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System;

// ===== src/LocmBot/Battle/Evaluator.cs =====
namespace Locm
{
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
        public double HpW = 0.4;             // за 1 HP
        public double LowHpW = 0.8;          // дополнительно за 1 HP ниже LowHp
        public int LowHp = 10;
        public double HandCardW = 1.0;       // карта в руке (не разыгранная) — небольшая ценность
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

        public double Score(GameState s, int me)
        {
            if (s.IsOver) return s.Winner == me ? WinScore : -WinScore;
            var p = s.Players[me];
            var o = s.Players[1 - me];
            double v = 0;
            for (int i = 0; i < p.BoardCount; i++) v += Creature(in p.Board[i]);
            for (int i = 0; i < o.BoardCount; i++) v -= Creature(in o.Board[i]);
            v += Health(p.Health) - Health(o.Health);
            v += p.HandCount * HandCardW - o.HandCount * HandCardW;
            v -= Math.Max(0, o.NextTurnDraw - 1) * OppDrawW;
            v += Math.Max(0, p.NextTurnDraw - 1) * MyDrawW;
            return v;
        }
    }
}

// ===== src/LocmBot/Battle/IBattleStrategy.cs =====
namespace Locm
{
    public interface IBattleStrategy
    {
        string PlayTurn(TurnInput input, TurnClock clock);

        void WarmUp(TurnClock clock);
    }

    public sealed class PassBattle : IBattleStrategy
    {
        public string PlayTurn(TurnInput input, TurnClock clock) => "PASS";
        public void WarmUp(TurnClock clock) { }
    }
}

// ===== src/LocmBot/Battle/SearchBattle.cs =====
namespace Locm
{
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

// ===== src/LocmBot/Bot.cs =====
namespace Locm
{
    public sealed class Bot
    {
        public const int DraftTurns = 30;

        private readonly IDraftStrategy _draft;
        private readonly IBattleStrategy _battle;
        private readonly TextWriter _log;
        private readonly TextWriter _dump;
        private readonly List<Card> _picked = new List<Card>();
        private int _turn;

        public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log) : this(draft, battle, log, null) { }

        public Bot(IDraftStrategy draft, IBattleStrategy battle, TextWriter log, TextWriter dump)
        {
            _draft = draft;
            _battle = battle;
            _log = log;
            _dump = dump;
        }

        public void Run(TextReader input, TextWriter output)
        {
            var recorder = _dump != null ? new RecordingReader(input) : null;
            if (recorder != null) input = recorder;

            while (true)
            {
                string first = input.ReadLine();
                if (first == null) return;
                var clock = new TurnClock((_turn == 0 || _turn == DraftTurns ? TimeLimits.FirstTurnMs : TimeLimits.TurnMs) - TimeLimits.SafetyMarginMs);

                string answer;
                try
                {
                    TurnInput turn = InputParser.ReadTurn(first, input);
                    answer = PlayTurn(turn, clock);
                }
                catch (Exception e)
                {
                    _log.WriteLine("Turn error: " + e.Message);
                    answer = "PASS";
                }

                output.WriteLine(answer);
                output.Flush();
                if (recorder != null)
                {
                    _dump.Write(recorder.Take());
                    _dump.WriteLine("> " + answer);
                    _dump.Flush();
                }
                _log.WriteLine($"turn {_turn} done in {clock.ElapsedMs} ms: {answer}");
                _turn++;
            }
        }

        public string PlayTurn(TurnInput turn, TurnClock clock)
        {
            bool isDraft = _turn < DraftTurns && turn.LooksLikeDraft;
            if (isDraft)
            {
                if (_turn == 0)
                {
                    try { _battle.WarmUp(clock); }
                    catch (Exception e) { _log.WriteLine("WarmUp error: " + e.Message); }
                }
                int idx = _draft.Pick(turn, _picked);
                if (idx < 0 || idx > 2) idx = 0;
                _picked.Add(turn.Cards[idx]);
                return "PICK " + idx;
            }

            string actions = _battle.PlayTurn(turn, clock);
            return string.IsNullOrWhiteSpace(actions) ? "PASS" : actions;
        }

        private sealed class RecordingReader : TextReader
        {
            private readonly TextReader _inner;
            private readonly StringBuilder _buf = new StringBuilder();

            public RecordingReader(TextReader inner) { _inner = inner; }

            public override string ReadLine()
            {
                string line = _inner.ReadLine();
                if (line != null) _buf.Append(line).Append('\n');
                return line;
            }

            public override int Read() => _inner.Read();
            public override int Peek() => _inner.Peek();

            public string Take()
            {
                string s = _buf.ToString();
                _buf.Clear();
                return s;
            }
        }
    }
}

// ===== src/LocmBot/Draft/CardRating.cs =====
namespace Locm
{
    public static class CardRating
    {
        public static double AttackW = 1.0;
        public static double DefenseW = 1.0;
        public static double BodyW = 0.06;         // бонус за «большое тело»: atk*def — 7/4 сильнее двух 3/2
        public static double GuardW = 1.0;
        public static double GuardDefW = 0.2;
        public static double WardW = 1.5;
        public static double WardAtkW = 0.3;
        public static double LethalW = 4.0;        // Lethal = размен с чем угодно
        public static double LethalAtkW = -0.3;    // на большой атаке почти лишний
        public static double ChargeLethalW = 3.5;  // Charge+Lethal = мгновенное удаление любого существа
        public static double DrainAtkW = 0.3;
        public static double BreakthroughAtkW = 0.2;
        public static double ChargeW = 0.8;
        public static double ChargeAtkW = 0.2;
        public static double DrawW = 2.0;
        public static double OppDamageW = 0.4;    // за 1 урона противнику при розыгрыше
        public static double MyHealW = 0.25;      // за 1 своего лечения
        public static double ItemDamageW = 1.4;   // урон предмета по существу, за единицу (с потолком)
        public static int ItemDamageCap = 8;
        public static double ItemRemoveAllW = 2.0;
        public static double ItemRemoveGuardW = 0.5;
        public static double BlueFlexW = 1.0;      // синий с уроном можно бить и в лицо

        public static double Par(int cost) => 2.0 * cost + 2.0;

        public static double Abilities(Abilities a, int attack, int defense)
        {
            double v = 0;
            if ((a & Locm.Abilities.Guard) != 0) v += GuardW + defense * GuardDefW;
            if ((a & Locm.Abilities.Ward) != 0) v += WardW + attack * WardAtkW;
            if ((a & Locm.Abilities.Lethal) != 0) v += LethalW + attack * LethalAtkW;
            if ((a & Locm.Abilities.Drain) != 0) v += attack * DrainAtkW;
            if ((a & Locm.Abilities.Breakthrough) != 0) v += attack * BreakthroughAtkW;
            if ((a & Locm.Abilities.Charge) != 0) v += ChargeW + attack * ChargeAtkW;
            if ((a & (Locm.Abilities.Charge | Locm.Abilities.Lethal)) == (Locm.Abilities.Charge | Locm.Abilities.Lethal)) v += ChargeLethalW;
            return v;
        }

        public static double Effects(Card c)
        {
            double v = c.CardDraw * DrawW;
            if (c.OpponentHealthChange < 0) v += -c.OpponentHealthChange * OppDamageW;
            else v -= c.OpponentHealthChange * OppDamageW;
            v += c.MyHealthChange * MyHealW;
            return v;
        }

        public static double Rate(Card c)
        {
            switch (c.Type)
            {
                case CardType.Creature:
                    return c.Attack * AttackW + c.Defense * DefenseW + c.Attack * c.Defense * BodyW
                           + Abilities(c.Abilities, c.Attack, c.Defense) + Effects(c) - Par(c.Cost);

                case CardType.GreenItem:
                {
                    double v = c.Attack * AttackW + c.Defense * DefenseW + Abilities(c.Abilities, 3, 3) + Effects(c);
                    return v - (1.5 * c.Cost + 1.5);
                }

                case CardType.RedItem:
                case CardType.BlueItem:
                {
                    double dmg = Math.Min(-c.Defense, ItemDamageCap);
                    double v = dmg * ItemDamageW + (-c.Attack) * AttackW * 0.8 + Effects(c);
                    if (c.Abilities == (Locm.Abilities.Breakthrough | Locm.Abilities.Charge | Locm.Abilities.Drain | Locm.Abilities.Guard | Locm.Abilities.Lethal | Locm.Abilities.Ward))
                        v += ItemRemoveAllW;
                    else if ((c.Abilities & Locm.Abilities.Guard) != 0) v += ItemRemoveGuardW;
                    if (c.Type == CardType.BlueItem && c.Defense < 0) v += BlueFlexW;
                    return v - (2.0 * c.Cost + 1.0);
                }
            }
            return 0;
        }
    }
}

// ===== src/LocmBot/Draft/IDraftStrategy.cs =====
namespace Locm
{
    public interface IDraftStrategy
    {
        int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked);
    }

    public sealed class FirstCardDraft : IDraftStrategy
    {
        public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked) => 0;
    }

    public sealed class RatingDraft : IDraftStrategy
    {
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

// ===== src/LocmBot/Game/Abilities.cs =====
namespace Locm
{
    [Flags]
    public enum Abilities : byte
    {
        None = 0,
        Breakthrough = 1 << 0,
        Charge = 1 << 1,
        Drain = 1 << 2,
        Guard = 1 << 3,
        Lethal = 1 << 4,
        Ward = 1 << 5,
    }

    public static class AbilitiesExt
    {
        public static Abilities Parse(string s)
        {
            Abilities a = Abilities.None;
            foreach (char c in s)
            {
                switch (c)
                {
                    case 'B': a |= Abilities.Breakthrough; break;
                    case 'C': a |= Abilities.Charge; break;
                    case 'D': a |= Abilities.Drain; break;
                    case 'G': a |= Abilities.Guard; break;
                    case 'L': a |= Abilities.Lethal; break;
                    case 'W': a |= Abilities.Ward; break;
                }
            }
            return a;
        }

        public static string Format(this Abilities a)
        {
            var sb = new StringBuilder(6);
            sb.Append((a & Abilities.Breakthrough) != 0 ? 'B' : '-');
            sb.Append((a & Abilities.Charge) != 0 ? 'C' : '-');
            sb.Append((a & Abilities.Drain) != 0 ? 'D' : '-');
            sb.Append((a & Abilities.Guard) != 0 ? 'G' : '-');
            sb.Append((a & Abilities.Lethal) != 0 ? 'L' : '-');
            sb.Append((a & Abilities.Ward) != 0 ? 'W' : '-');
            return sb.ToString();
        }

        public static bool Has(this Abilities a, Abilities flag) => (a & flag) != 0;
    }
}

// ===== src/LocmBot/Game/Card.cs =====
namespace Locm
{
    public enum CardType : byte
    {
        Creature = 0,
        GreenItem = 1,
        RedItem = 2,
        BlueItem = 3,
    }

    public enum Location : sbyte
    {
        OpponentBoard = -1,
        MyHand = 0,
        MyBoard = 1,
    }

    public readonly struct Card
    {
        public readonly int Number;        // cardNumber — id карты в наборе из 160 (baseId)
        public readonly int InstanceId;    // уникальный id экземпляра в партии (-1 у карт из CardDb)
        public readonly Location Location;
        public readonly CardType Type;
        public readonly int Cost;
        public readonly int Attack;
        public readonly int Defense;
        public readonly Abilities Abilities;
        public readonly int MyHealthChange;
        public readonly int OpponentHealthChange;
        public readonly int CardDraw;

        public Card(int number, int instanceId, Location location, CardType type, int cost, int attack, int defense,
                    Abilities abilities, int myHealthChange, int opponentHealthChange, int cardDraw)
        {
            Number = number;
            InstanceId = instanceId;
            Location = location;
            Type = type;
            Cost = cost;
            Attack = attack;
            Defense = defense;
            Abilities = abilities;
            MyHealthChange = myHealthChange;
            OpponentHealthChange = opponentHealthChange;
            CardDraw = cardDraw;
        }

        public bool IsCreature => Type == CardType.Creature;
        public bool IsItem => Type != CardType.Creature;

        public Card WithInstance(int instanceId, Location location) =>
            new Card(Number, instanceId, location, Type, Cost, Attack, Defense, Abilities, MyHealthChange, OpponentHealthChange, CardDraw);

        public string ToInputLine() => ToInputLine(Location);

        public string ToInputLine(Location location) =>
            $"{Number} {InstanceId} {(int)location} {(int)Type} {Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";

        public override string ToString() =>
            $"#{Number}/{InstanceId} {Type} {Cost}m {Attack}/{Defense} {Abilities.Format()} hp{MyHealthChange:+0;-0;0}/{OpponentHealthChange:+0;-0;0} draw{CardDraw}";
    }
}

// ===== src/LocmBot/Game/CardDb.cs =====
namespace Locm
{
    public static class CardDb
    {
        public const int Count = 160;

        private static readonly Card[] _cards = Build();
        private static readonly string[] _names = BuildNames();

        public static bool Contains(int baseId) => baseId >= 1 && baseId <= Count;

        public static Card Get(int baseId)
        {
            if (!Contains(baseId)) throw new System.ArgumentOutOfRangeException(nameof(baseId), "unknown card " + baseId);
            return _cards[baseId - 1];
        }

        public static string Name(int baseId) => Contains(baseId) ? _names[baseId - 1] : "?";

        private static Card[] Build()
        {
            var c = new Card[Count];
            c[0] = new Card(1, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.None, 1, 0, 0); // Slimer
            c[1] = new Card(2, -1, Location.MyHand, CardType.Creature, 1, 1, 2, Abilities.None, 0, -1, 0); // Scuttler
            c[2] = new Card(3, -1, Location.MyHand, CardType.Creature, 1, 2, 2, Abilities.None, 0, 0, 0); // Beavrat
            c[3] = new Card(4, -1, Location.MyHand, CardType.Creature, 2, 1, 5, Abilities.None, 0, 0, 0); // Plated Toad
            c[4] = new Card(5, -1, Location.MyHand, CardType.Creature, 2, 4, 1, Abilities.None, 0, 0, 0); // Grime Gnasher
            c[5] = new Card(6, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.None, 0, 0, 0); // Murgling
            c[6] = new Card(7, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.Ward, 0, 0, 0); // Rootkin Sapling
            c[7] = new Card(8, -1, Location.MyHand, CardType.Creature, 2, 2, 3, Abilities.None, 0, 0, 0); // Psyshroom
            c[8] = new Card(9, -1, Location.MyHand, CardType.Creature, 3, 3, 4, Abilities.None, 0, 0, 0); // Corrupted Beavrat
            c[9] = new Card(10, -1, Location.MyHand, CardType.Creature, 3, 3, 1, Abilities.Drain, 0, 0, 0); // Carnivorous Bush
            c[10] = new Card(11, -1, Location.MyHand, CardType.Creature, 3, 5, 2, Abilities.None, 0, 0, 0); // Snowsaur
            c[11] = new Card(12, -1, Location.MyHand, CardType.Creature, 3, 2, 5, Abilities.None, 0, 0, 0); // Woodshroom
            c[12] = new Card(13, -1, Location.MyHand, CardType.Creature, 4, 5, 3, Abilities.None, 1, -1, 0); // Swamp Terror
            c[13] = new Card(14, -1, Location.MyHand, CardType.Creature, 4, 9, 1, Abilities.None, 0, 0, 0); // Fanged Lunger
            c[14] = new Card(15, -1, Location.MyHand, CardType.Creature, 4, 4, 5, Abilities.None, 0, 0, 0); // Pouncing Flailmouth
            c[15] = new Card(16, -1, Location.MyHand, CardType.Creature, 4, 6, 2, Abilities.None, 0, 0, 0); // Wrangler Fish
            c[16] = new Card(17, -1, Location.MyHand, CardType.Creature, 4, 4, 5, Abilities.None, 0, 0, 0); // Ash Walker
            c[17] = new Card(18, -1, Location.MyHand, CardType.Creature, 4, 7, 4, Abilities.None, 0, 0, 0); // Acid Golem
            c[18] = new Card(19, -1, Location.MyHand, CardType.Creature, 5, 5, 6, Abilities.None, 0, 0, 0); // Foulbeast
            c[19] = new Card(20, -1, Location.MyHand, CardType.Creature, 5, 8, 2, Abilities.None, 0, 0, 0); // Hedge Demon
            c[20] = new Card(21, -1, Location.MyHand, CardType.Creature, 5, 6, 5, Abilities.None, 0, 0, 0); // Crested Scuttler
            c[21] = new Card(22, -1, Location.MyHand, CardType.Creature, 6, 7, 5, Abilities.None, 0, 0, 0); // Sigbovak
            c[22] = new Card(23, -1, Location.MyHand, CardType.Creature, 7, 8, 8, Abilities.None, 0, 0, 0); // Titan Cave Hog
            c[23] = new Card(24, -1, Location.MyHand, CardType.Creature, 1, 1, 1, Abilities.None, 0, -1, 0); // Exploding Skitterbug
            c[24] = new Card(25, -1, Location.MyHand, CardType.Creature, 2, 3, 1, Abilities.None, -2, -2, 0); // Spiney Chompleaf
            c[25] = new Card(26, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.None, 0, -1, 0); // Razor Crab
            c[26] = new Card(27, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.None, 2, 0, 0); // Nut Gatherer
            c[27] = new Card(28, -1, Location.MyHand, CardType.Creature, 2, 1, 2, Abilities.None, 0, 0, 1); // Infested Toad
            c[28] = new Card(29, -1, Location.MyHand, CardType.Creature, 2, 2, 1, Abilities.None, 0, 0, 1); // Steelplume Nestling
            c[29] = new Card(30, -1, Location.MyHand, CardType.Creature, 3, 4, 2, Abilities.None, 0, -2, 0); // Venomous Bog Hopper
            c[30] = new Card(31, -1, Location.MyHand, CardType.Creature, 3, 3, 1, Abilities.None, 0, -1, 0); // Woodland Hunter
            c[31] = new Card(32, -1, Location.MyHand, CardType.Creature, 3, 3, 2, Abilities.None, 0, 0, 1); // Sandsplat
            c[32] = new Card(33, -1, Location.MyHand, CardType.Creature, 4, 4, 3, Abilities.None, 0, 0, 1); // Chameleskulk
            c[33] = new Card(34, -1, Location.MyHand, CardType.Creature, 5, 3, 5, Abilities.None, 0, 0, 1); // Eldritch Cyclops
            c[34] = new Card(35, -1, Location.MyHand, CardType.Creature, 6, 5, 2, Abilities.Breakthrough, 0, 0, 1); // Snail-eyed Hulker
            c[35] = new Card(36, -1, Location.MyHand, CardType.Creature, 6, 4, 4, Abilities.None, 0, 0, 2); // Possessed Skull
            c[36] = new Card(37, -1, Location.MyHand, CardType.Creature, 6, 5, 7, Abilities.None, 0, 0, 1); // Eldritch Multiclops
            c[37] = new Card(38, -1, Location.MyHand, CardType.Creature, 1, 1, 3, Abilities.Drain, 0, 0, 0); // Imp
            c[38] = new Card(39, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.Drain, 0, 0, 0); // Voracious Imp
            c[39] = new Card(40, -1, Location.MyHand, CardType.Creature, 3, 2, 3, Abilities.Drain | Abilities.Guard, 0, 0, 0); // Rock Gobbler
            c[40] = new Card(41, -1, Location.MyHand, CardType.Creature, 3, 2, 2, Abilities.Charge | Abilities.Drain, 0, 0, 0); // Blizzard Demon
            c[41] = new Card(42, -1, Location.MyHand, CardType.Creature, 4, 4, 2, Abilities.Drain, 0, 0, 0); // Flying Leech
            c[42] = new Card(43, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Drain, 0, 0, 0); // Screeching Nightmare
            c[43] = new Card(44, -1, Location.MyHand, CardType.Creature, 6, 3, 7, Abilities.Drain | Abilities.Lethal, 0, 0, 0); // Deathstalker
            c[44] = new Card(45, -1, Location.MyHand, CardType.Creature, 6, 6, 5, Abilities.Breakthrough | Abilities.Drain, -3, 0, 0); // Night Howler
            c[45] = new Card(46, -1, Location.MyHand, CardType.Creature, 9, 7, 7, Abilities.Drain, 0, 0, 0); // Soul Devourer
            c[46] = new Card(47, -1, Location.MyHand, CardType.Creature, 2, 1, 5, Abilities.Drain, 0, 0, 0); // Gnipper
            c[47] = new Card(48, -1, Location.MyHand, CardType.Creature, 1, 1, 1, Abilities.Lethal, 0, 0, 0); // Venom Hedgehog
            c[48] = new Card(49, -1, Location.MyHand, CardType.Creature, 2, 1, 2, Abilities.Guard | Abilities.Lethal, 0, 0, 0); // Shiny Prowler
            c[49] = new Card(50, -1, Location.MyHand, CardType.Creature, 3, 3, 2, Abilities.Lethal, 0, 0, 0); // Puff Biter
            c[50] = new Card(51, -1, Location.MyHand, CardType.Creature, 4, 3, 5, Abilities.Lethal, 0, 0, 0); // Elite Bilespitter
            c[51] = new Card(52, -1, Location.MyHand, CardType.Creature, 4, 2, 4, Abilities.Lethal, 0, 0, 0); // Bilespitter
            c[52] = new Card(53, -1, Location.MyHand, CardType.Creature, 4, 1, 1, Abilities.Charge | Abilities.Lethal, 0, 0, 0); // Possessed Abomination
            c[53] = new Card(54, -1, Location.MyHand, CardType.Creature, 3, 2, 2, Abilities.Lethal, 0, 0, 0); // Shadow Biter
            c[54] = new Card(55, -1, Location.MyHand, CardType.Creature, 2, 0, 5, Abilities.Guard, 0, 0, 0); // Hermit Slime
            c[55] = new Card(56, -1, Location.MyHand, CardType.Creature, 4, 2, 7, Abilities.None, 0, 0, 0); // Giant Louse
            c[56] = new Card(57, -1, Location.MyHand, CardType.Creature, 4, 1, 8, Abilities.None, 0, 0, 0); // Dream-Eater
            c[57] = new Card(58, -1, Location.MyHand, CardType.Creature, 6, 5, 6, Abilities.Breakthrough, 0, 0, 0); // Darkscale Predator
            c[58] = new Card(59, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.None, 1, -1, 0); // Sea Ghost
            c[59] = new Card(60, -1, Location.MyHand, CardType.Creature, 7, 4, 8, Abilities.None, 0, 0, 0); // Gritsuck Troll
            c[60] = new Card(61, -1, Location.MyHand, CardType.Creature, 9, 10, 10, Abilities.None, 0, 0, 0); // Alpha Troll
            c[61] = new Card(62, -1, Location.MyHand, CardType.Creature, 12, 12, 12, Abilities.Breakthrough | Abilities.Guard, 0, 0, 0); // Mutant Troll
            c[62] = new Card(63, -1, Location.MyHand, CardType.Creature, 2, 0, 4, Abilities.Guard | Abilities.Ward, 0, 0, 0); // Rootkin Drone
            c[63] = new Card(64, -1, Location.MyHand, CardType.Creature, 2, 1, 1, Abilities.Guard | Abilities.Ward, 0, 0, 0); // Coppershell Tortoise
            c[64] = new Card(65, -1, Location.MyHand, CardType.Creature, 2, 2, 2, Abilities.Ward, 0, 0, 0); // Steelplume Defender
            c[65] = new Card(66, -1, Location.MyHand, CardType.Creature, 5, 5, 1, Abilities.Ward, 0, 0, 0); // Staring Wickerbeast
            c[66] = new Card(67, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Ward, 0, -2, 0); // Flailing Hammerhead
            c[67] = new Card(68, -1, Location.MyHand, CardType.Creature, 6, 7, 5, Abilities.Ward, 0, 0, 0); // Giant Squid
            c[68] = new Card(69, -1, Location.MyHand, CardType.Creature, 3, 4, 4, Abilities.Breakthrough, 0, 0, 0); // Charging Boarhound
            c[69] = new Card(70, -1, Location.MyHand, CardType.Creature, 4, 6, 3, Abilities.Breakthrough, 0, 0, 0); // Murglord
            c[70] = new Card(71, -1, Location.MyHand, CardType.Creature, 4, 3, 2, Abilities.Breakthrough | Abilities.Charge, 0, 0, 0); // Flying Murgling
            c[71] = new Card(72, -1, Location.MyHand, CardType.Creature, 4, 5, 3, Abilities.Breakthrough, 0, 0, 0); // Shuffling Nightmare
            c[72] = new Card(73, -1, Location.MyHand, CardType.Creature, 4, 4, 4, Abilities.Breakthrough, 4, 0, 0); // Bog Bounder
            c[73] = new Card(74, -1, Location.MyHand, CardType.Creature, 5, 5, 4, Abilities.Breakthrough | Abilities.Guard, 0, 0, 0); // Crusher
            c[74] = new Card(75, -1, Location.MyHand, CardType.Creature, 5, 6, 5, Abilities.Breakthrough, 0, 0, 0); // Titan Prowler
            c[75] = new Card(76, -1, Location.MyHand, CardType.Creature, 6, 5, 5, Abilities.Breakthrough | Abilities.Drain, 0, 0, 0); // Crested Chomper
            c[76] = new Card(77, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.Breakthrough, 0, 0, 0); // Lumbering Giant
            c[77] = new Card(78, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Breakthrough, 0, -5, 0); // Shambler
            c[78] = new Card(79, -1, Location.MyHand, CardType.Creature, 8, 8, 8, Abilities.Breakthrough, 0, 0, 0); // Scarlet Colossus
            c[79] = new Card(80, -1, Location.MyHand, CardType.Creature, 8, 8, 8, Abilities.Breakthrough | Abilities.Guard, 0, 0, 1); // Corpse Guzzler
            c[80] = new Card(81, -1, Location.MyHand, CardType.Creature, 9, 6, 6, Abilities.Breakthrough | Abilities.Charge, 0, 0, 0); // Flying Corpse Guzzler
            c[81] = new Card(82, -1, Location.MyHand, CardType.Creature, 7, 5, 5, Abilities.Breakthrough | Abilities.Drain | Abilities.Ward, 0, 0, 0); // Slithering Nightmare
            c[82] = new Card(83, -1, Location.MyHand, CardType.Creature, 0, 1, 1, Abilities.Charge, 0, 0, 0); // Restless Owl
            c[83] = new Card(84, -1, Location.MyHand, CardType.Creature, 2, 1, 1, Abilities.Charge | Abilities.Drain | Abilities.Ward, 0, 0, 0); // Fighter Tick
            c[84] = new Card(85, -1, Location.MyHand, CardType.Creature, 3, 2, 3, Abilities.Charge, 0, 0, 0); // Heartless Crow
            c[85] = new Card(86, -1, Location.MyHand, CardType.Creature, 3, 1, 5, Abilities.Charge, 0, 0, 0); // Crazed Nose-pincher
            c[86] = new Card(87, -1, Location.MyHand, CardType.Creature, 4, 2, 5, Abilities.Charge | Abilities.Guard, 0, 0, 0); // Bloat Demon
            c[87] = new Card(88, -1, Location.MyHand, CardType.Creature, 5, 4, 4, Abilities.Charge, 0, 0, 0); // Abyss Nightmare
            c[88] = new Card(89, -1, Location.MyHand, CardType.Creature, 5, 4, 1, Abilities.Charge, 2, 0, 0); // Boombeak
            c[89] = new Card(90, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Charge, 0, 0, 0); // Eldritch Swooper
            c[90] = new Card(91, -1, Location.MyHand, CardType.Creature, 0, 1, 2, Abilities.Guard, 0, 1, 0); // Flumpy
            c[91] = new Card(92, -1, Location.MyHand, CardType.Creature, 1, 0, 1, Abilities.Guard, 2, 0, 0); // Wurm
            c[92] = new Card(93, -1, Location.MyHand, CardType.Creature, 1, 2, 1, Abilities.Guard, 0, 0, 0); // Spinekid
            c[93] = new Card(94, -1, Location.MyHand, CardType.Creature, 2, 1, 4, Abilities.Guard, 0, 0, 0); // Rootkin Defender
            c[94] = new Card(95, -1, Location.MyHand, CardType.Creature, 2, 2, 3, Abilities.Guard, 0, 0, 0); // Wildum
            c[95] = new Card(96, -1, Location.MyHand, CardType.Creature, 2, 3, 2, Abilities.Guard, 0, 0, 0); // Prairie Protector
            c[96] = new Card(97, -1, Location.MyHand, CardType.Creature, 3, 3, 3, Abilities.Guard, 0, 0, 0); // Turta
            c[97] = new Card(98, -1, Location.MyHand, CardType.Creature, 3, 2, 4, Abilities.Guard, 0, 0, 0); // Lilly Hopper
            c[98] = new Card(99, -1, Location.MyHand, CardType.Creature, 3, 2, 5, Abilities.Guard, 0, 0, 0); // Cave Crab
            c[99] = new Card(100, -1, Location.MyHand, CardType.Creature, 3, 1, 6, Abilities.Guard, 0, 0, 0); // Stalagopod
            c[100] = new Card(101, -1, Location.MyHand, CardType.Creature, 4, 3, 4, Abilities.Guard, 0, 0, 0); // Engulfer
            c[101] = new Card(102, -1, Location.MyHand, CardType.Creature, 4, 3, 3, Abilities.Guard, 0, -1, 0); // Mole Demon
            c[102] = new Card(103, -1, Location.MyHand, CardType.Creature, 4, 3, 6, Abilities.Guard, 0, 0, 0); // Mutating Rootkin
            c[103] = new Card(104, -1, Location.MyHand, CardType.Creature, 4, 4, 4, Abilities.Guard, 0, 0, 0); // Deepwater Shellcrab
            c[104] = new Card(105, -1, Location.MyHand, CardType.Creature, 5, 4, 6, Abilities.Guard, 0, 0, 0); // King Shellcrab
            c[105] = new Card(106, -1, Location.MyHand, CardType.Creature, 5, 5, 5, Abilities.Guard, 0, 0, 0); // Far-reaching Nightmare
            c[106] = new Card(107, -1, Location.MyHand, CardType.Creature, 5, 3, 3, Abilities.Guard, 3, 0, 0); // Worker Shellcrab
            c[107] = new Card(108, -1, Location.MyHand, CardType.Creature, 5, 2, 6, Abilities.Guard, 0, 0, 0); // Rootkin Elder
            c[108] = new Card(109, -1, Location.MyHand, CardType.Creature, 5, 5, 6, Abilities.None, 0, 0, 0); // Elder Engulfer
            c[109] = new Card(110, -1, Location.MyHand, CardType.Creature, 5, 0, 9, Abilities.Guard, 0, 0, 0); // Gargoyle
            c[110] = new Card(111, -1, Location.MyHand, CardType.Creature, 6, 6, 6, Abilities.Guard, 0, 0, 0); // Turta Knight
            c[111] = new Card(112, -1, Location.MyHand, CardType.Creature, 6, 4, 7, Abilities.Guard, 0, 0, 0); // Rootkin Leader
            c[112] = new Card(113, -1, Location.MyHand, CardType.Creature, 6, 2, 4, Abilities.Guard, 4, 0, 0); // Tamed Bilespitter
            c[113] = new Card(114, -1, Location.MyHand, CardType.Creature, 7, 7, 7, Abilities.Guard, 0, 0, 0); // Gargantua
            c[114] = new Card(115, -1, Location.MyHand, CardType.Creature, 8, 5, 5, Abilities.Guard | Abilities.Ward, 0, 0, 0); // Rootkin Warchief
            c[115] = new Card(116, -1, Location.MyHand, CardType.Creature, 12, 8, 8, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0); // Emperor Nightmare
            c[116] = new Card(117, -1, Location.MyHand, CardType.GreenItem, 1, 1, 1, Abilities.Breakthrough, 0, 0, 0); // Protein
            c[117] = new Card(118, -1, Location.MyHand, CardType.GreenItem, 0, 0, 3, Abilities.None, 0, 0, 0); // Royal Helm
            c[118] = new Card(119, -1, Location.MyHand, CardType.GreenItem, 1, 1, 2, Abilities.None, 0, 0, 0); // Serrated Shield
            c[119] = new Card(120, -1, Location.MyHand, CardType.GreenItem, 2, 1, 0, Abilities.Lethal, 0, 0, 0); // Venomfruit
            c[120] = new Card(121, -1, Location.MyHand, CardType.GreenItem, 2, 0, 3, Abilities.None, 0, 0, 1); // Enchanted Hat
            c[121] = new Card(122, -1, Location.MyHand, CardType.GreenItem, 2, 1, 3, Abilities.Guard, 0, 0, 0); // Bolstering Bread
            c[122] = new Card(123, -1, Location.MyHand, CardType.GreenItem, 2, 4, 0, Abilities.None, 0, 0, 0); // Wristguards
            c[123] = new Card(124, -1, Location.MyHand, CardType.GreenItem, 3, 2, 1, Abilities.Drain, 0, 0, 0); // Blood Grapes
            c[124] = new Card(125, -1, Location.MyHand, CardType.GreenItem, 3, 1, 4, Abilities.None, 0, 0, 0); // Healthy Veggies
            c[125] = new Card(126, -1, Location.MyHand, CardType.GreenItem, 3, 2, 3, Abilities.None, 0, 0, 0); // Heavy Shield
            c[126] = new Card(127, -1, Location.MyHand, CardType.GreenItem, 3, 0, 6, Abilities.None, 0, 0, 0); // Imperial Helm
            c[127] = new Card(128, -1, Location.MyHand, CardType.GreenItem, 4, 4, 3, Abilities.None, 0, 0, 0); // Enchanted Cloth
            c[128] = new Card(129, -1, Location.MyHand, CardType.GreenItem, 4, 2, 5, Abilities.None, 0, 0, 0); // Enchanted Leather
            c[129] = new Card(130, -1, Location.MyHand, CardType.GreenItem, 4, 0, 6, Abilities.None, 4, 0, 0); // Helm of Remedy
            c[130] = new Card(131, -1, Location.MyHand, CardType.GreenItem, 4, 4, 1, Abilities.None, 0, 0, 0); // Heavy Gauntlet
            c[131] = new Card(132, -1, Location.MyHand, CardType.GreenItem, 5, 3, 3, Abilities.Breakthrough, 0, 0, 0); // High Protein
            c[132] = new Card(133, -1, Location.MyHand, CardType.GreenItem, 5, 4, 0, Abilities.Ward, 0, 0, 0); // Pie of Power
            c[133] = new Card(134, -1, Location.MyHand, CardType.GreenItem, 4, 2, 2, Abilities.None, 0, 0, 1); // Light The Way
            c[134] = new Card(135, -1, Location.MyHand, CardType.GreenItem, 6, 5, 5, Abilities.None, 0, 0, 0); // Imperial Armour
            c[135] = new Card(136, -1, Location.MyHand, CardType.GreenItem, 0, 1, 1, Abilities.None, 0, 0, 0); // Buckler
            c[136] = new Card(137, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Ward, 0, 0, 0); // Ward
            c[137] = new Card(138, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Guard, 0, 0, 1); // Grow Horns
            c[138] = new Card(139, -1, Location.MyHand, CardType.GreenItem, 4, 0, 0, Abilities.Lethal | Abilities.Ward, 0, 0, 0); // Grow Stingers
            c[139] = new Card(140, -1, Location.MyHand, CardType.GreenItem, 2, 0, 0, Abilities.Charge, 0, 0, 0); // Grow Wings
            c[140] = new Card(141, -1, Location.MyHand, CardType.RedItem, 0, -1, -1, Abilities.None, 0, 0, 0); // Throwing Knife
            c[141] = new Card(142, -1, Location.MyHand, CardType.RedItem, 0, 0, 0, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0); // Staff of Suppression
            c[142] = new Card(143, -1, Location.MyHand, CardType.RedItem, 0, 0, 0, Abilities.Guard, 0, 0, 0); // Pierce Armour
            c[143] = new Card(144, -1, Location.MyHand, CardType.RedItem, 1, 0, -2, Abilities.None, 0, 0, 0); // Rune Axe
            c[144] = new Card(145, -1, Location.MyHand, CardType.RedItem, 3, -2, -2, Abilities.None, 0, 0, 0); // Cursed Sword
            c[145] = new Card(146, -1, Location.MyHand, CardType.RedItem, 4, -2, -2, Abilities.None, 0, -2, 0); // Cursed Scimitar
            c[146] = new Card(147, -1, Location.MyHand, CardType.RedItem, 2, 0, -1, Abilities.None, 0, 0, 1); // Quick Shot
            c[147] = new Card(148, -1, Location.MyHand, CardType.RedItem, 2, 0, -2, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0); // Helm Crusher
            c[148] = new Card(149, -1, Location.MyHand, CardType.RedItem, 3, 0, 0, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 1); // Rootkin Ritual
            c[149] = new Card(150, -1, Location.MyHand, CardType.RedItem, 2, 0, -3, Abilities.None, 0, 0, 0); // Throwing Axe
            c[150] = new Card(151, -1, Location.MyHand, CardType.RedItem, 5, 0, -99, Abilities.Breakthrough | Abilities.Charge | Abilities.Drain | Abilities.Guard | Abilities.Lethal | Abilities.Ward, 0, 0, 0); // Decimate
            c[151] = new Card(152, -1, Location.MyHand, CardType.RedItem, 7, 0, -7, Abilities.None, 0, 0, 1); // Mighty Throwing Axe
            c[152] = new Card(153, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 5, 0, 0); // Healing Potion
            c[153] = new Card(154, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 0, -2, 1); // Poison
            c[154] = new Card(155, -1, Location.MyHand, CardType.BlueItem, 3, 0, -3, Abilities.None, 0, -1, 0); // Scroll of Firebolt
            c[155] = new Card(156, -1, Location.MyHand, CardType.BlueItem, 3, 0, 0, Abilities.None, 3, -3, 0); // Major Life Steal Potion
            c[156] = new Card(157, -1, Location.MyHand, CardType.BlueItem, 3, 0, -1, Abilities.None, 1, 0, 1); // Life Sap Drop
            c[157] = new Card(158, -1, Location.MyHand, CardType.BlueItem, 3, 0, -4, Abilities.None, 0, 0, 0); // Tome of Thunder
            c[158] = new Card(159, -1, Location.MyHand, CardType.BlueItem, 4, 0, -3, Abilities.None, 3, 0, 0); // Vial of Soul Drain
            c[159] = new Card(160, -1, Location.MyHand, CardType.BlueItem, 2, 0, 0, Abilities.None, 2, -2, 0); // Minor Life Steal Potion
            return c;
        }

        private static string[] BuildNames()
        {
            return new[]
            {
                "Slimer",
                "Scuttler",
                "Beavrat",
                "Plated Toad",
                "Grime Gnasher",
                "Murgling",
                "Rootkin Sapling",
                "Psyshroom",
                "Corrupted Beavrat",
                "Carnivorous Bush",
                "Snowsaur",
                "Woodshroom",
                "Swamp Terror",
                "Fanged Lunger",
                "Pouncing Flailmouth",
                "Wrangler Fish",
                "Ash Walker",
                "Acid Golem",
                "Foulbeast",
                "Hedge Demon",
                "Crested Scuttler",
                "Sigbovak",
                "Titan Cave Hog",
                "Exploding Skitterbug",
                "Spiney Chompleaf",
                "Razor Crab",
                "Nut Gatherer",
                "Infested Toad",
                "Steelplume Nestling",
                "Venomous Bog Hopper",
                "Woodland Hunter",
                "Sandsplat",
                "Chameleskulk",
                "Eldritch Cyclops",
                "Snail-eyed Hulker",
                "Possessed Skull",
                "Eldritch Multiclops",
                "Imp",
                "Voracious Imp",
                "Rock Gobbler",
                "Blizzard Demon",
                "Flying Leech",
                "Screeching Nightmare",
                "Deathstalker",
                "Night Howler",
                "Soul Devourer",
                "Gnipper",
                "Venom Hedgehog",
                "Shiny Prowler",
                "Puff Biter",
                "Elite Bilespitter",
                "Bilespitter",
                "Possessed Abomination",
                "Shadow Biter",
                "Hermit Slime",
                "Giant Louse",
                "Dream-Eater",
                "Darkscale Predator",
                "Sea Ghost",
                "Gritsuck Troll",
                "Alpha Troll",
                "Mutant Troll",
                "Rootkin Drone",
                "Coppershell Tortoise",
                "Steelplume Defender",
                "Staring Wickerbeast",
                "Flailing Hammerhead",
                "Giant Squid",
                "Charging Boarhound",
                "Murglord",
                "Flying Murgling",
                "Shuffling Nightmare",
                "Bog Bounder",
                "Crusher",
                "Titan Prowler",
                "Crested Chomper",
                "Lumbering Giant",
                "Shambler",
                "Scarlet Colossus",
                "Corpse Guzzler",
                "Flying Corpse Guzzler",
                "Slithering Nightmare",
                "Restless Owl",
                "Fighter Tick",
                "Heartless Crow",
                "Crazed Nose-pincher",
                "Bloat Demon",
                "Abyss Nightmare",
                "Boombeak",
                "Eldritch Swooper",
                "Flumpy",
                "Wurm",
                "Spinekid",
                "Rootkin Defender",
                "Wildum",
                "Prairie Protector",
                "Turta",
                "Lilly Hopper",
                "Cave Crab",
                "Stalagopod",
                "Engulfer",
                "Mole Demon",
                "Mutating Rootkin",
                "Deepwater Shellcrab",
                "King Shellcrab",
                "Far-reaching Nightmare",
                "Worker Shellcrab",
                "Rootkin Elder",
                "Elder Engulfer",
                "Gargoyle",
                "Turta Knight",
                "Rootkin Leader",
                "Tamed Bilespitter",
                "Gargantua",
                "Rootkin Warchief",
                "Emperor Nightmare",
                "Protein",
                "Royal Helm",
                "Serrated Shield",
                "Venomfruit",
                "Enchanted Hat",
                "Bolstering Bread",
                "Wristguards",
                "Blood Grapes",
                "Healthy Veggies",
                "Heavy Shield",
                "Imperial Helm",
                "Enchanted Cloth",
                "Enchanted Leather",
                "Helm of Remedy",
                "Heavy Gauntlet",
                "High Protein",
                "Pie of Power",
                "Light The Way",
                "Imperial Armour",
                "Buckler",
                "Ward",
                "Grow Horns",
                "Grow Stingers",
                "Grow Wings",
                "Throwing Knife",
                "Staff of Suppression",
                "Pierce Armour",
                "Rune Axe",
                "Cursed Sword",
                "Cursed Scimitar",
                "Quick Shot",
                "Helm Crusher",
                "Rootkin Ritual",
                "Throwing Axe",
                "Decimate",
                "Mighty Throwing Axe",
                "Healing Potion",
                "Poison",
                "Scroll of Firebolt",
                "Major Life Steal Potion",
                "Life Sap Drop",
                "Tome of Thunder",
                "Vial of Soul Drain",
                "Minor Life Steal Potion",
            };
        }
    }
}

// ===== src/LocmBot/Game/InputParser.cs =====
namespace Locm
{
    public static class InputParser
    {
        public static TurnInput ReadTurn(TextReader reader)
        {
            string first = reader.ReadLine();
            if (first == null) return null;
            return ReadTurn(first, reader);
        }

        public static TurnInput ReadTurn(string firstLine, TextReader reader)
        {
            var t = new TurnInput();
            t.Me = ParsePlayer(firstLine);
            t.Opponent = ParsePlayer(ReadRequired(reader));

            var oppLine = Split(ReadRequired(reader));
            t.OpponentHandSize = int.Parse(oppLine[0]);
            int opponentActions = int.Parse(oppLine[1]);
            for (int i = 0; i < opponentActions; i++)
            {
                string line = ReadRequired(reader);
                int space = line.IndexOf(' ');
                t.OpponentActions.Add(new OpponentAction
                {
                    CardNumber = int.Parse(space < 0 ? line : line.Substring(0, space)),
                    Action = space < 0 ? "" : line.Substring(space + 1),
                });
            }

            int cardCount = int.Parse(ReadRequired(reader).Trim());
            for (int i = 0; i < cardCount; i++)
                t.Cards.Add(ParseCard(ReadRequired(reader)));

            return t;
        }

        public static PlayerInfo ParsePlayer(string line)
        {
            var p = Split(line);
            return new PlayerInfo
            {
                Health = int.Parse(p[0]),
                Mana = int.Parse(p[1]),
                DeckSize = int.Parse(p[2]),
                Rune = int.Parse(p[3]),
                Draw = int.Parse(p[4]),
            };
        }

        public static Card ParseCard(string line)
        {
            var p = Split(line);
            return new Card(
                number: int.Parse(p[0]),
                instanceId: int.Parse(p[1]),
                location: (Location)int.Parse(p[2]),
                type: (CardType)int.Parse(p[3]),
                cost: int.Parse(p[4]),
                attack: int.Parse(p[5]),
                defense: int.Parse(p[6]),
                abilities: AbilitiesExt.Parse(p[7]),
                myHealthChange: int.Parse(p[8]),
                opponentHealthChange: int.Parse(p[9]),
                cardDraw: int.Parse(p[10]));
        }

        private static string[] Split(string line) =>
            line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static string ReadRequired(TextReader reader)
        {
            string line = reader.ReadLine();
            if (line == null) throw new EndOfStreamException("Unexpected end of input");
            return line;
        }
    }
}

// ===== src/LocmBot/Game/TurnInput.cs =====
namespace Locm
{
    public struct PlayerInfo
    {
        public int Health;
        public int Mana;
        public int DeckSize;
        public int Rune;      // следующая руна (25/20/15/10/5) или 0
        public int Draw;      // сколько карт игрок доберёт в начале следующего хода

        public override string ToString() => $"hp={Health} mana={Mana} deck={DeckSize} rune={Rune} draw={Draw}";
    }

    public struct OpponentAction
    {
        public int CardNumber;
        public string Action;

        public override string ToString() => $"{CardNumber} {Action}";
    }

    public sealed class TurnInput
    {
        public PlayerInfo Me;
        public PlayerInfo Opponent;
        public int OpponentHandSize;
        public List<OpponentAction> OpponentActions = new List<OpponentAction>();
        public List<Card> Cards = new List<Card>();

        public bool LooksLikeDraft
        {
            get
            {
                if (Me.Mana != 0 || Cards.Count != 3) return false;
                foreach (var c in Cards)
                    if (c.Location != Location.MyHand) return false;
                return true;
            }
        }
    }
}

// ===== src/LocmBot/Program.cs =====
namespace Locm
{
    public static class Program
    {
        private const bool DumpInput = false;

        public static void Main()
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var bot = new Bot(new RatingDraft(), new SearchBattle(), stderr, DumpInput ? stderr : null);
            bot.Run(Console.In, stdout);
        }
    }
}

// ===== src/LocmBot/Sim/Creature.cs =====
namespace Locm
{
    public struct Creature
    {
        public Card Card;
        public int Attack;
        public int Defense;
        public Abilities Abilities;
        public bool CanAttack;
        public bool HasAttacked;   // атаковало в текущем ходу (для Charge через предмет)
        public int MyHealthChange;
        public int OpponentHealthChange;
        public int CardDraw;

        public int InstanceId => Card.InstanceId;
        public int BaseId => Card.Number;

        public bool Has(Abilities flag) => (Abilities & flag) != 0;

        public static Creature Summon(Card card)
        {
            return new Creature
            {
                Card = card,
                Attack = card.Attack,
                Defense = card.Defense,
                Abilities = card.Abilities,
                CanAttack = (card.Abilities & Abilities.Charge) != 0,
                HasAttacked = false,
                MyHealthChange = card.MyHealthChange,
                OpponentHealthChange = card.OpponentHealthChange,
                CardDraw = card.CardDraw,
            };
        }

        public static Creature FromInput(Card card, bool canAttack)
        {
            return new Creature
            {
                Card = card,
                Attack = card.Attack,
                Defense = card.Defense,
                Abilities = card.Abilities,
                CanAttack = canAttack,
                HasAttacked = false,
                MyHealthChange = card.MyHealthChange,
                OpponentHealthChange = card.OpponentHealthChange,
                CardDraw = card.CardDraw,
            };
        }

        public void ClearSummonEffects()
        {
            MyHealthChange = 0;
            OpponentHealthChange = 0;
            CardDraw = 0;
        }

        public string ToInputLine(bool opponentBoard)
        {
            int loc = opponentBoard ? -1 : 1;
            return $"{Card.Number} {Card.InstanceId} {loc} 0 {Card.Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";
        }

        public override string ToString() =>
            $"#{BaseId}/{InstanceId} {Attack}/{Defense} {Abilities.Format()}{(CanAttack ? " ready" : "")}{(HasAttacked ? " attacked" : "")}";
    }
}

// ===== src/LocmBot/Sim/GameAction.cs =====
namespace Locm
{
    public enum ActionType : byte
    {
        Pass = 0,
        Summon = 1,
        Attack = 2,
        Use = 3,
    }

    public readonly struct GameAction : IEquatable<GameAction>
    {
        public readonly ActionType Type;
        public readonly int Id;       // instanceId карты/существа
        public readonly int Target;   // instanceId цели или -1 (лицо игрока); для SUMMON не используется

        public const int Face = -1;

        public GameAction(ActionType type, int id, int target)
        {
            Type = type;
            Id = id;
            Target = target;
        }

        public static readonly GameAction Pass = new GameAction(ActionType.Pass, 0, 0);
        public static GameAction Summon(int id) => new GameAction(ActionType.Summon, id, 0);
        public static GameAction Attack(int id, int target) => new GameAction(ActionType.Attack, id, target);
        public static GameAction Use(int id, int target) => new GameAction(ActionType.Use, id, target);

        public bool IsPass => Type == ActionType.Pass;

        public override string ToString()
        {
            switch (Type)
            {
                case ActionType.Summon: return "SUMMON " + Id;
                case ActionType.Attack: return "ATTACK " + Id + " " + Target;
                case ActionType.Use: return "USE " + Id + " " + Target;
                default: return "PASS";
            }
        }

        public static string Format(IList<GameAction> actions)
        {
            if (actions == null || actions.Count == 0) return "PASS";
            var sb = new StringBuilder();
            for (int i = 0; i < actions.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(actions[i].ToString());
            }
            return sb.ToString();
        }

        public static GameAction Parse(string s)
        {
            GameAction a;
            if (!TryParse(s, out a)) throw new FormatException("invalid action: '" + s + "'");
            return a;
        }

        public static bool TryParse(string s, out GameAction action)
        {
            action = Pass;
            if (s == null) return false;
            var p = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return false;
            int id, target;
            switch (p[0])
            {
                case "PASS":
                    return true;
                case "SUMMON":
                    if (p.Length < 2 || !int.TryParse(p[1], out id)) return false;
                    action = Summon(id);
                    return true;
                case "ATTACK":
                case "USE":
                    if (p.Length < 3 || !int.TryParse(p[1], out id) || !int.TryParse(p[2], out target)) return false;
                    action = p[0] == "ATTACK" ? Attack(id, target) : Use(id, target);
                    return true;
                default:
                    return false;
            }
        }

        public static List<GameAction> ParseSequence(string line)
        {
            var result = new List<GameAction>();
            if (line == null) return result;
            foreach (var part in line.Split(';'))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;
                result.Add(Parse(s));
            }
            return result;
        }

        public bool Equals(GameAction o) => Type == o.Type && Id == o.Id && Target == o.Target;
        public override bool Equals(object obj) => obj is GameAction && Equals((GameAction)obj);
        public override int GetHashCode() => ((int)Type * 397 + Id) * 397 + Target;
        public static bool operator ==(GameAction a, GameAction b) => a.Equals(b);
        public static bool operator !=(GameAction a, GameAction b) => !a.Equals(b);
    }
}

// ===== src/LocmBot/Sim/GameState.cs =====
namespace Locm
{
    public sealed class GameState
    {
        public const int MaxHand = 8;
        public const int MaxBoard = 6;
        public const int MaxMana = 12;
        public const int InitialHealth = 30;
        public const int PlayerTurnLimit = 50;

        public readonly PlayerState[] Players = { new PlayerState(), new PlayerState() };
        public int Current;
        public int Winner = -1;
        public int Turn;

        public PlayerState Me => Players[Current];
        public PlayerState Opp => Players[1 - Current];
        public bool IsOver => Winner >= 0;

        public static int RefereeTurn(int myBattleTurn, bool secondPlayer) => 2 * myBattleTurn + (secondPlayer ? 1 : 0);

        public static bool IsSecondPlayer(TurnInput input)
        {
            foreach (var c in input.Cards)
                if (c.Location != Location.OpponentBoard) return c.InstanceId % 2 == 0;
            foreach (var c in input.Cards)
                return c.InstanceId % 2 != 0;
            return false;
        }

        public static GameState FromInput(TurnInput input) => FromInput(input, 0);

        public static GameState FromInput(TurnInput input, int turn) => new GameState().Load(input, turn);

        public GameState Load(TurnInput input, int turn)
        {
            var s = this;
            var me = s.Players[0];
            var opp = s.Players[1];
            me.Reset();
            opp.Reset();
            s.Winner = -1;

            me.Health = input.Me.Health;
            me.MaxMana = input.Me.Mana;      // арбитр присылает maxMana; в начале хода currentMana == maxMana
            me.Mana = input.Me.Mana;
            me.DeckSize = input.Me.DeckSize;
            me.NextRune = input.Me.Rune;
            me.DrawShown = input.Me.Draw;    // сколько карт я добрал в начале этого хода
            me.NextTurnDraw = 1;

            opp.Health = input.Opponent.Health;
            opp.MaxMana = input.Opponent.Mana;
            opp.Mana = 0;
            opp.DeckSize = input.Opponent.DeckSize;
            opp.NextRune = input.Opponent.Rune;
            opp.DrawShown = input.Opponent.Draw;
            opp.NextTurnDraw = input.Opponent.Draw;   // столько противник доберёт (плюс руны, которые я пробью)
            opp.HandCount = input.OpponentHandSize;

            foreach (var c in input.Cards)
            {
                switch (c.Location)
                {
                    case Location.MyHand: me.AddHandCard(c); break;
                    case Location.MyBoard: me.AddCreature(Creature.FromInput(c, true)); break;
                    case Location.OpponentBoard: opp.AddCreature(Creature.FromInput(c, false)); break;
                }
            }

            s.Current = 0;
            s.Turn = turn;
            s.CheckWinCondition();
            return s;
        }


        public ulong Hash()
        {
            ulong h = Mix((ulong)(uint)(Current | (Winner + 1) << 2 | Turn << 4));
            for (int p = 0; p < 2; p++)
            {
                var pl = Players[p];
                ulong salt = (ulong)(p + 1) * 0x9E3779B97F4A7C15UL;
                h ^= Mix(salt ^ (ulong)(uint)(pl.Health & 0xFFFF | (pl.Mana & 0xFF) << 16 | (pl.MaxMana & 0xFF) << 24));
                h ^= Mix(salt + 1 ^ (ulong)(uint)(pl.DeckSize & 0xFF | (pl.NextRune & 0xFF) << 8 | (pl.NextTurnDraw & 0xFF) << 16 | (pl.HandCount & 0xFF) << 24));
                for (int i = 0; i < pl.HandKnown; i++)
                    h ^= Mix(salt + 2 ^ (ulong)(uint)pl.Hand[i].InstanceId);
                for (int i = 0; i < pl.BoardCount; i++)
                {
                    ref Creature c = ref pl.Board[i];
                    ulong packed = (ulong)(uint)(c.InstanceId & 0xFF | (c.Attack & 0xFF) << 8 | (c.Defense & 0xFF) << 16 | (int)c.Abilities << 24)
                                   | (c.CanAttack ? 1UL << 32 : 0) | (c.HasAttacked ? 1UL << 33 : 0);
                    h ^= Mix(salt + 3 ^ packed);
                }
            }
            return h;
        }

        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public void CopyFrom(GameState o)
        {
            Players[0].CopyFrom(o.Players[0]);
            Players[1].CopyFrom(o.Players[1]);
            Current = o.Current;
            Winner = o.Winner;
            Turn = o.Turn;
        }

        public GameState Clone()
        {
            var s = new GameState();
            s.CopyFrom(this);
            return s;
        }


        public void LegalActions(List<GameAction> into)
        {
            into.Clear();
            var me = Me;
            var opp = Opp;

            if (me.BoardCount < MaxBoard)
            {
                for (int i = 0; i < me.HandKnown; i++)
                {
                    if (me.Hand[i].Type != CardType.Creature || me.Hand[i].Cost > me.Mana) continue;
                    into.Add(GameAction.Summon(me.Hand[i].InstanceId));
                }
            }

            bool guards = opp.HasGuard();
            for (int i = 0; i < me.BoardCount; i++)
            {
                if (!me.Board[i].CanAttack) continue;
                int id = me.Board[i].InstanceId;
                if (guards)
                {
                    for (int j = 0; j < opp.BoardCount; j++)
                        if (opp.Board[j].Has(Abilities.Guard)) into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
                }
                else
                {
                    into.Add(GameAction.Attack(id, GameAction.Face));
                    for (int j = 0; j < opp.BoardCount; j++)
                        into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
                }
            }

            for (int i = 0; i < me.HandKnown; i++)
            {
                Card c = me.Hand[i];
                if (c.Type == CardType.Creature || c.Cost > me.Mana) continue;
                if (c.Type == CardType.GreenItem)
                {
                    for (int j = 0; j < me.BoardCount; j++)
                        into.Add(GameAction.Use(c.InstanceId, me.Board[j].InstanceId));
                }
                else
                {
                    for (int j = 0; j < opp.BoardCount; j++)
                        into.Add(GameAction.Use(c.InstanceId, opp.Board[j].InstanceId));
                    if (c.Type == CardType.BlueItem) into.Add(GameAction.Use(c.InstanceId, GameAction.Face));
                }
            }

            into.Add(GameAction.Pass);
        }

        public bool IsLegal(GameAction a)
        {
            var me = Me;
            var opp = Opp;
            switch (a.Type)
            {
                case ActionType.Pass:
                    return true;

                case ActionType.Summon:
                {
                    if (me.BoardCount >= MaxBoard) return false;
                    int i = me.FindHand(a.Id);
                    return i >= 0 && me.Hand[i].Type == CardType.Creature && me.Hand[i].Cost <= me.Mana;
                }

                case ActionType.Attack:
                {
                    int ai = me.FindCreature(a.Id);
                    if (ai < 0 || !me.Board[ai].CanAttack) return false;
                    bool guards = opp.HasGuard();
                    if (a.Target == GameAction.Face) return !guards;
                    int di = opp.FindCreature(a.Target);
                    if (di < 0) return false;
                    return !guards || opp.Board[di].Has(Abilities.Guard);
                }

                case ActionType.Use:
                {
                    int i = me.FindHand(a.Id);
                    if (i < 0) return false;
                    Card c = me.Hand[i];
                    if (c.Type == CardType.Creature || c.Cost > me.Mana) return false;
                    if (c.Type == CardType.GreenItem) return a.Target != GameAction.Face && me.FindCreature(a.Target) >= 0;
                    if (a.Target == GameAction.Face) return c.Type == CardType.BlueItem;
                    return opp.FindCreature(a.Target) >= 0;
                }
            }
            return false;
        }


        public bool TryApply(GameAction a)
        {
            if (!IsLegal(a)) return false;
            Apply(a);
            return true;
        }

        public int ApplySequence(IList<GameAction> actions)
        {
            int illegal = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                if (IsOver) break;
                if (actions[i].IsPass) continue;
                if (!TryApply(actions[i])) illegal++;
            }
            return illegal;
        }

        public void Apply(GameAction a)
        {
            var me = Me;
            var opp = Opp;
            switch (a.Type)
            {
                case ActionType.Pass:
                    return;

                case ActionType.Summon:
                {
                    int hi = me.FindHand(a.Id);
                    if (hi < 0) throw Illegal(a, "card not in hand");
                    Card c = me.Hand[hi];
                    me.RemoveHand(hi);
                    me.Mana -= c.Cost;
                    me.AddCreature(Creature.Summon(c));
                    me.ModifyHealth(c.MyHealthChange);
                    opp.ModifyHealth(c.OpponentHealthChange);
                    me.NextTurnDraw += c.CardDraw;
                    break;
                }

                case ActionType.Attack:
                {
                    int ai = me.FindCreature(a.Id);
                    if (ai < 0) throw Illegal(a, "attacker not on board");
                    Creature att = me.Board[ai];
                    if (a.Target == GameAction.Face)
                    {
                        att.CanAttack = false;
                        att.HasAttacked = true;
                        att.ClearSummonEffects();
                        me.Board[ai] = att;
                        me.ModifyHealth(att.Has(Abilities.Drain) ? att.Attack : 0);
                        opp.ModifyHealth(-att.Attack);
                    }
                    else
                    {
                        int di = opp.FindCreature(a.Target);
                        if (di < 0) throw Illegal(a, "defender not on board");
                        Creature def = opp.Board[di];
                        bool attDied, defDied;
                        int healthGain, healthTaken;
                        ResolveAttack(ref att, ref def, out attDied, out defDied, out healthGain, out healthTaken);
                        if (defDied) opp.RemoveCreature(di); else opp.Board[di] = def;
                        if (attDied) me.RemoveCreature(ai); else me.Board[ai] = att;
                        me.ModifyHealth(healthGain);
                        opp.ModifyHealth(healthTaken);
                    }
                    break;
                }

                case ActionType.Use:
                {
                    int hi = me.FindHand(a.Id);
                    if (hi < 0) throw Illegal(a, "item not in hand");
                    Card item = me.Hand[hi];
                    me.RemoveHand(hi);
                    me.Mana -= item.Cost;

                    if (item.Type == CardType.GreenItem)
                    {
                        int ti = me.FindCreature(a.Target);
                        if (ti < 0) throw Illegal(a, "target not on my board");
                        Creature t = me.Board[ti];
                        bool died = ResolveUse(item, ref t);
                        if (!died) me.Board[ti] = t;
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.OpponentHealthChange);
                    }
                    else if (a.Target == GameAction.Face)
                    {
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.Defense + item.OpponentHealthChange);
                    }
                    else
                    {
                        int ti = opp.FindCreature(a.Target);
                        if (ti < 0) throw Illegal(a, "target not on opponent board");
                        Creature t = opp.Board[ti];
                        bool died = ResolveUse(item, ref t);
                        if (died) opp.RemoveCreature(ti); else opp.Board[ti] = t;
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.OpponentHealthChange);
                    }
                    me.NextTurnDraw += item.CardDraw;
                    break;
                }
            }
            CheckWinCondition();
        }

        private static InvalidOperationException Illegal(GameAction a, string why) =>
            new InvalidOperationException("illegal action " + a + ": " + why);

        public static void ResolveAttack(ref Creature att, ref Creature def,
                                         out bool attackerDied, out bool defenderDied, out int healthGain, out int healthTaken)
        {
            Creature a0 = att;
            Creature d0 = def;

            att.CanAttack = false;
            att.HasAttacked = true;
            att.ClearSummonEffects();
            def.ClearSummonEffects();

            if (d0.Has(Abilities.Ward)) SetWard(ref def, a0.Attack == 0);
            if (a0.Has(Abilities.Ward)) SetWard(ref att, d0.Attack == 0);

            int damageGiven = d0.Has(Abilities.Ward) ? 0 : a0.Attack;
            int damageTaken = a0.Has(Abilities.Ward) ? 0 : d0.Attack;
            healthGain = 0;
            healthTaken = 0;

            defenderDied = damageGiven >= d0.Defense;
            if (a0.Has(Abilities.Breakthrough) && defenderDied) healthTaken = d0.Defense - damageGiven;
            if (a0.Has(Abilities.Lethal) && damageGiven > 0) defenderDied = true;
            if (a0.Has(Abilities.Drain) && damageGiven > 0) healthGain = a0.Attack;
            if (!defenderDied) def.Defense -= damageGiven;

            attackerDied = damageTaken >= a0.Defense;
            if (d0.Has(Abilities.Lethal) && damageTaken > 0) attackerDied = true;
            if (!attackerDied) att.Defense -= damageTaken;
        }

        public static bool ResolveUse(Card item, ref Creature t)
        {
            t.ClearSummonEffects();
            if (item.Type == CardType.GreenItem)
            {
                t.Abilities |= item.Abilities;
                if ((item.Abilities & Abilities.Charge) != 0) t.CanAttack = !t.HasAttacked;
            }
            else
            {
                t.Abilities &= ~item.Abilities;
            }

            t.Attack = Math.Max(0, t.Attack + item.Attack);

            if (t.Has(Abilities.Ward) && item.Defense < 0)
                t.Abilities &= ~Abilities.Ward;      // Ward поглощает урон предмета целиком
            else
                t.Defense += item.Defense;

            return t.Defense <= 0;
        }

        private static void SetWard(ref Creature c, bool on)
        {
            if (on) c.Abilities |= Abilities.Ward; else c.Abilities &= ~Abilities.Ward;
        }

        public void CheckWinCondition()
        {
            if (Opp.Health <= 0) Winner = Current;           // сначала честная победа
            else if (Me.Health <= 0) Winner = 1 - Current;   // потом самоубийство
        }


        public void EndTurn()
        {
            CheckWinCondition();

            var prev = Me;
            for (int i = 0; i < prev.BoardCount; i++)
            {
                prev.Board[i].CanAttack = false;
                prev.Board[i].HasAttacked = false;
            }
            prev.DrawShown = prev.NextTurnDraw;

            Current = 1 - Current;
            Turn++;
            var p = Me;

            if (p.MaxMana < MaxMana + (p.BonusManaTurns > 0 ? 1 : 0))
                p.MaxMana++;

            if (p.BonusManaTurns > 0 && p.Mana == 0)
            {
                p.BonusManaTurns--;
                if (p.BonusManaTurns == 0) p.MaxMana--;
            }

            p.Mana = p.MaxMana;

            for (int i = 0; i < p.BoardCount; i++)
                p.Board[i].CanAttack = true;

            p.DrawCards(p.NextTurnDraw, Turn / 2);
            p.DrawShown = p.NextTurnDraw;
            p.NextTurnDraw = 1;
            CheckWinCondition();
        }


        public string[] ToInputLines()
        {
            var me = Me;
            var opp = Opp;
            var lines = new List<string>(4 + me.HandKnown + me.BoardCount + opp.BoardCount);
            lines.Add(me.ToInputLine());
            lines.Add(opp.ToInputLine());
            lines.Add(opp.HandCount + " 0");
            lines.Add((me.HandKnown + me.BoardCount + opp.BoardCount).ToString());
            for (int i = 0; i < me.HandKnown; i++) lines.Add(me.Hand[i].ToInputLine(Location.MyHand));
            for (int i = 0; i < me.BoardCount; i++) lines.Add(me.Board[i].ToInputLine(false));
            for (int i = 0; i < opp.BoardCount; i++) lines.Add(opp.Board[i].ToInputLine(true));
            return lines.ToArray();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("turn ").Append(Turn).Append(" current P").Append(Current);
            if (IsOver) sb.Append(" winner P").Append(Winner);
            sb.AppendLine();
            for (int p = 0; p < 2; p++)
            {
                var pl = Players[p];
                sb.Append("P").Append(p).Append(": ").Append(pl).AppendLine();
                for (int i = 0; i < pl.HandKnown; i++) sb.Append("   hand ").Append(pl.Hand[i]).AppendLine();
                for (int i = 0; i < pl.BoardCount; i++) sb.Append("   board ").Append(pl.Board[i]).AppendLine();
            }
            return sb.ToString();
        }
    }
}

// ===== src/LocmBot/Sim/PlayerState.cs =====
namespace Locm
{
    public sealed class PlayerState
    {
        public int Health;
        public int MaxMana;
        public int Mana;              // текущая мана (currentMana)
        public int DeckSize;
        public int NextRune;          // следующая руна: 25/20/15/10/5, 0 — рун нет
        public int NextTurnDraw;      // сколько карт доберёт в начале своего следующего хода
        public int DrawShown;         // поле draw из ввода (drawValueToShow арбитра)
        public int BonusManaTurns;    // бонус маны второго игрока: 1 — ещё действует, 0 — нет

        public int HandCount;         // всего карт в руке, включая неизвестные
        public int HandKnown;         // известные карты: Hand[0..HandKnown)
        public readonly Card[] Hand = new Card[GameState.MaxHand];

        public int BoardCount;
        public readonly Creature[] Board = new Creature[GameState.MaxBoard];

        public void Reset()
        {
            Health = 0; MaxMana = 0; Mana = 0; DeckSize = 0; NextRune = 0; NextTurnDraw = 0; DrawShown = 0; BonusManaTurns = 0;
            HandCount = 0; HandKnown = 0; BoardCount = 0;
        }

        public void CopyFrom(PlayerState o)
        {
            Health = o.Health;
            MaxMana = o.MaxMana;
            Mana = o.Mana;
            DeckSize = o.DeckSize;
            NextRune = o.NextRune;
            NextTurnDraw = o.NextTurnDraw;
            DrawShown = o.DrawShown;
            BonusManaTurns = o.BonusManaTurns;
            HandCount = o.HandCount;
            HandKnown = o.HandKnown;
            Array.Copy(o.Hand, Hand, o.HandKnown);
            BoardCount = o.BoardCount;
            Array.Copy(o.Board, Board, o.BoardCount);
        }

        public void ModifyHealth(int mod)
        {
            Health += mod;
            if (mod >= 0) return;
            while (NextRune > 0 && Health <= NextRune)
            {
                NextTurnDraw++;
                NextRune -= 5;
            }
        }

        public void DrawCards(int n, int playerTurn)
        {
            for (int i = 0; i < n; i++)
            {
                if (DeckSize == 0 || playerTurn >= GameState.PlayerTurnLimit)
                {
                    SuicideRunes();
                    continue;
                }
                if (HandCount >= GameState.MaxHand) break;
                DeckSize--;
                HandCount++;
            }
        }

        private void SuicideRunes()
        {
            if (NextRune > 0)
            {
                Health = NextRune;
                NextRune -= 5;
            }
            else
            {
                Health = 0;
            }
        }

        public int FindHand(int instanceId)
        {
            for (int i = 0; i < HandKnown; i++)
                if (Hand[i].InstanceId == instanceId) return i;
            return -1;
        }

        public int FindCreature(int instanceId)
        {
            for (int i = 0; i < BoardCount; i++)
                if (Board[i].InstanceId == instanceId) return i;
            return -1;
        }

        public bool HasGuard()
        {
            for (int i = 0; i < BoardCount; i++)
                if ((Board[i].Abilities & Abilities.Guard) != 0) return true;
            return false;
        }

        public void AddHandCard(Card c)
        {
            if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
            Hand[HandKnown++] = c;
            HandCount++;
        }

        public void RevealHandCard(Card c)
        {
            if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
            Hand[HandKnown++] = c;
            if (HandCount < HandKnown) HandCount = HandKnown;
        }

        public void RemoveHand(int index)
        {
            for (int i = index + 1; i < HandKnown; i++) Hand[i - 1] = Hand[i];
            HandKnown--;
            HandCount--;
        }

        public void AddCreature(Creature c)
        {
            if (BoardCount >= Board.Length) throw new InvalidOperationException("board overflow");
            Board[BoardCount++] = c;
        }

        public void RemoveCreature(int index)
        {
            for (int i = index + 1; i < BoardCount; i++) Board[i - 1] = Board[i];
            BoardCount--;
        }

        public string ToInputLine() => $"{Health} {MaxMana} {DeckSize} {NextRune} {DrawShown}";

        public override string ToString() =>
            $"hp={Health} mana={Mana}/{MaxMana} deck={DeckSize} rune={NextRune} draw={NextTurnDraw} hand={HandKnown}/{HandCount} board={BoardCount}";
    }
}

// ===== src/LocmBot/TurnClock.cs =====
namespace Locm
{
    public sealed class TurnClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public readonly int BudgetMs;

        public TurnClock(int budgetMs) { BudgetMs = budgetMs; }

        public long ElapsedMs => _sw.ElapsedMilliseconds;
        public long RemainingMs => BudgetMs - _sw.ElapsedMilliseconds;
        public bool TimeUp => _sw.ElapsedMilliseconds >= BudgetMs;
    }

    public static class TimeLimits
    {
        public const int FirstTurnMs = 1000;
        public const int TurnMs = 100;
        public const int SafetyMarginMs = 15;
    }
}
