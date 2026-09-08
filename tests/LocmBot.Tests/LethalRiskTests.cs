using System.Collections.Generic;
using static Locm.Tests.TestUtil;

namespace Locm.Tests
{
    public static class LethalRiskTests
    {
        private static TurnInput Triple(int a, int b, int c)
        {
            var t = new TurnInput();
            t.Me.Mana = 0;
            t.Cards.Add(CardDb.Get(a));
            t.Cards.Add(CardDb.Get(b));
            t.Cards.Add(CardDb.Get(c));
            return t;
        }

        /// <summary>Поиск с известной колодой противника: все 30 троек — одна и та же карта.</summary>
        private static SearchBattle WithDeck(int baseId)
        {
            var search = new SearchBattle();
            search.LethalRiskCandidates = 8;
            for (int i = 0; i < 30; i++) search.ObserveDraft(Triple(baseId, baseId, baseId));
            return search;
        }

        private static GameState Position(int myHp, int oppMaxMana)
        {
            var s = NewState(mana: 2, myHp: myHp);
            s.Players[1].HandCount = 1;
            s.Players[1].DeckSize = 10;
            s.Players[1].MaxMana = oppMaxMana;
            return s;
        }

        [Test]
        public static void Poison_InOpponentDeck_IsLethalAtTwoHp()
        {
            var search = WithDeck(154);   // Poison: 2 урона в лицо за 2 маны
            Assert.Equal(1.0, search.LethalRiskScore(Position(2, 6), 0));
            Assert.Equal(0.0, search.LethalRiskScore(Position(9, 6), 0), "две Poison = 4 урона < 9");
            Assert.Equal(0.0, search.LethalRiskScore(Position(2, 0), 0), "маны на Poison нет (0 -> 1 после его добора)");
        }

        [Test]
        public static void ChargeCreature_IsBlockedByGuard()
        {
            // карта 7 — 3/2 Charge за 2 маны? берём реальную: ищем существо с Charge в базе
            int charge = -1;
            for (int id = 1; id <= CardDb.Count && charge < 0; id++)
            {
                var c = CardDb.Get(id);
                if (c.Type == CardType.Creature && (c.Abilities & Abilities.Charge) != 0 && c.Cost <= 3 && c.Attack >= 2) charge = id;
            }
            Assert.True(charge > 0);
            int atk = CardDb.Get(charge).Attack;
            var search = WithDeck(charge);
            var open = Position(atk, 6);
            Assert.Equal(1.0, search.LethalRiskScore(open, 0), "Charge бьёт в лицо на " + atk);
            var guarded = Position(atk, 6);
            Board(guarded, 0, Cr(1, 1, 9, "---G--"));
            Assert.Equal(0.0, search.LethalRiskScore(guarded, 0), "Guard 1/9 закрывает лицо");
        }

        [Test]
        public static void Search_PrefersHealingOverSummonWhenBurnIsLethal()
        {
            // 2 маны: либо призвать 6/6 за 2, либо выпить зелье +5 HP; в колоде противника только Poison (2 урона)
            var s = Position(2, 6);
            s.Players[0].AddHandCard(Cr(1, 6, 6, "------", 2));
            s.Players[0].AddHandCard(Item(CardType.BlueItem, 2, 0, 0, "------", 2, 5, 0, 0));
            var risky = new SearchBattle();
            var safe = WithDeck(154);
            var lineRisky = new List<GameAction>(risky.Search(s, new TurnClock(60)));
            var lineSafe = new List<GameAction>(safe.Search(s, new TurnClock(60)));
            Assert.True(lineRisky.Count > 0 && lineRisky[0].Type == ActionType.Summon, "без риска — призыв 6/6: " + Joined(lineRisky));
            Assert.True(lineSafe.Count > 0 && lineSafe[0].Type == ActionType.Use, "с риском — лечение: " + Joined(lineSafe));
        }
    }
}

namespace Locm.Tests
{
    public static class ExactLethalTests
    {
        [Test]
        public static void WardGuard_GreedySaysLethal_ExactDoesNot()
        {
            // моё HP 3, страж 6/7 GW; у него 4/4 B и 6/5: эвристика 10 − 7 ≥ 3, но Ward съедает первый удар
            var s = NewState(myHp: 3);
            Board(s, 0, Cr(1, 6, 7, "---G-W"));
            Board(s, 1, Cr(2, 4, 4, "B-----"));
            Board(s, 1, Cr(3, 6, 5));
            var search = new SearchBattle();
            search.ExactLethalNodes = 200;
            Assert.True(search.GreedyLethal(s, 0));
            Assert.False(search.AttackLethal(s, 0, 200));
            Assert.True(search.ReplyScore(s, 0) > -Evaluator.WinScore / 2, "ReplyScore не должен считать позицию проигранной");
        }

        [Test]
        public static void LethalAbility_KillsGuard_ExactFindsLethal()
        {
            // моё HP 11, страж 2/4; у него 3/7 Lethal и 5/2, 5/3, 2/2, 1/5: эвристика 16 − 4 = 12 ≥ 11 — и точно летал есть
            var s = NewState(myHp: 11);
            Board(s, 0, Cr(1, 2, 4, "---G--"));
            Board(s, 0, Cr(2, 3, 7, "--D-L-"));
            Board(s, 1, Cr(3, 3, 7, "--D-L-"));
            Board(s, 1, Cr(4, 5, 2));
            Board(s, 1, Cr(5, 5, 3));
            Board(s, 1, Cr(6, 2, 2));
            Board(s, 1, Cr(7, 1, 5));
            var search = new SearchBattle();
            search.ExactLethalNodes = 200;
            Assert.True(search.AttackLethal(s, 0, 200));
            Assert.True(search.DeepReplyScore(s, 0) < -Evaluator.WinScore / 2, "глубокий ответ должен видеть летал");
        }

        [Test]
        public static void LethalAbility_GreedyMisses_ExactFinds()
        {
            // моё HP 5, страж 1/9; у него 1/1 Lethal и 5/5: эвристика 6 − 9 < 5, но Lethal убивает стража, 5 в лицо
            var s = NewState(myHp: 5);
            Board(s, 0, Cr(1, 1, 9, "---G--"));
            Board(s, 1, Cr(2, 1, 1, "----L-"));
            Board(s, 1, Cr(3, 5, 5));
            var search = new SearchBattle();
            search.ExactLethalNodes = 200;
            Assert.False(search.GreedyLethal(s, 0));
            Assert.True(search.AttackLethal(s, 0, 200));
            Assert.True(search.DeepReplyScore(s, 0) < -Evaluator.WinScore / 2);
        }
    }
}
