using System;
using System.Collections.Generic;
using System.IO;

namespace Locm
{
    /// <summary>
    /// Разбор ввода арбитра. Читает ровно один ход. Первая строка читается снаружи
    /// (чтобы засечь время сразу после её получения) и передаётся параметром.
    /// </summary>
    public static class InputParser
    {
        /// <summary>Читает ход целиком, включая первую строку. Возвращает null на конце потока.</summary>
        public static TurnInput ReadTurn(TextReader reader)
        {
            string first = reader.ReadLine();
            if (first == null) return null;
            return ReadTurn(first, reader);
        }

        /// <summary>Читает ход, первая строка уже прочитана.</summary>
        public static TurnInput ReadTurn(string firstLine, TextReader reader)
        {
            var t = new TurnInput();
            t.Me = ParsePlayer(firstLine);
            t.Opponent = ParsePlayer(ReadRequired(reader));

            var oppLine = Split(ReadRequired(reader));
            t.OpponentHandSize = int.Parse(oppLine[0]);
            int opponentActions = int.Parse(oppLine[1]);
            for (int i = 0; i < opponentActions; i++)
            {
                string line = ReadRequired(reader);
                int space = line.IndexOf(' ');
                t.OpponentActions.Add(new OpponentAction
                {
                    CardNumber = int.Parse(space < 0 ? line : line.Substring(0, space)),
                    Action = space < 0 ? "" : line.Substring(space + 1),
                });
            }

            int cardCount = int.Parse(ReadRequired(reader).Trim());
            for (int i = 0; i < cardCount; i++)
                t.Cards.Add(ParseCard(ReadRequired(reader)));

            return t;
        }

        public static PlayerInfo ParsePlayer(string line)
        {
            var p = Split(line);
            return new PlayerInfo
            {
                Health = int.Parse(p[0]),
                Mana = int.Parse(p[1]),
                DeckSize = int.Parse(p[2]),
                Rune = int.Parse(p[3]),
                Draw = int.Parse(p[4]),
            };
        }

        public static Card ParseCard(string line)
        {
            var p = Split(line);
            return new Card(
                number: int.Parse(p[0]),
                instanceId: int.Parse(p[1]),
                location: (Location)int.Parse(p[2]),
                type: (CardType)int.Parse(p[3]),
                cost: int.Parse(p[4]),
                attack: int.Parse(p[5]),
                defense: int.Parse(p[6]),
                abilities: AbilitiesExt.Parse(p[7]),
                myHealthChange: int.Parse(p[8]),
                opponentHealthChange: int.Parse(p[9]),
                cardDraw: int.Parse(p[10]));
        }

        private static string[] Split(string line) =>
            line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static string ReadRequired(TextReader reader)
        {
            string line = reader.ReadLine();
            if (line == null) throw new EndOfStreamException("Unexpected end of input");
            return line;
        }
    }
}
