using System.Collections.Generic;

namespace Locm
{
    /// <summary>Строка игрока из ввода: health mana deck rune draw.</summary>
    public struct PlayerInfo
    {
        public int Health;
        public int Mana;
        public int DeckSize;
        public int Rune;      // следующая руна (25/20/15/10/5) или 0
        public int Draw;      // сколько карт игрок доберёт в начале следующего хода

        public override string ToString() => $"hp={Health} mana={Mana} deck={DeckSize} rune={Rune} draw={Draw}";
    }

    /// <summary>Действие противника из его прошлого хода (строка "cardNumber action").</summary>
    public struct OpponentAction
    {
        public int CardNumber;
        public string Action;

        public override string ToString() => $"{CardNumber} {Action}";
    }

    /// <summary>Всё, что арбитр прислал за один ход, без интерпретации.</summary>
    public sealed class TurnInput
    {
        public PlayerInfo Me;
        public PlayerInfo Opponent;
        public int OpponentHandSize;
        public List<OpponentAction> OpponentActions = new List<OpponentAction>();
        public List<Card> Cards = new List<Card>();

        /// <summary>Эвристика фазы по содержимому ввода: в драфте мана 0 и ровно 3 карты в руке.</summary>
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
