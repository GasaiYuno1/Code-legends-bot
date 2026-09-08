using System;
using System.Collections.Generic;
using System.Text;

namespace Locm
{
    public enum ActionType : byte
    {
        Pass = 0,
        Summon = 1,
        Attack = 2,
        Use = 3,
    }

    /// <summary>
    /// Действие в формате арбитра: SUMMON id | ATTACK id target | USE id target | PASS.
    /// Структура без ссылок — перебор ходов не аллоцирует.
    /// </summary>
    public readonly struct GameAction : IEquatable<GameAction>
    {
        public readonly ActionType Type;
        public readonly int Id;       // instanceId карты/существа
        public readonly int Target;   // instanceId цели или -1 (лицо игрока); для SUMMON не используется

        public const int Face = -1;

        public GameAction(ActionType type, int id, int target)
        {
            Type = type;
            Id = id;
            Target = target;
        }

        public static readonly GameAction Pass = new GameAction(ActionType.Pass, 0, 0);
        public static GameAction Summon(int id) => new GameAction(ActionType.Summon, id, 0);
        public static GameAction Attack(int id, int target) => new GameAction(ActionType.Attack, id, target);
        public static GameAction Use(int id, int target) => new GameAction(ActionType.Use, id, target);

        public bool IsPass => Type == ActionType.Pass;

        public override string ToString()
        {
            switch (Type)
            {
                case ActionType.Summon: return "SUMMON " + Id;
                case ActionType.Attack: return "ATTACK " + Id + " " + Target;
                case ActionType.Use: return "USE " + Id + " " + Target;
                default: return "PASS";
            }
        }

        /// <summary>Строка ответа для арбитра: действия через ';', пустой список → PASS.</summary>
        public static string Format(IList<GameAction> actions)
        {
            if (actions == null || actions.Count == 0) return "PASS";
            var sb = new StringBuilder();
            for (int i = 0; i < actions.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(actions[i].ToString());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Разбор одного действия. Как и у арбитра, лишний текст после аргументов игнорируется.
        /// Ошибка формата (неизвестное имя, не хватает аргументов) → FormatException:
        /// арбитр за такое снимает игрока с партии (InvalidActionHard).
        /// </summary>
        public static GameAction Parse(string s)
        {
            GameAction a;
            if (!TryParse(s, out a)) throw new FormatException("invalid action: '" + s + "'");
            return a;
        }

        public static bool TryParse(string s, out GameAction action)
        {
            action = Pass;
            if (s == null) return false;
            var p = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return false;
            int id, target;
            switch (p[0])
            {
                case "PASS":
                    return true;
                case "SUMMON":
                    if (p.Length < 2 || !int.TryParse(p[1], out id)) return false;
                    action = Summon(id);
                    return true;
                case "ATTACK":
                case "USE":
                    if (p.Length < 3 || !int.TryParse(p[1], out id) || !int.TryParse(p[2], out target)) return false;
                    action = p[0] == "ATTACK" ? Attack(id, target) : Use(id, target);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Разбор строки хода "A;B;C" (пустые сегменты пропускаются, как у арбитра).</summary>
        public static List<GameAction> ParseSequence(string line)
        {
            var result = new List<GameAction>();
            if (line == null) return result;
            foreach (var part in line.Split(';'))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;
                result.Add(Parse(s));
            }
            return result;
        }

        public bool Equals(GameAction o) => Type == o.Type && Id == o.Id && Target == o.Target;
        public override bool Equals(object obj) => obj is GameAction && Equals((GameAction)obj);
        public override int GetHashCode() => ((int)Type * 397 + Id) * 397 + Target;
        public static bool operator ==(GameAction a, GameAction b) => a.Equals(b);
        public static bool operator !=(GameAction a, GameAction b) => !a.Equals(b);
    }
}
