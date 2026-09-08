using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using static Locm.Tests.TestUtil;

namespace Locm.Tests
{
    public static class SearchTests
    {
        private static List<GameAction> Run(GameState s, int ms = 60)
        {
            var search = new SearchBattle();
            return new List<GameAction>(search.Search(s, new TurnClock(ms)));
        }

        private static GameState After(GameState s, IList<GameAction> line)
        {
            var c = s.Clone();
            Assert.Equal(0, c.ApplySequence(line), "line must be fully legal: " + Joined(line));
            return c;
        }

        [Test]
        public static void FindsLethal()
        {
            var s = NewState(oppHp: 5);
            Board(s, 0, Cr(1, 3, 3));
            Board(s, 0, Cr(2, 3, 3));
            Board(s, 1, Cr(3, 9, 9));
            var line = Run(s);
            Assert.Equal(0, After(s, line).Winner, Joined(line));
        }

        [Test]
        public static void KillsGuardThenFace()
        {
            var s = NewState(oppHp: 5);
            Board(s, 0, Cr(1, 5, 5));
            Board(s, 0, Cr(2, 2, 2));
            Board(s, 1, Cr(3, 1, 1, "---G--"));
            var line = Run(s);
            Assert.Equal(0, After(s, line).Winner, Joined(line));
            Assert.Equal("ATTACK 2 3;ATTACK 1 -1", Joined(line));
        }

        [Test]
        public static void PrefersFaceOverSuicide()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 1, 1));
            Board(s, 1, Cr(2, 5, 5));
            Assert.Equal("ATTACK 1 -1", Joined(Run(s)));
        }

        [Test]
        public static void SummonsWithinMana()
        {
            var s = NewState(mana: 5);
            s.Players[0].AddHandCard(Cr(1, 3, 3, cost: 2));
            s.Players[0].AddHandCard(Cr(2, 4, 4, cost: 3));
            s.Players[0].AddHandCard(Cr(3, 5, 5, cost: 4));
            var line = Run(s);
            var after = After(s, line);
            Assert.True(OnBoard(after, 0, 1) && OnBoard(after, 0, 2), Joined(line));
            Assert.Equal(0, after.Players[0].Mana);
        }

        [Test]
        public static void UsesRemovalOnBigThreat()
        {
            var s = NewState(mana: 5);
            s.Players[0].AddHandCard(Db(151, 10)); // Decimate
            Board(s, 1, Cr(2, 8, 8));
            var line = Run(s);
            Assert.Equal("USE 10 2", Joined(line));
        }

        [Test]
        public static void BoardFull_TradesThenSummons()
        {
            var s = NewState(mana: 5);
            for (int i = 0; i < 6; i++) Board(s, 0, Cr(10 + i, 1, 1));
            for (int i = 0; i < 3; i++) Board(s, 1, Cr(20 + i, 1, 1));
            s.Players[0].AddHandCard(Cr(1, 5, 5, cost: 5));
            var line = Run(s);
            var after = After(s, line);
            Assert.True(OnBoard(after, 0, 1), "5/5 must be summoned into a freed slot: " + Joined(line));
            bool sawTrade = false;
            foreach (var a in line)
            {
                if (a.Type == ActionType.Summon) Assert.True(sawTrade, "summon must come after a trade: " + Joined(line));
                if (a.Type == ActionType.Attack && a.Target != -1) sawTrade = true;
            }
        }

        [Test]
        public static void ChargeItemEnablesImmediateAttack()
        {
            var s = NewState(mana: 6, oppHp: 6);
            s.Players[0].AddHandCard(Cr(1, 6, 6, cost: 4));
            s.Players[0].AddHandCard(Db(140, 10)); // Grow Wings: Charge, cost 2
            var line = Run(s);
            Assert.Equal(0, After(s, line).Winner, Joined(line));
        }

        [Test]
        public static void RespectsTimeBudget()
        {
            var s = NewState(mana: 12);
            for (int i = 0; i < 6; i++) Board(s, 0, Cr(10 + i, 2 + i, 3 + i));
            for (int i = 0; i < 6; i++) Board(s, 1, Cr(20 + i, 2 + i, 3 + i));
            for (int i = 0; i < 4; i++) s.Players[0].AddHandCard(Item(CardType.RedItem, 30 + i, 0, -2, cost: 1));
            for (int i = 0; i < 4; i++) s.Players[0].AddHandCard(Item(CardType.GreenItem, 40 + i, 2, 2, cost: 1));
            var search = new SearchBattle();
            var sw = Stopwatch.StartNew();
            var line = search.Search(s, new TurnClock(40));
            sw.Stop();
            Assert.True(search.TimedOut, "position is too big to finish, nodes=" + search.Nodes);
            Assert.True(sw.ElapsedMilliseconds < 120, "took " + sw.ElapsedMilliseconds + " ms");
            Assert.True(line.Count > 0);
            After(s, line);
        }

        [Test]
        public static void AnswersAreLegalOnRefereePositions()
        {
            string dir = Replay.FixturesDir();
            Assert.True(dir != null);
            var files = Directory.GetFiles(dir, "*.log");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            int positions = 0;
            var search = new SearchBattle();
            foreach (var f in files)
            {
                if (positions >= 60) break;
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (t.Input.LooksLikeDraft) continue;
                    var s = GameState.FromInput(t.Input);
                    if (s.IsOver) continue;
                    var line = search.Search(s, new TurnClock(15));
                    Assert.Equal(0, s.Clone().ApplySequence(line), Path.GetFileName(f) + ": " + Joined(line));
                    positions++;
                    if (positions >= 60) break;
                }
            }
            Assert.True(positions >= 40, "positions=" + positions);
        }
    }
}
