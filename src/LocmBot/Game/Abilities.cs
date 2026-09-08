using System;
using System.Text;

namespace Locm
{
    /// <summary>Способности карты. Битовая маска — дёшево копировать и сравнивать.</summary>
    [Flags]
    public enum Abilities : byte
    {
        None = 0,
        Breakthrough = 1 << 0,
        Charge = 1 << 1,
        Drain = 1 << 2,
        Guard = 1 << 3,
        Lethal = 1 << 4,
        Ward = 1 << 5,
    }

    public static class AbilitiesExt
    {
        /// <summary>Разбор строки вида "BCDGLW" / "--D--W" (порядок фиксирован арбитром).</summary>
        public static Abilities Parse(string s)
        {
            Abilities a = Abilities.None;
            foreach (char c in s)
            {
                switch (c)
                {
                    case 'B': a |= Abilities.Breakthrough; break;
                    case 'C': a |= Abilities.Charge; break;
                    case 'D': a |= Abilities.Drain; break;
                    case 'G': a |= Abilities.Guard; break;
                    case 'L': a |= Abilities.Lethal; break;
                    case 'W': a |= Abilities.Ward; break;
                }
            }
            return a;
        }

        public static string Format(this Abilities a)
        {
            var sb = new StringBuilder(6);
            sb.Append((a & Abilities.Breakthrough) != 0 ? 'B' : '-');
            sb.Append((a & Abilities.Charge) != 0 ? 'C' : '-');
            sb.Append((a & Abilities.Drain) != 0 ? 'D' : '-');
            sb.Append((a & Abilities.Guard) != 0 ? 'G' : '-');
            sb.Append((a & Abilities.Lethal) != 0 ? 'L' : '-');
            sb.Append((a & Abilities.Ward) != 0 ? 'W' : '-');
            return sb.ToString();
        }

        public static bool Has(this Abilities a, Abilities flag) => (a & flag) != 0;
    }
}
