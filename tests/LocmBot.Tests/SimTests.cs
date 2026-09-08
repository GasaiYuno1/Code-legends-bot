using System.Collections.Generic;
using System.IO;

namespace Locm.Tests
{
    /// <summary>Граничные случаи правил боя. Ожидания — по коду GameState/Gamer официального арбитра.</summary>
    public static class SimTests
    {
        // ------------------------------------------------------------ helpers

        /// <summary>Синтетическое существо: baseId = 200 + id, чтобы не путать с реальными картами.</summary>
        private static Card Cr(int id, int atk, int def, string abilities = "------", int cost = 1, int myHp = 0, int oppHp = 0, int draw = 0) =>
            new Card(200 + id, id, Location.MyHand, CardType.Creature, cost, atk, def, AbilitiesExt.Parse(abilities), myHp, oppHp, draw);

        private static Card Item(CardType type, int id, int atk, int def, string abilities = "------", int cost = 0, int myHp = 0, int oppHp = 0, int draw = 0) =>
            new Card(300 + id, id, Location.MyHand, type, cost, atk, def, AbilitiesExt.Parse(abilities), myHp, oppHp, draw);

        /// <summary>Реальная карта арбитра с заданным instanceId.</summary>
        private static Card Db(int baseId, int id) => CardDb.Get(baseId).WithInstance(id, Location.MyHand);

        private static GameState NewState(int mana = 12, int deck = 20)
        {
            var s = new GameState();
            foreach (var p in s.Players)
            {
                p.Health = 30;
                p.NextRune = 25;
                p.DeckSize = deck;
                p.NextTurnDraw = 1;
                p.MaxMana = mana;
                p.Mana = mana;
            }
            s.Players[1].Mana = 0;
            return s;
        }

        private static void Board(GameState s, int player, Card c, bool canAttack = true) =>
            s.Players[player].AddCreature(Creature.FromInput(c, canAttack));

        private static Creature Cre(GameState s, int player, int id)
        {
            int i = s.Players[player].FindCreature(id);
            Assert.True(i >= 0, $"creature {id} not on board of P{player}");
            return s.Players[player].Board[i];
        }

        private static bool OnBoard(GameState s, int player, int id) => s.Players[player].FindCreature(id) >= 0;

        private static List<GameAction> Legal(GameState s)
        {
            var l = new List<GameAction>();
            s.LegalActions(l);
            return l;
        }

        private static string Joined(List<GameAction> l) => GameAction.Format(l);

        // ------------------------------------------------------------ атаки

        [Test]
        public static void Attack_TradesDamageBothWays()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 3, 4));
            Board(s, 1, Cr(2, 2, 5));
            s.Apply(GameAction.Attack(1, 2));
            Assert.Equal(2, Cre(s, 0, 1).Defense);
            Assert.Equal(2, Cre(s, 1, 2).Defense);
            Assert.False(Cre(s, 0, 1).CanAttack);
            Assert.True(Cre(s, 0, 1).HasAttacked);
            Assert.Equal(30, s.Players[1].Health);
        }

        [Test]
        public static void Attack_BothDieWhenDamageReachesDefense()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 3, 2));
            Board(s, 1, Cr(2, 2, 3));
            s.Apply(GameAction.Attack(1, 2));
            Assert.False(OnBoard(s, 0, 1));
            Assert.False(OnBoard(s, 1, 2));
        }

        [Test]
        public static void Attack_Face_DamagesPlayerAndSpendsAttack()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 4, 1));
            s.Apply(GameAction.Attack(1, -1));
            Assert.Equal(26, s.Players[1].Health);
            Assert.False(Cre(s, 0, 1).CanAttack);
            Assert.False(s.IsLegal(GameAction.Attack(1, -1)));
        }

        [Test]
        public static void Attack_ZeroAttack_ChangesNothingButIsSpent()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 0, 3));
            Board(s, 1, Cr(2, 2, 2, "-----W"));
            s.Apply(GameAction.Attack(1, 2));
            Assert.True(Cre(s, 1, 2).Has(Abilities.Ward), "ward stays after 0-damage hit");
            Assert.Equal(2, Cre(s, 1, 2).Defense);
            Assert.Equal(1, Cre(s, 0, 1).Defense);
            Assert.False(Cre(s, 0, 1).CanAttack);
        }

        // ------------------------------------------------------------ Ward / Lethal / Breakthrough / Drain

        [Test]
        public static void Ward_BlocksFirstDamageAndDrops()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 3, 3));
            Board(s, 1, Cr(2, 2, 2, "-----W"));
            s.Apply(GameAction.Attack(1, 2));
            var d = Cre(s, 1, 2);
            Assert.Equal(2, d.Defense);
            Assert.False(d.Has(Abilities.Ward));
            Assert.Equal(1, Cre(s, 0, 1).Defense, "attacker still takes retaliation");
        }

        [Test]
        public static void Ward_OnAttacker_BlocksRetaliation()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 1, 1, "-----W"));
            Board(s, 1, Cr(2, 5, 5));
            s.Apply(GameAction.Attack(1, 2));
            var a = Cre(s, 0, 1);
            Assert.Equal(1, a.Defense);
            Assert.False(a.Has(Abilities.Ward));
            Assert.Equal(4, Cre(s, 1, 2).Defense);

            // защитник с 0 атаки не снимает Ward атакующего
            var s2 = NewState();
            Board(s2, 0, Cr(1, 1, 1, "-----W"));
            Board(s2, 1, Cr(2, 0, 5));
            s2.Apply(GameAction.Attack(1, 2));
            Assert.True(Cre(s2, 0, 1).Has(Abilities.Ward));
        }

        [Test]
        public static void Lethal_KillsAnyDamagedCreature_ButNotThroughWard()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 1, 5, "----L-"));
            Board(s, 1, Cr(2, 1, 9));
            Board(s, 1, Cr(3, 1, 9, "-----W"));
            s.Apply(GameAction.Attack(1, 2));
            Assert.False(OnBoard(s, 1, 2), "lethal kills 1/9");

            var s2 = NewState();
            Board(s2, 0, Cr(1, 1, 5, "----L-"));
            Board(s2, 1, Cr(3, 1, 9, "-----W"));
            s2.Apply(GameAction.Attack(1, 3));
            Assert.True(OnBoard(s2, 1, 3), "ward saves from lethal");
            Assert.Equal(9, Cre(s2, 1, 3).Defense);
            Assert.False(Cre(s2, 1, 3).Has(Abilities.Ward));
        }

        [Test]
        public static void Lethal_OnDefender_KillsAttacker()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 5, 5));
            Board(s, 1, Cr(2, 1, 1, "----L-"));
            s.Apply(GameAction.Attack(1, 2));
            Assert.False(OnBoard(s, 0, 1));
            Assert.False(OnBoard(s, 1, 2));

            // Lethal у защитника не работает против Ward атакующего (урон 0)
            var s2 = NewState();
            Board(s2, 0, Cr(1, 5, 5, "-----W"));
            Board(s2, 1, Cr(2, 1, 1, "----L-"));
            s2.Apply(GameAction.Attack(1, 2));
            Assert.True(OnBoard(s2, 0, 1));
            Assert.Equal(5, Cre(s2, 0, 1).Defense);
        }

        [Test]
        public static void Breakthrough_ExcessGoesToFace_NotVsWard()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 5, 5, "B-----"));
            Board(s, 1, Cr(2, 2, 2));
            s.Apply(GameAction.Attack(1, 2));
            Assert.Equal(27, s.Players[1].Health);
            Assert.False(OnBoard(s, 1, 2));

            var s2 = NewState();
            Board(s2, 0, Cr(1, 5, 5, "B-----"));
            Board(s2, 1, Cr(2, 2, 2, "-----W"));
            s2.Apply(GameAction.Attack(1, 2));
            Assert.Equal(30, s2.Players[1].Health);
            Assert.True(OnBoard(s2, 1, 2));
        }

        [Test]
        public static void Breakthrough_ExactKillGivesNoExcess_LethalDoesNotCount()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 2, 2, "B---L-"));
            Board(s, 1, Cr(2, 1, 5));
            s.Apply(GameAction.Attack(1, 2));
            Assert.False(OnBoard(s, 1, 2), "lethal kills");
            Assert.Equal(30, s.Players[1].Health, "no breakthrough excess: 2 < 5");

            var s2 = NewState();
            Board(s2, 0, Cr(1, 5, 5, "B---L-"));
            Board(s2, 1, Cr(2, 1, 3));
            s2.Apply(GameAction.Attack(1, 2));
            Assert.Equal(28, s2.Players[1].Health);
        }

        [Test]
        public static void Drain_HealsOnDamage_NotVsWard_AlsoOnFace()
        {
            var s = NewState();
            s.Players[0].Health = 20;
            Board(s, 0, Cr(1, 3, 3, "--D---"));
            Board(s, 0, Cr(2, 3, 3, "--D---"));
            Board(s, 0, Cr(3, 3, 3, "--D---"));
            Board(s, 1, Cr(4, 1, 5));
            Board(s, 1, Cr(5, 1, 5, "-----W"));
            s.Apply(GameAction.Attack(1, 4));
            Assert.Equal(23, s.Players[0].Health);
            s.Apply(GameAction.Attack(2, 5));
            Assert.Equal(23, s.Players[0].Health, "no drain through ward");
            s.Apply(GameAction.Attack(3, -1));
            Assert.Equal(26, s.Players[0].Health);
            Assert.Equal(27, s.Players[1].Health);
        }

        [Test]
        public static void SummonEffects_ShownUntilFirstFightOrItem()
        {
            // Причуда арбитра: после боя или предмета поля myHp/oppHp/draw существа во вводе становятся нулями.
            var s = NewState();
            s.Players[0].AddHandCard(Db(13, 10));   // Swamp Terror: 1/-1
            s.Players[0].AddHandCard(Db(13, 11));
            s.Players[0].AddHandCard(Db(118, 12));  // Royal Helm +0/+3
            Board(s, 1, Db(30, 20), canAttack: false); // Venomous Bog Hopper: opp -2
            s.Apply(GameAction.Summon(10));
            s.Apply(GameAction.Summon(11));
            Assert.Equal("13 10 1 0 4 5 3 ------ 1 -1 0", Cre(s, 0, 10).ToInputLine(false));
            s.Apply(GameAction.Attack(10, -1));
            Assert.Equal("13 10 1 0 4 5 3 ------ 0 0 0", Cre(s, 0, 10).ToInputLine(false));
            s.Apply(GameAction.Use(12, 11));
            Assert.Equal("13 11 1 0 4 5 6 ------ 0 0 0", Cre(s, 0, 11).ToInputLine(false));
            Assert.Equal("30 20 -1 0 3 4 2 ------ 0 -2 0", Cre(s, 1, 20).ToInputLine(true));
            Board(s, 0, Cr(1, 1, 1));
            s.Apply(GameAction.Attack(1, 20));
            Assert.Equal("30 20 -1 0 3 4 1 ------ 0 0 0", Cre(s, 1, 20).ToInputLine(true));
        }

        [Test]
        public static void Defender_BreakthroughAndDrain_DoNotWork()
        {
            var s = NewState();
            s.Players[1].Health = 20;
            Board(s, 0, Cr(1, 1, 1));
            Board(s, 1, Cr(2, 5, 5, "B-D---"));
            s.Apply(GameAction.Attack(1, 2));
            Assert.False(OnBoard(s, 0, 1));
            Assert.Equal(30, s.Players[0].Health, "no breakthrough on defence");
            Assert.Equal(20, s.Players[1].Health, "no drain on defence");
        }

        // ------------------------------------------------------------ Guard / Charge

        [Test]
        public static void Guard_MustBeAttackedFirst()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 3, 3));
            Board(s, 1, Cr(2, 1, 1));
            Board(s, 1, Cr(3, 1, 3, "---G--"));
            Assert.Equal("ATTACK 1 3;PASS", Joined(Legal(s)));
            Assert.False(s.IsLegal(GameAction.Attack(1, -1)));
            Assert.False(s.IsLegal(GameAction.Attack(1, 2)));
            Assert.True(s.IsLegal(GameAction.Attack(1, 3)));
            s.Apply(GameAction.Attack(1, 3));
            Assert.False(OnBoard(s, 1, 3));
            Board(s, 0, Cr(4, 1, 1));
            Assert.Equal("ATTACK 4 -1;ATTACK 4 2;PASS", Joined(Legal(s)));
        }

        [Test]
        public static void Charge_CanAttackOnSummon_OthersNextTurn()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Cr(1, 2, 2, "-C----"));
            s.Players[0].AddHandCard(Cr(2, 2, 2));
            s.Apply(GameAction.Summon(1));
            s.Apply(GameAction.Summon(2));
            Assert.True(Cre(s, 0, 1).CanAttack);
            Assert.False(Cre(s, 0, 2).CanAttack);
            Assert.True(s.IsLegal(GameAction.Attack(1, -1)));
            Assert.False(s.IsLegal(GameAction.Attack(2, -1)));
            s.EndTurn();
            s.EndTurn();
            Assert.True(Cre(s, 0, 2).CanAttack);
        }

        [Test]
        public static void ChargeItem_GivesAttackOnlyIfNotAttackedThisTurn()
        {
            var s = NewState();
            var wings = Db(140, 10); // Grow Wings: Charge
            var wings2 = Db(140, 11);
            var wings3 = Db(140, 12);
            s.Players[0].AddHandCard(wings);
            s.Players[0].AddHandCard(wings2);
            s.Players[0].AddHandCard(wings3);
            s.Players[0].AddHandCard(Cr(1, 2, 2));
            Board(s, 0, Cr(2, 2, 2));
            Board(s, 0, Cr(3, 2, 2));

            s.Apply(GameAction.Summon(1));
            Assert.False(Cre(s, 0, 1).CanAttack);
            s.Apply(GameAction.Use(10, 1));
            Assert.True(Cre(s, 0, 1).CanAttack, "summoned this turn + charge item = can attack");
            Assert.True(Cre(s, 0, 1).Has(Abilities.Charge));

            s.Apply(GameAction.Attack(2, -1));
            s.Apply(GameAction.Use(11, 2));
            Assert.False(Cre(s, 0, 2).CanAttack, "already attacked: charge item does not refresh");

            s.Apply(GameAction.Use(12, 3));
            Assert.True(Cre(s, 0, 3).CanAttack, "not attacked yet: still can");
        }

        // ------------------------------------------------------------ предметы

        [Test]
        public static void GreenItem_AddsKeywordsAndStats_OnlyOwnCreatures()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(124, 10)); // Blood Grapes +2/+1 Drain
            Board(s, 0, Cr(1, 1, 1, "-----W"));
            Board(s, 1, Cr(2, 1, 1));
            Assert.True(s.IsLegal(GameAction.Use(10, 1)));
            Assert.False(s.IsLegal(GameAction.Use(10, 2)));
            Assert.False(s.IsLegal(GameAction.Use(10, -1)));
            s.Apply(GameAction.Use(10, 1));
            var c = Cre(s, 0, 1);
            Assert.Equal(3, c.Attack);
            Assert.Equal(2, c.Defense);
            Assert.Equal(Abilities.Drain | Abilities.Ward, c.Abilities);
            Assert.Equal(9, s.Players[0].Mana);
            Assert.Equal(0, s.Players[0].HandCount);
        }

        [Test]
        public static void GreenItem_NoOwnCreatures_NoLegalUse()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(118, 10)); // Royal Helm, cost 0
            Board(s, 1, Cr(2, 1, 1));
            Assert.Equal("PASS", Joined(Legal(s)));
        }

        [Test]
        public static void GreenItem_ThatWouldKill_LeavesCreatureUnchanged()
        {
            // Причуда арбитра: зелёный предмет «не убивает» — при defense <= 0 существо остаётся как было.
            var s = NewState();
            s.Players[0].AddHandCard(Item(CardType.GreenItem, 10, 0, -5));
            Board(s, 0, Cr(1, 2, 2));
            s.Apply(GameAction.Use(10, 1));
            Assert.True(OnBoard(s, 0, 1));
            Assert.Equal(2, Cre(s, 0, 1).Defense);
        }

        [Test]
        public static void RedItem_RemovesKeywordsBeforeDamage()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(148, 10)); // Helm Crusher: снять всё, потом -2
            s.Players[0].AddHandCard(Db(144, 11)); // Rune Axe: -2
            s.Players[0].AddHandCard(Db(141, 12)); // Throwing Knife: -1/-1
            Board(s, 1, Cr(1, 2, 2, "-----W"));
            Board(s, 1, Cr(2, 2, 2, "-----W"));
            Board(s, 1, Cr(3, 1, 1));

            s.Apply(GameAction.Use(10, 1));
            Assert.False(OnBoard(s, 1, 1), "ward removed first, then 2 damage kills 2/2");

            s.Apply(GameAction.Use(11, 2));
            Assert.True(OnBoard(s, 1, 2));
            Assert.Equal(2, Cre(s, 1, 2).Defense, "ward absorbs the whole item damage");
            Assert.False(Cre(s, 1, 2).Has(Abilities.Ward));

            s.Apply(GameAction.Use(12, 3));
            Assert.False(OnBoard(s, 1, 3));
            Assert.Equal(12 - 2 - 1 - 0, s.Players[0].Mana);
        }

        [Test]
        public static void RedItem_AttackFloorZero_AndPlayerEffects()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(146, 10)); // Cursed Scimitar: -2/-2, opp -2
            Board(s, 1, Cr(1, 1, 5));
            s.Apply(GameAction.Use(10, 1));
            Assert.Equal(0, Cre(s, 1, 1).Attack);
            Assert.Equal(3, Cre(s, 1, 1).Defense);
            Assert.Equal(28, s.Players[1].Health);
            Assert.False(s.IsLegal(GameAction.Use(10, -1)), "red item never targets face");
        }

        [Test]
        public static void BlueItem_FaceUsesDefenseField_CreatureSplitsEffects()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(155, 10)); // Scroll of Firebolt: def -3, opp -1
            s.Players[0].AddHandCard(Db(155, 11));
            s.Players[0].AddHandCard(Db(157, 12)); // Life Sap Drop: def -1, me +1, draw 1
            s.Players[0].Health = 20;
            Board(s, 1, Cr(1, 1, 5));

            s.Apply(GameAction.Use(10, -1));
            Assert.Equal(26, s.Players[1].Health);

            s.Apply(GameAction.Use(11, 1));
            Assert.Equal(2, Cre(s, 1, 1).Defense);
            Assert.Equal(25, s.Players[1].Health);

            s.Apply(GameAction.Use(12, -1));
            Assert.Equal(24, s.Players[1].Health);
            Assert.Equal(21, s.Players[0].Health);
            Assert.Equal(2, s.Players[0].NextTurnDraw);
        }

        [Test]
        public static void BlueItem_OnWardCreature_WardAbsorbs()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(158, 10)); // Tome of Thunder: -4
            Board(s, 1, Cr(1, 1, 2, "-----W"));
            s.Apply(GameAction.Use(10, 1));
            Assert.True(OnBoard(s, 1, 1));
            Assert.Equal(2, Cre(s, 1, 1).Defense);
            Assert.False(Cre(s, 1, 1).Has(Abilities.Ward));
        }

        // ------------------------------------------------------------ призыв и эффекты карт

        [Test]
        public static void Summon_EffectsHealthDrawAndMana()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Db(13, 10)); // Swamp Terror: me +1, opp -1
            s.Players[0].AddHandCard(Db(36, 11)); // Possessed Skull: draw 2
            s.Players[0].Health = 20;
            s.Apply(GameAction.Summon(10));
            Assert.Equal(21, s.Players[0].Health);
            Assert.Equal(29, s.Players[1].Health);
            s.Apply(GameAction.Summon(11));
            Assert.Equal(3, s.Players[0].NextTurnDraw);
            Assert.Equal(12 - 4 - 6, s.Players[0].Mana);
            Assert.Equal(2, s.Players[0].BoardCount);
            Assert.Equal(0, s.Players[0].HandCount);
        }

        [Test]
        public static void Summon_IllegalWhenBoardFullOrNoMana()
        {
            var s = NewState(mana: 1);
            s.Players[0].AddHandCard(Cr(1, 1, 1, cost: 1));
            s.Players[0].AddHandCard(Cr(2, 1, 1, cost: 2));
            Assert.True(s.IsLegal(GameAction.Summon(1)));
            Assert.False(s.IsLegal(GameAction.Summon(2)));
            for (int i = 0; i < 6; i++) Board(s, 0, Cr(10 + i, 1, 1));
            Assert.False(s.IsLegal(GameAction.Summon(1)));
            Assert.Equal("ATTACK 10 -1;ATTACK 11 -1;ATTACK 12 -1;ATTACK 13 -1;ATTACK 14 -1;ATTACK 15 -1;PASS", Joined(Legal(s)));
        }

        [Test]
        public static void Win_OpponentDeathChecksBeforeOwn()
        {
            var s = NewState();
            s.Players[0].Health = 2;
            s.Players[1].Health = 2;
            s.Players[0].AddHandCard(Db(25, 10)); // Spiney Chompleaf: -2 both
            s.Apply(GameAction.Summon(10));
            Assert.Equal(0, s.Winner);

            var s2 = NewState();
            s2.Players[0].Health = 3;
            s2.Players[0].AddHandCard(Db(45, 10)); // Night Howler: me -3
            s2.Apply(GameAction.Summon(10));
            Assert.Equal(1, s2.Winner);
        }

        // ------------------------------------------------------------ руны и добор

        [Test]
        public static void Runes_BreakingGivesOpponentDraws_HealingDoesNotRestore()
        {
            var s = NewState();
            var p = s.Players[1];
            p.ModifyHealth(-12);                  // 30 -> 18: руны 25 и 20
            Assert.Equal(3, p.NextTurnDraw);
            Assert.Equal(15, p.NextRune);
            p.ModifyHealth(+12);                  // лечение руны не возвращает
            Assert.Equal(15, p.NextRune);
            p.ModifyHealth(-9);                   // 30 -> 21: ничего
            Assert.Equal(3, p.NextTurnDraw);
            p.ModifyHealth(-6);                   // 21 -> 15: руна 15
            Assert.Equal(4, p.NextTurnDraw);
            Assert.Equal(10, p.NextRune);
            p.ModifyHealth(-15);                  // 15 -> 0: руны 10 и 5
            Assert.Equal(6, p.NextTurnDraw);
            Assert.Equal(0, p.NextRune);
            p.ModifyHealth(-5);
            Assert.Equal(6, p.NextTurnDraw, "no runes left");
        }

        [Test]
        public static void Runes_BrokenByAttack_OpponentDrawsNextTurn()
        {
            var s = NewState();
            Board(s, 0, Cr(1, 6, 6));
            s.Apply(GameAction.Attack(1, -1));    // 30 -> 24
            Assert.Equal(2, s.Players[1].NextTurnDraw);
            s.EndTurn();
            Assert.Equal(1, s.Current);
            Assert.Equal(2, s.Players[1].HandCount);
            Assert.Equal(18, s.Players[1].DeckSize);
            Assert.Equal(2, s.Players[1].DrawShown);
            Assert.Equal(1, s.Players[1].NextTurnDraw);
        }

        [Test]
        public static void Draw_EmptyDeck_LosesRunePerCard_NoDrawBonus()
        {
            var s = NewState(deck: 0);
            s.Players[1].NextTurnDraw = 3;
            s.Players[1].NextRune = 25;
            s.EndTurn();
            var p = s.Players[1];
            Assert.Equal(15, p.Health, "three runes lost: health set to 25, then 20, then 15");
            Assert.Equal(10, p.NextRune);
            Assert.Equal(0, p.HandCount);
            Assert.Equal(1, p.NextTurnDraw, "suicide runes do not give extra draws");
            Assert.Equal(-1, s.Winner);
        }

        [Test]
        public static void Draw_EmptyDeck_NoRunes_Kills()
        {
            var s = NewState(deck: 0);
            s.Players[1].NextRune = 0;
            s.Players[1].Health = 7;
            s.EndTurn();
            Assert.Equal(0, s.Players[1].Health);
            Assert.Equal(0, s.Winner);
        }

        [Test]
        public static void Draw_EmptyDeck_HandFull_StillLosesRune()
        {
            // у арбитра проверка пустой колоды идёт раньше проверки полной руки
            var s = NewState(deck: 0);
            for (int i = 0; i < 8; i++) s.Players[1].AddHandCard(Cr(10 + i, 1, 1));
            s.EndTurn();
            Assert.Equal(25, s.Players[1].Health);
            Assert.Equal(8, s.Players[1].HandCount);
        }

        [Test]
        public static void Draw_TurnLimit50_LosesRunesEvenWithDeck()
        {
            var s = NewState(deck: 10);
            s.Turn = 2 * 50 - 1;                 // после EndTurn: Turn = 100 -> ход игрока #50
            s.EndTurn();
            Assert.Equal(25, s.Players[1].Health);
            Assert.Equal(10, s.Players[1].DeckSize);
            Assert.Equal(0, s.Players[1].HandCount);

            var s2 = NewState(deck: 10);
            s2.Turn = 2 * 49 - 1;                // ход #49 — ещё обычный добор
            s2.EndTurn();
            Assert.Equal(30, s2.Players[1].Health);
            Assert.Equal(9, s2.Players[1].DeckSize);
        }

        [Test]
        public static void Draw_HandFull_ExtraCardsBurn_DeckUntouched()
        {
            var s = NewState(deck: 10);
            for (int i = 0; i < 7; i++) s.Players[1].AddHandCard(Cr(10 + i, 1, 1));
            s.Players[1].NextTurnDraw = 3;
            s.EndTurn();
            Assert.Equal(8, s.Players[1].HandCount);
            Assert.Equal(9, s.Players[1].DeckSize, "only one card actually drawn");
            Assert.Equal(3, s.Players[1].DrawShown, "referee still shows the requested draw");
            Assert.Equal(1, s.Players[1].NextTurnDraw);
        }

        // ------------------------------------------------------------ смена хода и мана

        [Test]
        public static void EndTurn_ManaGrowsToTwelve_CreaturesReady()
        {
            var s = NewState(mana: 11);
            s.Players[1].MaxMana = 11;
            Board(s, 0, Cr(1, 1, 1));
            Board(s, 1, Cr(2, 1, 1), canAttack: false);
            s.Players[0].Mana = 3;
            s.Apply(GameAction.Attack(1, -1));
            s.EndTurn();
            Assert.Equal(1, s.Current);
            Assert.Equal(1, s.Turn);
            Assert.Equal(12, s.Players[1].MaxMana);
            Assert.Equal(12, s.Players[1].Mana);
            Assert.True(Cre(s, 1, 2).CanAttack);
            Assert.False(Cre(s, 0, 1).CanAttack);
            Assert.False(Cre(s, 0, 1).HasAttacked);
            Assert.Equal(3, s.Players[0].Mana, "current mana of the player who ended the turn is kept");
            s.EndTurn();
            Assert.Equal(12, s.Players[0].MaxMana, "capped at 12");
            Assert.True(Cre(s, 0, 1).CanAttack);
        }

        [Test]
        public static void SecondPlayer_BonusMana_UntilFirstFullSpend()
        {
            // второй игрок: maxMana 1 при создании, бонус +1 к максимуму, пока впервые не потратит всё
            var s = NewState(mana: 1);
            s.Players[1].MaxMana = 1;
            s.Players[1].Mana = 1;
            s.Players[1].BonusManaTurns = 1;

            s.EndTurn();                          // первый ход второго игрока
            Assert.Equal(2, s.Players[1].MaxMana);
            s.Players[1].Mana = 1;                // потратил не всё
            s.EndTurn();
            s.EndTurn();
            Assert.Equal(3, s.Players[1].MaxMana, "bonus still active");
            s.Players[1].Mana = 0;                // потратил всё
            s.EndTurn();
            s.EndTurn();
            Assert.Equal(3, s.Players[1].MaxMana, "bonus removed: 3 + 1 - 1");
            Assert.Equal(0, s.Players[1].BonusManaTurns);
            s.Players[1].Mana = 0;
            s.EndTurn();
            s.EndTurn();
            Assert.Equal(4, s.Players[1].MaxMana);

            // с бонусом максимум 13, без — 12
            s.Players[1].MaxMana = 12;
            s.Players[1].BonusManaTurns = 1;
            s.Players[1].Mana = 5;
            s.EndTurn();
            s.EndTurn();
            Assert.Equal(13, s.Players[1].MaxMana);
        }

        // ------------------------------------------------------------ легальные ходы и последовательности

        [Test]
        public static void LegalActions_OrderMatchesReferee()
        {
            var s = NewState(mana: 5);
            var me = s.Players[0];
            me.AddHandCard(Cr(1, 1, 1, cost: 1));                                  // SUMMON
            me.AddHandCard(Item(CardType.GreenItem, 2, 1, 1, cost: 1));            // green
            me.AddHandCard(Cr(3, 9, 9, cost: 9));                                  // не хватает маны
            me.AddHandCard(Item(CardType.BlueItem, 4, 0, -2, cost: 2));            // blue
            me.AddHandCard(Item(CardType.RedItem, 5, 0, -2, cost: 2));             // red
            Board(s, 0, Cr(10, 2, 2));
            Board(s, 0, Cr(11, 2, 2), canAttack: false);
            Board(s, 1, Cr(20, 2, 2));
            Board(s, 1, Cr(21, 2, 2));
            Assert.Equal(
                "SUMMON 1;" +
                "ATTACK 10 -1;ATTACK 10 20;ATTACK 10 21;" +
                "USE 2 10;USE 2 11;" +
                "USE 4 20;USE 4 21;USE 4 -1;" +
                "USE 5 20;USE 5 21;" +
                "PASS",
                Joined(Legal(s)));

            var legal = Legal(s);
            foreach (var a in legal) Assert.True(s.IsLegal(a), a + " should be legal");
            Assert.False(s.IsLegal(GameAction.Summon(3)));
            Assert.False(s.IsLegal(GameAction.Attack(11, -1)));
            Assert.False(s.IsLegal(GameAction.Use(2, 20)));
            Assert.False(s.IsLegal(GameAction.Use(5, -1)));
            Assert.False(s.IsLegal(GameAction.Use(4, 10)));
            Assert.False(s.IsLegal(GameAction.Attack(10, 99)));
            Assert.False(s.IsLegal(GameAction.Attack(99, -1)));
        }

        [Test]
        public static void ApplySequence_SkipsIllegalAndPass_LikeReferee()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Cr(1, 1, 1));
            Board(s, 0, Cr(10, 2, 2));
            var seq = GameAction.ParseSequence("PASS;ATTACK 10 -1;ATTACK 10 -1;SUMMON 99;SUMMON 1");
            int illegal = s.ApplySequence(seq);
            Assert.Equal(2, illegal);
            Assert.Equal(28, s.Players[1].Health);
            Assert.True(OnBoard(s, 0, 1), "summon after illegal actions is still applied");
        }

        [Test]
        public static void ApplySequence_StopsAfterWin()
        {
            var s = NewState();
            s.Players[1].Health = 2;
            Board(s, 0, Cr(10, 2, 2));
            Board(s, 0, Cr(11, 2, 2));
            s.ApplySequence(GameAction.ParseSequence("ATTACK 10 -1;ATTACK 11 -1"));
            Assert.Equal(0, s.Winner);
            Assert.Equal(0, s.Players[1].Health);
            Assert.True(Cre(s, 0, 11).CanAttack, "second attack never happened");
        }

        // ------------------------------------------------------------ ввод/вывод

        private const string BattleInput =
            "27 4 22 25 1\n" +
            "22 4 21 20 2\n" +
            "5 2\n" +
            "3 SUMMON 7\n" +
            "3 ATTACK 7 -1\n" +
            "5\n" +
            "18 12 0 0 4 7 4 ------ 0 0 0\n" +
            "157 15 0 3 3 0 -1 ------ 1 0 1\n" +
            "3 9 1 0 1 2 2 ------ 0 0 0\n" +
            "3 7 -1 0 1 2 2 ------ 0 0 0\n" +
            "104 11 -1 0 4 4 1 --D--W 0 0 0\n";

        [Test]
        public static void FromInput_BuildsState()
        {
            var input = InputParser.ReadTurn(new StringReader(BattleInput));
            var s = GameState.FromInput(input, 7);
            Assert.Equal(0, s.Current);
            Assert.Equal(7, s.Turn);
            var me = s.Players[0];
            var opp = s.Players[1];
            Assert.Equal(27, me.Health);
            Assert.Equal(4, me.Mana);
            Assert.Equal(4, me.MaxMana);
            Assert.Equal(22, me.DeckSize);
            Assert.Equal(25, me.NextRune);
            Assert.Equal(1, me.NextTurnDraw);
            Assert.Equal(2, me.HandCount);
            Assert.Equal(2, me.HandKnown);
            Assert.Equal(1, me.BoardCount);
            Assert.True(me.Board[0].CanAttack);
            Assert.Equal(22, opp.Health);
            Assert.Equal(4, opp.MaxMana);
            Assert.Equal(20, opp.NextRune);
            Assert.Equal(2, opp.NextTurnDraw);
            Assert.Equal(5, opp.HandCount);
            Assert.Equal(0, opp.HandKnown);
            Assert.Equal(2, opp.BoardCount);
            Assert.Equal(11, opp.Board[1].InstanceId);
            Assert.Equal(Abilities.Drain | Abilities.Ward, opp.Board[1].Abilities);
            Assert.False(opp.Board[0].CanAttack);
            Assert.Equal(-1, s.Winner);
        }

        [Test]
        public static void ToInputLines_RoundTrips()
        {
            var input = InputParser.ReadTurn(new StringReader(BattleInput));
            var s = GameState.FromInput(input);
            var lines = s.ToInputLines();
            Assert.Equal("27 4 22 25 1", lines[0]);
            Assert.Equal("22 4 21 20 2", lines[1]);
            Assert.Equal("5 0", lines[2]);
            Assert.Equal("5", lines[3]);
            Assert.Equal("157 15 0 3 3 0 -1 ------ 1 0 1", lines[5]);
            Assert.Equal("104 11 -1 0 4 4 1 --D--W 0 0 0", lines[8]);

            var again = GameState.FromInput(InputParser.ReadTurn(new StringReader(string.Join("\n", lines) + "\n")));
            Assert.Equal(string.Join("\n", lines), string.Join("\n", again.ToInputLines()));
        }

        [Test]
        public static void IsSecondPlayer_UsesFirstOwnCard()
        {
            var t = new TurnInput();
            t.Cards.Add(Cr(4, 1, 1));
            Assert.True(GameState.IsSecondPlayer(t));
            t.Cards.Clear();
            t.Cards.Add(Cr(3, 1, 1));
            Assert.False(GameState.IsSecondPlayer(t));
            t.Cards.Clear();
            t.Cards.Add(new Card(1, 3, Location.OpponentBoard, CardType.Creature, 1, 1, 1, Abilities.None, 0, 0, 0));
            Assert.True(GameState.IsSecondPlayer(t), "opponent has odd ids -> I am second");
            t.Cards.Clear();
            Assert.False(GameState.IsSecondPlayer(t));
        }

        [Test]
        public static void Clone_IsDeepCopy()
        {
            var s = NewState();
            s.Players[0].AddHandCard(Cr(1, 1, 1));
            Board(s, 0, Cr(10, 5, 5));
            Board(s, 1, Cr(20, 2, 2));
            var c = s.Clone();
            c.Apply(GameAction.Summon(1));
            c.Apply(GameAction.Attack(10, 20));
            Assert.Equal(1, s.Players[0].HandCount);
            Assert.Equal(1, s.Players[0].BoardCount);
            Assert.True(OnBoard(s, 1, 20));
            Assert.Equal(2, c.Players[0].BoardCount);
            Assert.False(OnBoard(c, 1, 20));

            var back = new GameState();
            back.CopyFrom(c);
            Assert.Equal(string.Join("\n", c.ToInputLines()), string.Join("\n", back.ToInputLines()));
        }
    }
}

namespace Locm.Tests
{
    public static class HashTests
    {
        [Test]
        public static void SameStateSameHash_BoardOrderIgnored()
        {
            var a = TestUtil.NewState();
            var b = TestUtil.NewState();
            TestUtil.Board(a, 0, TestUtil.Cr(1, 2, 2));
            TestUtil.Board(a, 0, TestUtil.Cr(2, 3, 3));
            TestUtil.Board(b, 0, TestUtil.Cr(2, 3, 3));
            TestUtil.Board(b, 0, TestUtil.Cr(1, 2, 2));
            Assert.Equal(a.Hash(), b.Hash());
            Assert.Equal(a.Hash(), a.Clone().Hash());
        }

        [Test]
        public static void DifferentStatesDifferentHash()
        {
            var a = TestUtil.NewState();
            TestUtil.Board(a, 0, TestUtil.Cr(1, 2, 2));
            TestUtil.Board(a, 1, TestUtil.Cr(2, 3, 3));
            ulong h0 = a.Hash();
            var b = a.Clone();
            b.Apply(GameAction.Attack(1, -1));
            Assert.True(h0 != b.Hash(), "face attack changes state");
            var c = a.Clone();
            c.Apply(GameAction.Attack(1, 2));
            Assert.True(b.Hash() != c.Hash());
            var d = a.Clone();
            d.Players[0].Mana--;
            Assert.True(h0 != d.Hash());
            var e = a.Clone();
            e.Players[1].HandCount++;
            Assert.True(h0 != e.Hash());
        }
    }
}
