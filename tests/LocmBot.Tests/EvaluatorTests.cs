using static Locm.Tests.TestUtil;

namespace Locm.Tests
{
    public static class EvaluatorTests
    {
        [Test]
        public static void MirroredPositionIsZero()
        {
            var e = new Evaluator();
            var s = NewState();
            Board(s, 0, Cr(1, 3, 3, "---G--"));
            Board(s, 1, Cr(2, 3, 3, "---G--"));
            s.Players[1].HandCount = s.Players[0].HandCount;
            Assert.Equal(0.0, e.Score(s, 0));
            Assert.Equal(0.0, e.Score(s, 1));
        }

        [Test]
        public static void MoreBoardAndLessOpponentHealthIsBetter()
        {
            var e = new Evaluator();
            var s = NewState();
            double b0 = e.Score(s, 0);
            Board(s, 0, Cr(1, 3, 3));
            double b1 = e.Score(s, 0);
            Assert.True(b1 > b0);
            s.Players[1].Health -= 5;
            Assert.True(e.Score(s, 0) > b1);
            Assert.True(e.Score(s, 1) < -b1);
        }

        [Test]
        public static void LowHealthHurtsMore()
        {
            var e = new Evaluator();
            Assert.True(e.Health(30) - e.Health(25) < e.Health(8) - e.Health(3));
        }

        [Test]
        public static void WinIsAbsolute()
        {
            var e = new Evaluator();
            var s = NewState(oppHp: 1);
            Board(s, 0, Cr(1, 1, 1));
            s.Apply(GameAction.Attack(1, -1));
            Assert.Equal(Evaluator.WinScore, e.Score(s, 0));
            Assert.Equal(-Evaluator.WinScore, e.Score(s, 1));
        }

        [Test]
        public static void BrokenRunesArePenalised()
        {
            var e = new Evaluator();
            var s = NewState();
            double before = e.Score(s, 0);
            s.Players[1].ModifyHealth(-1);     // 30 -> 29: рун нет
            double a = e.Score(s, 0);
            s.Players[1].ModifyHealth(-4);     // 29 -> 25: руна 25 -> +1 карта противнику
            double b = e.Score(s, 0);
            Assert.True(a > before);
            Assert.True(b - a < a - before, "rune break should be worth less than plain damage");
        }
    }
}
