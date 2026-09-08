using System;
using System.IO;

namespace Locm
{
    public static class Program
    {
        // true — дублировать в stderr весь ввод и ответ каждого хода (лог для сверки симулятора, см. tests/LocmBot.Tests replay).
        private const bool DumpInput = false;

        public static void Main()
        {
            // Буферизованный вывод: один WriteLine + Flush на ход.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var bot = new Bot(new FirstCardDraft(), new PassBattle(), stderr, DumpInput ? stderr : null);
            bot.Run(Console.In, stdout);
        }
    }
}
