using System.IO;

namespace Locm.Tests
{
    public static class ReplayTests
    {
        /// <summary>Логи tests/fixtures/*.log сгенерированы настоящим движком арбитра (tools/refcheck). Ни одного расхождения.</summary>
        [Test]
        public static void FixturesMatchReferee()
        {
            string dir = Replay.FixturesDir();
            Assert.True(dir != null, "tests/fixtures not found");
            var files = Directory.GetFiles(dir, "*.log");
            Assert.True(files.Length > 0, "no fixture logs");

            int checkedTurns = 0;
            foreach (var f in files)
            {
                var r = Replay.RunFile(f);
                Assert.True(r.Ok, r.Name + ": " + (r.Mismatches.Count > 0 ? r.Mismatches[0] : ""));
                Assert.True(r.CheckedTurns > 0, r.Name + ": nothing checked");
                checkedTurns += r.CheckedTurns;
            }
            Assert.True(checkedTurns >= 200, "expected at least 200 verified turns, got " + checkedTurns);
        }

        [Test]
        public static void Parse_SkipsCommentsAndSplitsTurns()
        {
            const string log =
                "# header\n" +
                "30 1 25 25 1\n30 1 25 25 1\n5 0\n1\n3 9 0 0 1 2 2 ------ 0 0 0\n" +
                "> SUMMON 9\n" +
                "\n" +
                "30 2 24 25 1\n29 2 24 25 1\n5 1\n3 SUMMON 4\n2\n3 9 1 0 1 2 2 ------ 0 0 0\n3 4 -1 0 1 2 2 ------ 0 0 0\n" +
                ">PASS\n";
            var turns = Replay.Parse(log);
            Assert.Equal(2, turns.Count);
            Assert.Equal("SUMMON 9", turns[0].Answer);
            Assert.Equal("PASS", turns[1].Answer);
            Assert.Equal(1, turns[1].Input.OpponentActions.Count);
        }

        [Test]
        public static void Run_DetectsMismatch()
        {
            // Противник выставил Beavrat (2/2 по базе), но арбитр «прислал» 2/3 — расхождение по столу.
            const string log =
                "30 1 25 25 1\n30 1 25 25 1\n5 0\n1\n3 9 0 0 1 2 2 ------ 0 0 0\n" +
                "> SUMMON 9\n" +
                "30 2 24 25 1\n30 2 24 25 1\n5 1\n3 SUMMON 4\n3\n5 11 0 0 2 4 1 ------ 0 0 0\n3 9 1 0 1 2 2 ------ 0 0 0\n3 4 -1 0 1 2 3 ------ 0 0 0\n" +
                "> PASS\n";
            var r = Replay.Run(log);
            Assert.Equal(1, r.CheckedTurns);
            Assert.False(r.Ok);
            Assert.True(r.Mismatches[0].Contains("opp board[0]"), r.Mismatches[0]);
        }

        [Test]
        public static void Run_ConsistentLogHasNoMismatch()
        {
            // Я (первый игрок, нечётные id) выставил Beavrat; противник (P1, бонус маны: 1 -> 2) выставил своего и добрал.
            const string log =
                "30 1 25 25 1\n30 1 25 25 1\n5 0\n1\n3 9 0 0 1 2 2 ------ 0 0 0\n" +
                "> SUMMON 9\n" +
                "30 2 24 25 1\n30 2 24 25 1\n5 1\n3 SUMMON 4\n3\n5 11 0 0 2 4 1 ------ 0 0 0\n3 9 1 0 1 2 2 ------ 0 0 0\n3 4 -1 0 1 2 2 ------ 0 0 0\n" +
                "> PASS\n";
            var r = Replay.Run(log);
            Assert.Equal(1, r.CheckedTurns);
            Assert.True(r.Ok, r.Ok ? "" : r.Mismatches[0]);
        }
    }
}
