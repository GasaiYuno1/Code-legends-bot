using System;

namespace Locm
{
    /// <summary>
    /// Состояние одного игрока (аналог Gamer арбитра). Колода и рука противника — только счётчики:
    /// карты в руке известны лишь у самого бота (и те карты противника, которые он уже разыграл).
    /// </summary>
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

        /// <summary>Gamer.ModifyHealth: при уроне снимаются все руны, до которых опустилось здоровье, +1 добор за каждую.</summary>
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

        /// <summary>
        /// Gamer.DrawCards: за каждую карту, которую нельзя взять из пустой колоды (или после 50-го хода игрока),
        /// теряется руна и здоровье падает до её значения; при полной руке остаток добора сгорает.
        /// Взятые карты неизвестны — растёт только HandCount.
        /// </summary>
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

        /// <summary>Добавить известную карту в руку (например, из ввода). Увеличивает и HandCount.</summary>
        public void AddHandCard(Card c)
        {
            if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
            Hand[HandKnown++] = c;
            HandCount++;
        }

        /// <summary>Неизвестная карта в руке стала известной (противник её разыграл). HandCount не меняется.</summary>
        public void RevealHandCard(Card c)
        {
            if (HandKnown >= Hand.Length) throw new InvalidOperationException("hand overflow");
            Hand[HandKnown++] = c;
            if (HandCount < HandKnown) HandCount = HandKnown;
        }

        /// <summary>Убрать карту из руки, сохраняя порядок остальных (как ArrayList.remove у арбитра).</summary>
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

        /// <summary>Убрать существо со стола, сохраняя порядок остальных.</summary>
        public void RemoveCreature(int index)
        {
            for (int i = index + 1; i < BoardCount; i++) Board[i - 1] = Board[i];
            BoardCount--;
        }

        /// <summary>Строка игрока в формате ввода арбитра: health mana deck rune draw.</summary>
        public string ToInputLine() => $"{Health} {MaxMana} {DeckSize} {NextRune} {DrawShown}";

        public override string ToString() =>
            $"hp={Health} mana={Mana}/{MaxMana} deck={DeckSize} rune={NextRune} draw={NextTurnDraw} hand={HandKnown}/{HandCount} board={BoardCount}";
    }
}
