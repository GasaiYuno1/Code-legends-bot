using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Locm.Tests
{
    /// <summary>
    /// Выгрузка для модели «реальный ответ противника»: по парам соседних ходов игрока из лога арены
    /// S_after (позиция после его хода) → S_next (его следующий ввод, то есть после реального ответа противника).
    /// Строка: gameId, признаки S_after (NetFeatures + мана/рука противника на его ход + мой добор), pred (DeepReplyScore
    /// по текущей модели «только атаки»), target (моя оценка S_next без добранных карт), died.
    /// </summary>
    public static class ReplyData
    {
        public static int Run(string[] paths, string outPath, int step)
        {
            var search = new SearchBattle();
            var eval = search.Eval;
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.log"));
                else files.Add(p);
            }
            files.Sort(StringComparer.Ordinal);
            var f = new float[NetFeatures.Count];
            int rows = 0, died = 0, seen = 0;
            using (var w = new StreamWriter(outPath))
            {
                var sb = new StringBuilder();
                sb.Append("game\tturn");
                for (int i = 0; i < NetFeatures.Count; i++) sb.Append("\tf").Append(i);
                sb.Append("\toppMana\toppHand\toppDeck\tmyDraw\tmyHand\tpred\tstatic\ttarget\tdied");
                w.WriteLine(sb.ToString());
                foreach (var file in files)
                {
                    var battle = new List<Replay.LoggedTurn>();
                    foreach (var t in Replay.Parse(File.ReadAllText(file)))
                        if (!t.Input.LooksLikeDraft) battle.Add(t);
                    string game = Path.GetFileNameWithoutExtension(file);
                    for (int i = 0; i + 1 < battle.Count; i++)
                    {
                        if (seen++ % step != 0) continue;
                        var s = GameState.FromInput(battle[i].Input);
                        if (s.IsOver) continue;
                        var line = GameAction.ParseSequence(battle[i].Answer);
                        if (s.ApplySequence(line) != 0) continue;
                        if (s.IsOver) continue;                     // выиграл своим ходом — ответа нет
                        var next = GameState.FromInput(battle[i + 1].Input);
                        // цель: моя оценка следующей позиции; добранные карты убираем (на момент S_after их нет)
                        int myDraw = battle[i + 1].Input.Me.Draw;
                        bool dead = next.Players[0].Health <= 0;
                        double target;
                        if (dead) target = -100;
                        else
                        {
                            target = eval.Score(next, 0) - myDraw * eval.HandCardW;
                            if (target > 100) target = 100; else if (target < -100) target = -100;
                        }
                        double pred = search.DeepReplyScore(s, 0);
                        if (pred > 100) pred = 100; else if (pred < -100) pred = -100;
                        double stat = eval.Score(s, 0);
                        NetFeatures.Extract(s, 0, f);
                        var o = s.Players[1];
                        int oppMana = Math.Min(12, o.MaxMana + 1);
                        int oppHand = Math.Min(8, o.HandCount + o.NextTurnDraw);
                        sb.Clear();
                        sb.Append(game).Append('\t').Append(i);
                        for (int k = 0; k < f.Length; k++) sb.Append('\t').Append(f[k].ToString("G6", CultureInfo.InvariantCulture));
                        sb.Append('\t').Append(oppMana).Append('\t').Append(oppHand).Append('\t').Append(o.DeckSize)
                          .Append('\t').Append(myDraw).Append('\t').Append(s.Players[0].HandCount)
                          .Append('\t').Append(pred.ToString("G6", CultureInfo.InvariantCulture))
                          .Append('\t').Append(stat.ToString("G6", CultureInfo.InvariantCulture))
                          .Append('\t').Append(target.ToString("G6", CultureInfo.InvariantCulture))
                          .Append('\t').Append(dead ? 1 : 0);
                        w.WriteLine(sb.ToString());
                        rows++;
                        if (dead) died++;
                    }
                }
            }
            Console.WriteLine($"{files.Count} files, {rows} rows ({died} deaths) -> {outPath}");
            return 0;
        }
    }
}
