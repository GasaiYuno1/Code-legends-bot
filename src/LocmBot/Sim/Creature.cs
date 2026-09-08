namespace Locm
{
    /// <summary>
    /// Существо на столе (аналог CreatureOnBoard арбитра). Изменяемая структура в массиве
    /// <see cref="PlayerState.Board"/>; базовая карта хранится ради cost/эффектов призыва при выводе.
    /// </summary>
    public struct Creature
    {
        public Card Card;
        public int Attack;
        public int Defense;
        public Abilities Abilities;
        public bool CanAttack;
        public bool HasAttacked;   // атаковало в текущем ходу (для Charge через предмет)
        // Эффекты призыва, как их показывает арбитр во вводе: копирующий конструктор CreatureOnBoard их
        // не переносит, поэтому после первого боя или предмета арбитр присылает нули. На правила не влияют.
        public int MyHealthChange;
        public int OpponentHealthChange;
        public int CardDraw;

        public int InstanceId => Card.InstanceId;
        public int BaseId => Card.Number;

        public bool Has(Abilities flag) => (Abilities & flag) != 0;

        /// <summary>Существо, только что выставленное с руки: атаковать может только с Charge.</summary>
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

        /// <summary>Существо из строки ввода (текущие attack/defense/abilities уже в карте).</summary>
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

        /// <summary>Арбитр пересоздаёт существо копией после боя/предмета — эффекты призыва в выводе обнуляются.</summary>
        public void ClearSummonEffects()
        {
            MyHealthChange = 0;
            OpponentHealthChange = 0;
            CardDraw = 0;
        }

        /// <summary>Строка в формате ввода арбитра (CreatureOnBoard.getAsInput).</summary>
        public string ToInputLine(bool opponentBoard)
        {
            int loc = opponentBoard ? -1 : 1;
            return $"{Card.Number} {Card.InstanceId} {loc} 0 {Card.Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";
        }

        public override string ToString() =>
            $"#{BaseId}/{InstanceId} {Attack}/{Defense} {Abilities.Format()}{(CanAttack ? " ready" : "")}{(HasAttacked ? " attacked" : "")}";
    }
}
