using System.Collections.Generic;
using static Locm.Tests.TestUtil;

namespace Locm.Tests
{
    public static class DraftTests
    {
        private static TurnInput Triple(params int[] baseIds)
        {
            var t = new TurnInput();
            t.Me.Mana = 0;
            for (int i = 0; i < 3; i++) t.Cards.Add(CardDb.Get(baseIds[i]));
            return t;
        }

        /// <summary>Тесты формулы гоняются с выключенной таблицей.</summary>
        private static RatingDraft FormulaDraft()
        {
            CardRating.UseTable = false;
            return new RatingDraft();
        }

        [Test]
        public static void Table_CoversAllCardsAndRanksKnownStrongCardsHigh()
        {
            CardRating.UseTable = true;
            Assert.Equal(CardDb.Count + 1, CardTable.Rating.Length);
            Assert.True(CardTable.Picks > 0);
            // Acid Golem и Decimate — общепризнанные топы контеста; Wurm 0/1 за 1 — низ
            Assert.True(CardRating.Rate(CardDb.Get(18)) > CardRating.Rate(CardDb.Get(92)), "Acid Golem > Wurm");
            Assert.True(CardRating.Rate(CardDb.Get(151)) > 0, "Decimate above average");
            var d = new RatingDraft();
            Assert.Equal(0, d.Pick(Triple(18, 92, 92), new System.Collections.Generic.List<Card>()));
        }

        [Test]
        public static void PicksHigherRatedCard()
        {
            var d = FormulaDraft();
            // Acid Golem 7/4 за 4 против Plated Toad 1/5 за 2 и Wurm 0/1 G за 1
            Assert.Equal(0, d.Pick(Triple(18, 4, 92), new List<Card>()));
            Assert.Equal(2, d.Pick(Triple(92, 4, 18), new List<Card>()));
        }

        [Test]
        public static void ManaCurve_ShiftsPickTowardsMissingCosts()
        {
            var d = FormulaDraft();
            var picked = new List<Card>();
            var triple = Triple(7, 19, 19);   // Rootkin Sapling 2/2 W (2) против Foulbeast 5/6 (5)
            Assert.Equal(0, d.Pick(triple, picked), "empty deck: cheap ward creature first");
            for (int i = 0; i < 7; i++) picked.Add(CardDb.Get(6)); // семь 2-дропов
            Assert.Equal(1, d.Pick(triple, picked), "curve full of 2-drops: take the 5-drop");
        }

        [Test]
        public static void ItemLimit_PrefersCreatureAfterManyItems()
        {
            var d = FormulaDraft();
            var picked = new List<Card>();
            var triple = Triple(151, 9, 9);   // Decimate против Corrupted Beavrat 3/4
            Assert.Equal(0, d.Pick(triple, picked));
            for (int i = 0; i < d.MaxItems; i++) picked.Add(CardDb.Get(144));
            Assert.Equal(1, d.Pick(triple, picked));
        }

        [Test]
        public static void RatingsAreSane()
        {
            CardRating.UseTable = false;
            double sum = 0;
            int n = 0;
            for (int id = 1; id <= CardDb.Count; id++)
            {
                double r = CardRating.Rate(CardDb.Get(id));
                Assert.True(!double.IsNaN(r) && r > -15 && r < 15, "card " + id + " rating " + r);
                if (CardDb.Get(id).IsCreature) { sum += r; n++; }
            }
            double avg = sum / n;
            Assert.True(avg > -3 && avg < 3, "average creature rating " + avg);
            Assert.True(CardRating.Rate(CardDb.Get(151)) < 8, "Decimate is capped");
            Assert.True(CardRating.Rate(CardDb.Get(18)) > CardRating.Rate(CardDb.Get(91)), "Acid Golem > Flumpy");
            Assert.True(CardRating.Rate(CardDb.Get(53)) > CardRating.Rate(CardDb.Get(15)), "Charge+Lethal 1/1 > vanilla 4/5 at cost 4");
        }
    }
}
