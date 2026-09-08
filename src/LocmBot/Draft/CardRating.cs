using System;

namespace Locm
{
    /// <summary>
    /// Статический рейтинг карты для драфта (вариант D1 из docs): ценность статов и способностей
    /// относительно «нормы» для её стоимости. Веса — ручное первое приближение, тюнинг — этап 4.
    /// </summary>
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

        /// <summary>«Норма» суммарной ценности для стоимости: столько даёт среднее существо за эту ману.</summary>
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
                    // усиление существа: статы как у существа, способности — как будто они на среднем теле 3/3;
                    // норма ниже, чем у существ, но предмет без цели бесполезен — минус за зависимость
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
