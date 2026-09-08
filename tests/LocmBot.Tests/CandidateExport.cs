using System;
using System.Collections.Generic;
using System.IO;

namespace Locm.Tests
{
    /// <summary>
    /// Выгрузка данных для обучения оценки листа по решениям экспертов: на каждом ходу из логов
    /// поиск бота строит кандидатов (состояния после моего хода), для каждого считается лист после
    /// ответа противника (DeepReply/Reply) и признаки NetFeatures; кандидат, совпадающий с реальным
    /// ходом эксперта, помечается как выбранный. Формат: бинарный float32: [turnId, isExpert, f1..fN].
    /// </summary>
    public static class CandidateExport
    {
        public static int Run(string dir, string outPath, int topK, int step, int ms)
        {
            var files = Directory.GetFiles(dir, "*.log");
            Array.Sort(files, StringComparer.Ordinal);
            var search = new SearchBattle();
            var f = new float[NetFeatures.Count];
            int turns = 0, rows = 0, expertFound = 0, seen = 0;
            using (var bw = new BinaryWriter(File.Create(outPath)))
            {
                bw.Write(NetFeatures.Count);
                foreach (var file in files)
                {
                    foreach (var t in Replay.Parse(File.ReadAllText(file)))
                    {
                        if (t.Input.LooksLikeDraft) continue;
                        var s = GameState.FromInput(t.Input);
                        if (s.IsOver) continue;
                        if (seen++ % step != 0) continue;

                        // конечное состояние эксперта
                        var expert = s.Clone();
                        expert.ApplySequence(GameAction.ParseSequence(t.Answer));
                        if (expert.IsOver) continue;   // летал — нечему учиться
                        ulong expertHash = expert.Hash();

                        // кандидаты поиска (состояния после моего хода), лучшие по текущей оценке
                        var cands = search.CollectCandidates(s, new TurnClock(ms), topK);
                        bool found = false;
                        int written = 0;
                        foreach (var c in cands)
                        {
                            bool isExpert = c.Hash() == expertHash;
                            if (isExpert) found = true;
                            if (!WriteLeaf(bw, search, c, turns, isExpert, f)) continue;
                            written++;
                        }
                        if (!found && WriteLeaf(bw, search, expert, turns, true, f)) written++;
                        if (written < 2) continue;
                        rows += written;
                        turns++;
                        if (found) expertFound++;
                    }
                }
            }
            Console.WriteLine($"{files.Length} files, {turns} turns, {rows} candidate rows -> {outPath}; expert among top-{topK}: {100.0 * expertFound / Math.Max(1, turns):F1}%");
            return 0;
        }

        private static bool WriteLeaf(BinaryWriter bw, SearchBattle search, GameState afterMove, int turnId, bool isExpert, float[] f)
        {
            var leaf = search.ReplyState(afterMove, 0);   // после ответа противника (глубокий перебор его атак)
            if (leaf == null) return false;
            NetFeatures.Extract(leaf, 0, f);
            bw.Write((float)turnId);
            bw.Write(isExpert ? 1f : 0f);
            for (int i = 0; i < f.Length; i++) bw.Write(f[i]);
            return true;
        }
    }
}
