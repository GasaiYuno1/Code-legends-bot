using System;

namespace Locm.Tests
{
    public static class GameActionTests
    {
        [Test]
        public static void ParsesAllKinds()
        {
            Assert.Equal(GameAction.Summon(7), GameAction.Parse("SUMMON 7"));
            Assert.Equal(GameAction.Attack(5, -1), GameAction.Parse("ATTACK 5 -1"));
            Assert.Equal(GameAction.Use(3, 12), GameAction.Parse("USE 3 12"));
            Assert.Equal(GameAction.Pass, GameAction.Parse("PASS"));
        }

        [Test]
        public static void IgnoresTrailingTextAndExtraSpaces()
        {
            Assert.Equal(GameAction.Summon(7), GameAction.Parse("SUMMON 7 hello there"));
            Assert.Equal(GameAction.Attack(5, -1), GameAction.Parse("  ATTACK  5   -1  gg "));
            Assert.Equal(GameAction.Pass, GameAction.Parse("PASS whatever"));
        }

        [Test]
        public static void MalformedActionThrows()
        {
            Assert.Throws<FormatException>(() => GameAction.Parse("SUMMON"));
            Assert.Throws<FormatException>(() => GameAction.Parse("ATTACK 5"));
            Assert.Throws<FormatException>(() => GameAction.Parse("USE x -1"));
            Assert.Throws<FormatException>(() => GameAction.Parse("PLAY 1"));
            Assert.Throws<FormatException>(() => GameAction.Parse(""));
        }

        [Test]
        public static void SequenceSkipsEmptySegments()
        {
            var seq = GameAction.ParseSequence("SUMMON 1;;ATTACK 1 -1; ;PASS;");
            Assert.Equal(3, seq.Count);
            Assert.Equal(GameAction.Summon(1), seq[0]);
            Assert.Equal(GameAction.Attack(1, -1), seq[1]);
            Assert.Equal(GameAction.Pass, seq[2]);
            Assert.Equal(0, GameAction.ParseSequence("").Count);
            Assert.Equal(0, GameAction.ParseSequence(null).Count);
        }

        [Test]
        public static void FormatsRefereeStrings()
        {
            Assert.Equal("SUMMON 7", GameAction.Summon(7).ToString());
            Assert.Equal("ATTACK 5 -1", GameAction.Attack(5, -1).ToString());
            Assert.Equal("USE 3 12", GameAction.Use(3, 12).ToString());
            Assert.Equal("PASS", GameAction.Pass.ToString());
            Assert.Equal("PASS", GameAction.Format(new GameAction[0]));
            Assert.Equal("SUMMON 7;ATTACK 7 -1", GameAction.Format(new[] { GameAction.Summon(7), GameAction.Attack(7, -1) }));
        }

        [Test]
        public static void EqualityIgnoresNothing()
        {
            Assert.True(GameAction.Attack(1, 2) == GameAction.Attack(1, 2));
            Assert.True(GameAction.Attack(1, 2) != GameAction.Attack(1, 3));
            Assert.True(GameAction.Attack(1, 2) != GameAction.Use(1, 2));
            Assert.Equal(GameAction.Attack(1, 2).GetHashCode(), GameAction.Attack(1, 2).GetHashCode());
        }
    }
}
