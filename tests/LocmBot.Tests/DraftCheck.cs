using System;
using System.Collections.Generic;
using System.IO;

namespace Locm.Tests
{
    /// <summary>
    /// Проверка драфта по логам: на каждом пике считаем выбор текущего RatingDraft (с той же уже набранной колодой)
    /// и сравниваем с фактическим "> PICK n". Показывает, какая версия драфта играла, и где новая выбрала бы иначе.
    /// </summary>
    public static class DraftCheck
    {
        public static int Run(string[] paths, bool verbose, string[] overrides)
        {
            Tuning.Apply(overrides, new SearchBattle(), null);
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*.log"));
                else files.Add(p);
            }
            files.Sort(StringComparer.Ordinal);

            int picks = 0, same = 0, sameByFormula = 0;
            foreach (var f in files)
            {
                var draftTable = new RatingDraft();
                var pickedTable = new List<Card>();
                var pickedFormula = new List<Card>();
                var actual = new List<Card>();
                foreach (var t in Replay.Parse(File.ReadAllText(f)))
                {
                    if (!t.Input.LooksLikeDraft) break;
                    int idx = ParsePick(t.Answer);
                    if (idx < 0 || idx > 2) continue;
                    CardRating.UseTable = true;
                    int mineTable = draftTable.Pick(t.Input, actual);
                    CardRating.UseTable = false;
                    int mineFormula = draftTable.Pick(t.Input, actual);
                    CardRating.UseTable = true;
                    picks++;
                    if (mineTable == idx) same++;
                    if (mineFormula == idx) sameByFormula++;
                    if (verbose && mineTable != idx)
                        Console.WriteLine($"{Path.GetFileName(f)} pick {actual.Count}: actual {Name(t.Input.Cards[idx])}, table {Name(t.Input.Cards[mineTable])}, formula {Name(t.Input.Cards[mineFormula])}");
                    actual.Add(t.Input.Cards[idx]);
                }
            }
            Console.WriteLine($"{files.Count} files, {picks} picks: same as table draft {Pct(same, picks)}, same as formula draft {Pct(sameByFormula, picks)}");
            return 0;
        }

        private static int ParsePick(string answer)
        {
            var p = answer.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int v;
            if (p.Length >= 2 && p[0] == "PICK" && int.TryParse(p[1], out v)) return v;
            if (p.Length >= 1 && p[0] == "PASS") return 0;
            return -1;
        }

        private static string Name(Card c) => $"#{c.Number} {CardDb.Name(c.Number)}";
        private static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("F1") + "%";
    }
}
