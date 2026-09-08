using System;
using System.IO;

namespace Locm
{
    public static class Program
    {
        public static void Main()
        {
            // Буферизованный вывод: один WriteLine + Flush на ход.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var bot = new Bot(new FirstCardDraft(), new PassBattle(), stderr);
            bot.Run(Console.In, stdout);
        }
    }
}
