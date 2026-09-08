using System;

namespace Locm
{
    /// <summary>
    /// Признаки позиции для обучаемой оценки с точки зрения игрока me (масштаб ~0..1).
    /// Этот же код выгружает обучающие данные (режим features тестового проекта), чтобы C# и обучение не разошлись.
    /// </summary>
    public static class NetFeatures
    {
        public const int PerSide = 22;
        public const int Count = PerSide * 2 + 5;

        public static void Extract(GameState s, int me, float[] f)
        {
            var p = s.Players[me];
            var o = s.Players[1 - me];
            int k = 0;
            int pAtk = Side(p, f, ref k);
            int oAtk = Side(o, f, ref k);
            f[k++] = (pAtk - o.Health) / 30f;    // близость моего летала
            f[k++] = (oAtk - p.Health) / 30f;    // близость его летала
            f[k++] = (p.Health - o.Health) / 30f;
            f[k++] = (p.MaxMana - o.MaxMana) / 12f;
            f[k++] = s.Current == me ? 1f : 0f;  // лист после моего хода (1) или после ответа противника (0)
        }

        private static int Side(PlayerState p, float[] f, ref int k)
        {
            int sumA = 0, sumD = 0, maxA = 0, maxD = 0, guards = 0, guardDef = 0, wards = 0, lethal = 0, drain = 0, bt = 0, charge = 0, def1 = 0, minAD = 0;
            for (int i = 0; i < p.BoardCount; i++)
            {
                ref Creature c = ref p.Board[i];
                sumA += c.Attack;
                sumD += c.Defense;
                if (c.Attack > maxA) maxA = c.Attack;
                if (c.Defense > maxD) maxD = c.Defense;
                if (c.Has(Abilities.Guard)) { guards++; guardDef += c.Defense; }
                if (c.Has(Abilities.Ward)) wards++;
                if (c.Has(Abilities.Lethal)) lethal++;
                if (c.Has(Abilities.Drain)) drain += c.Attack;
                if (c.Has(Abilities.Breakthrough)) bt += c.Attack;
                if (c.Has(Abilities.Charge)) charge++;
                if (c.Defense <= 1) def1++;
                minAD += Math.Min(c.Attack, c.Defense);
            }
            f[k++] = p.Health / 30f;
            f[k++] = Math.Max(0, 10 - p.Health) / 10f;
            f[k++] = Math.Max(0, 20 - p.Health) / 20f;
            f[k++] = p.BoardCount / 6f;
            f[k++] = sumA / 20f;
            f[k++] = sumD / 20f;
            f[k++] = maxA / 12f;
            f[k++] = maxD / 12f;
            f[k++] = guards / 3f;
            f[k++] = guardDef / 15f;
            f[k++] = wards / 3f;
            f[k++] = lethal / 3f;
            f[k++] = drain / 10f;
            f[k++] = bt / 10f;
            f[k++] = charge / 3f;
            f[k++] = def1 / 3f;
            f[k++] = minAD / 15f;
            f[k++] = p.HandCount / 8f;
            f[k++] = Math.Max(0, p.NextTurnDraw - 1) / 3f;
            f[k++] = p.DeckSize / 30f;
            f[k++] = p.NextRune / 25f;
            f[k++] = p.MaxMana / 12f;
            return sumA;
        }
    }

    /// <summary>Сеть Count → Hidden (tanh) → 1: логит вероятности выигрыша игрока me. Веса — NetWeights (генерируются tools/arena/train_net.py).</summary>
    public sealed class NetEval
    {
        public static bool Available => NetWeights.Hidden > 0;

        private readonly int _h;
        private readonly float[] _w1;   // [h * Count]
        private readonly float[] _b1;   // [h]
        private readonly float[] _w2;   // [h]
        private readonly float _b2;
        private readonly float[] _f = new float[NetFeatures.Count];

        public NetEval()
        {
            _h = NetWeights.Hidden;
            var v = NetWeights.Packed.Length == 0 ? new string[0] : NetWeights.Packed.Split(',');
            int n = NetFeatures.Count;
            _w1 = new float[_h * n];
            _b1 = new float[_h];
            _w2 = new float[_h];
            int i = 0;
            for (int j = 0; j < _w1.Length && i < v.Length; j++) _w1[j] = int.Parse(v[i++]) / 10000f;
            for (int j = 0; j < _h && i < v.Length; j++) _b1[j] = int.Parse(v[i++]) / 10000f;
            for (int j = 0; j < _h && i < v.Length; j++) _w2[j] = int.Parse(v[i++]) / 10000f;
            _b2 = i < v.Length ? int.Parse(v[i]) / 10000f : 0f;
        }

        public double Logit(GameState s, int me)
        {
            NetFeatures.Extract(s, me, _f);
            int n = NetFeatures.Count;
            double outv = _b2;
            for (int j = 0; j < _h; j++)
            {
                double z = _b1[j];
                int off = j * n;
                for (int i = 0; i < n; i++) z += _w1[off + i] * _f[i];
                outv += _w2[j] * Math.Tanh(z);
            }
            return outv;
        }
    }
}
