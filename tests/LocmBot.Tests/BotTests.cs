using System.IO;
using System.Text;

namespace Locm.Tests
{
    public static class BotTests
    {
        private static string DraftLine(int i) =>
            $"30 0 {i} 25 0\n30 0 {i} 25 0\n0 0\n3\n" +
            "1 -1 0 0 1 2 1 ------ 1 0 0\n" +
            "2 -1 0 0 1 1 2 ------ 0 -1 0\n" +
            "3 -1 0 0 1 2 2 ------ 0 0 0\n";

        private const string BattleLine =
            "30 1 25 25 1\n30 1 25 25 1\n5 0\n1\n" +
            "3 9 0 0 1 2 2 ------ 0 0 0\n";

        [Test]
        public static void DraftThenBattle_ProducesPickThenPass()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Bot.DraftTurns; i++) sb.Append(DraftLine(i));
            sb.Append(BattleLine);

            var output = new StringWriter();
            var bot = new Bot(new FirstCardDraft(), new PassBattle(), TextWriter.Null);
            bot.Run(new StringReader(sb.ToString()), output);

            var lines = output.ToString().TrimEnd().Split('\n');
            Assert.Equal(Bot.DraftTurns + 1, lines.Length);
            for (int i = 0; i < Bot.DraftTurns; i++) Assert.Equal("PICK 0", lines[i].Trim());
            Assert.Equal("PASS", lines[Bot.DraftTurns].Trim());
        }

        [Test]
        public static void BrokenInput_StillAnswersPass()
        {
            var output = new StringWriter();
            var bot = new Bot(new FirstCardDraft(), new PassBattle(), TextWriter.Null);
            bot.Run(new StringReader("garbage line\n"), output);
            Assert.Equal("PASS", output.ToString().Trim());
        }
    }
}
