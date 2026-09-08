namespace Locm
{
    public enum CardType : byte
    {
        Creature = 0,
        GreenItem = 1,
        RedItem = 2,
        BlueItem = 3,
    }

    /// <summary>Где лежит карта с точки зрения текущего игрока (значение поля location во вводе).</summary>
    public enum Location : sbyte
    {
        OpponentBoard = -1,
        MyHand = 0,
        MyBoard = 1,
    }

    /// <summary>
    /// Карта ровно в том виде, в каком её присылает арбитр (одна строка ввода).
    /// Неизменяемая структура: копируется по значению, поэтому годится и для карты в руке
    /// симулятора, и как базовая карта существа на столе (см. <see cref="Creature"/>).
    /// </summary>
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

        /// <summary>Та же карта с другим instanceId/location (например, карта из CardDb, которую противник только что разыграл).</summary>
        public Card WithInstance(int instanceId, Location location) =>
            new Card(Number, instanceId, location, Type, Cost, Attack, Defense, Abilities, MyHealthChange, OpponentHealthChange, CardDraw);

        /// <summary>Строка карты в формате ввода арбитра (Card.getAsInput / CreatureOnBoard.getAsInput).</summary>
        public string ToInputLine() => ToInputLine(Location);

        public string ToInputLine(Location location) =>
            $"{Number} {InstanceId} {(int)location} {(int)Type} {Cost} {Attack} {Defense} {Abilities.Format()} {MyHealthChange} {OpponentHealthChange} {CardDraw}";

        public override string ToString() =>
            $"#{Number}/{InstanceId} {Type} {Cost}m {Attack}/{Defense} {Abilities.Format()} hp{MyHealthChange:+0;-0;0}/{OpponentHealthChange:+0;-0;0} draw{CardDraw}";
    }
}
