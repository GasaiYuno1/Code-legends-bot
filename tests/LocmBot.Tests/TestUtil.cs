using System.Collections.Generic;

namespace Locm.Tests
{
    /// <summary>Общие помощники для тестов симулятора, поиска и драфта.</summary>
    public static class TestUtil
    {
        /// <summary>Синтетическое существо: baseId = 200 + id.</summary>
        public static Card Cr(int id, int atk, int def, string abilities = "------", int cost = 1, int myHp = 0, int oppHp = 0, int draw = 0) =>
            new Card(200 + id, id, Location.MyHand, CardType.Creature, cost, atk, def, AbilitiesExt.Parse(abilities), myHp, oppHp, draw);

        public static Card Item(CardType type, int id, int atk, int def, string abilities = "------", int cost = 0, int myHp = 0, int oppHp = 0, int draw = 0) =>
            new Card(300 + id, id, Location.MyHand, type, cost, atk, def, AbilitiesExt.Parse(abilities), myHp, oppHp, draw);

        /// <summary>Реальная карта арбитра с заданным instanceId.</summary>
        public static Card Db(int baseId, int id) => CardDb.Get(baseId).WithInstance(id, Location.MyHand);

        public static GameState NewState(int mana = 12, int deck = 20, int myHp = 30, int oppHp = 30)
        {
            var s = new GameState();
            foreach (var p in s.Players)
            {
                p.Health = 30;
                p.NextRune = 25;
                p.DeckSize = deck;
                p.NextTurnDraw = 1;
                p.MaxMana = mana;
                p.Mana = mana;
            }
            s.Players[0].Health = myHp;
            s.Players[1].Health = oppHp;
            s.Players[0].NextRune = RuneBelow(myHp);
            s.Players[1].NextRune = RuneBelow(oppHp);
            s.Players[1].Mana = 0;
            return s;
        }

        /// <summary>Как у арбитра: остаются только руны строго ниже здоровья.</summary>
        public static int RuneBelow(int hp)
        {
            for (int r = 25; r > 0; r -= 5)
                if (r < hp) return r;
            return 0;
        }

        public static void Board(GameState s, int player, Card c, bool canAttack = true) =>
            s.Players[player].AddCreature(Creature.FromInput(c, canAttack));

        public static bool OnBoard(GameState s, int player, int id) => s.Players[player].FindCreature(id) >= 0;

        public static string Joined(IList<GameAction> l) => GameAction.Format(l);
    }
}
