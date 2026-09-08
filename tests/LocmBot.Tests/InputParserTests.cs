using System.IO;

namespace Locm.Tests
{
    public static class InputParserTests
    {
        // Реальный формат арбитра: ход драфта.
        private const string DraftTurn =
            "30 0 3 25 0\n" +
            "30 0 4 25 0\n" +
            "0 0\n" +
            "3\n" +
            "1 -1 0 0 1 2 1 ------ 1 0 0\n" +
            "104 -1 0 0 4 4 1 --D--W 0 0 0\n" +
            "141 -1 0 1 0 1 1 -----W 0 0 0\n";

        // Ход боя: у меня карты в руке и на столе, у противника существо на столе, был его ход с двумя действиями.
        private const string BattleTurn =
            "27 4 22 25 1\n" +
            "22 4 21 20 2\n" +
            "5 2\n" +
            "3 SUMMON 7\n" +
            "3 ATTACK 7 -1\n" +
            "4\n" +
            "18 12 0 0 4 7 4 ------ 0 0 0\n" +
            "157 15 0 3 2 -1 0 ------ 0 0 1\n" +
            "3 9 1 0 1 2 2 ------ 0 0 0\n" +
            "3 7 -1 0 1 2 2 ------ 0 0 0\n";

        [Test]
        public static void ParsesDraftTurn()
        {
            var t = InputParser.ReadTurn(new StringReader(DraftTurn));
            Assert.Equal(30, t.Me.Health);
            Assert.Equal(0, t.Me.Mana);
            Assert.Equal(3, t.Me.DeckSize);
            Assert.Equal(4, t.Opponent.DeckSize);
            Assert.Equal(0, t.OpponentHandSize);
            Assert.Equal(0, t.OpponentActions.Count);
            Assert.Equal(3, t.Cards.Count);
            Assert.True(t.LooksLikeDraft);

            var c = t.Cards[1];
            Assert.Equal(104, c.Number);
            Assert.Equal(CardType.Creature, c.Type);
            Assert.Equal(4, c.Cost);
            Assert.Equal(Abilities.Drain | Abilities.Ward, c.Abilities);
            Assert.Equal("--D--W", c.Abilities.Format());
            Assert.Equal(CardType.GreenItem, t.Cards[2].Type);
        }

        [Test]
        public static void ParsesBattleTurn()
        {
            var t = InputParser.ReadTurn(new StringReader(BattleTurn));
            Assert.False(t.LooksLikeDraft);
            Assert.Equal(27, t.Me.Health);
            Assert.Equal(4, t.Me.Mana);
            Assert.Equal(20, t.Opponent.Rune);
            Assert.Equal(2, t.Opponent.Draw);
            Assert.Equal(5, t.OpponentHandSize);
            Assert.Equal(2, t.OpponentActions.Count);
            Assert.Equal(3, t.OpponentActions[1].CardNumber);
            Assert.Equal("ATTACK 7 -1", t.OpponentActions[1].Action);

            Assert.Equal(4, t.Cards.Count);
            Assert.Equal(Location.MyHand, t.Cards[0].Location);
            Assert.Equal(CardType.BlueItem, t.Cards[1].Type);
            Assert.Equal(-1, t.Cards[1].Attack);
            Assert.Equal(1, t.Cards[1].CardDraw);
            Assert.Equal(Location.MyBoard, t.Cards[2].Location);
            Assert.Equal(Location.OpponentBoard, t.Cards[3].Location);
            Assert.Equal(7, t.Cards[3].InstanceId);
        }

        [Test]
        public static void ReadTurnReturnsNullOnEmptyInput()
        {
            Assert.True(InputParser.ReadTurn(new StringReader("")) == null);
        }

        [Test]
        public static void TruncatedInputThrows()
        {
            Assert.Throws<EndOfStreamException>(() => InputParser.ReadTurn(new StringReader("30 0 3 25 0\n30 0 4 25 0\n")));
        }
    }
}
