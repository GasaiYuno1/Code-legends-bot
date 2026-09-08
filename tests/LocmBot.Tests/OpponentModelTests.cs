using System.Collections.Generic;

namespace Locm.Tests
{
    public static class OpponentModelTests
    {
        private static TurnInput Triple(int a, int b, int c)
        {
            var t = new TurnInput();
            t.Me.Mana = 0;
            t.Cards.Add(CardDb.Get(a));
            t.Cards.Add(CardDb.Get(b));
            t.Cards.Add(CardDb.Get(c));
            return t;
        }

        private static TurnInput Battle(params string[] oppActions)
        {
            var t = new TurnInput();
            foreach (var a in oppActions)
            {
                int sp = a.IndexOf(' ');
                t.OpponentActions.Add(new OpponentAction { CardNumber = int.Parse(a.Substring(0, sp)), Action = a.Substring(sp + 1) });
            }
            return t;
        }

        /// <summary>30 троек: i-я = {i+1, i+31, i+61} — все номера различны.</summary>
        private static OpponentModel FullDraft()
        {
            var m = new OpponentModel();
            for (int i = 0; i < 30; i++) m.ObserveDraft(Triple(i + 1, i + 31, i + 61));
            return m;
        }

        [Test]
        public static void WithoutDraft_SamplesFromPool()
        {
            var m = new OpponentModel();
            Assert.False(m.DeckKnown);
            ulong rng = 12345;
            var hand = new Card[5];
            Assert.Equal(5, m.Sample(ref rng, hand, 5, 1000));
            for (int i = 0; i < 5; i++) Assert.True(CardDb.Contains(hand[i].Number));
            Assert.Equal(-1, m.UnknownCount());
        }

        [Test]
        public static void RevealedCards_ReduceUnknownAndNeverAppearInSample()
        {
            var m = FullDraft();
            Assert.True(m.DeckKnown);
            Assert.Equal(30, m.UnknownCount());
            // сыграл карты из троек 0 (номер 1) и 5 (номер 36); ATTACK не считается
            m.ObserveBattle(Battle("1 SUMMON 3", "36 USE 5 -1", "1 ATTACK 3 -1"));
            Assert.Equal(2, m.Revealed);
            Assert.Equal(0, m.Unmatched);
            Assert.Equal(28, m.UnknownCount());
            ulong rng = 7;
            var hand = new Card[8];
            for (int rep = 0; rep < 50; rep++)
            {
                int n = m.Sample(ref rng, hand, 8, 1000);
                Assert.Equal(8, n);
                var seen = new HashSet<int>();
                for (int i = 0; i < n; i++)
                {
                    int num = hand[i].Number;
                    // ни одна карта из уже раскрытых троек, все — из разных троек, instanceId уникальны
                    Assert.True(num != 1 && num != 31 && num != 61 && num != 6 && num != 36 && num != 66, "card from revealed triple: " + num);
                    int triple = (num - 1) % 30;
                    Assert.True(seen.Add(triple), "two cards from one triple");
                    Assert.Equal(1000 + i, hand[i].InstanceId);
                    Assert.Equal(Location.MyHand, hand[i].Location);
                }
            }
        }

        [Test]
        public static void Matching_HandlesDuplicateCardsAcrossTriples()
        {
            var m = new OpponentModel();
            // карта 5 предложена в тройках 0 и 1; карта 7 — только в тройке 0
            m.ObserveDraft(Triple(5, 7, 9));
            m.ObserveDraft(Triple(5, 1, 2));
            for (int i = 2; i < 30; i++) m.ObserveDraft(Triple(i + 10, i + 40, i + 70));
            // показаны 5 и 7: жадное приписывание 5→тройка 0 сломало бы 7, паросочетание должно найти 5→1, 7→0
            m.ObserveBattle(Battle("5 SUMMON 2", "7 SUMMON 4"));
            Assert.Equal(0, m.Unmatched);
            Assert.Equal(28, m.UnknownCount());
            // третья показанная карта 5 не помещается — Unmatched
            m.ObserveBattle(Battle("5 SUMMON 6"));
            Assert.Equal(1, m.Unmatched);
            Assert.Equal(28, m.UnknownCount());
        }

        [Test]
        public static void Sample_CapsAtUnknownCount()
        {
            var m = FullDraft();
            var actions = new List<string>();
            for (int i = 0; i < 28; i++) actions.Add((i + 1) + " SUMMON " + (i + 2));
            m.ObserveBattle(Battle(actions.ToArray()));
            Assert.Equal(2, m.UnknownCount());
            ulong rng = 99;
            var hand = new Card[8];
            Assert.Equal(2, m.Sample(ref rng, hand, 8, 1000));
            Assert.True(hand[0].Number != hand[1].Number);
        }

        [Test]
        public static void Reset_ForgetsEverything()
        {
            var m = FullDraft();
            m.ObserveBattle(Battle("1 SUMMON 3"));
            m.Reset();
            Assert.False(m.DeckKnown);
            Assert.Equal(0, m.Revealed);
            Assert.Equal(0, m.Triples);
        }
    }
}
