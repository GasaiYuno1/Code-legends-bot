using System;
using System.Collections.Generic;
using System.Text;

namespace Locm
{
    /// <summary>
    /// Симулятор правил боя — построчная копия логики GameState/Gamer официального арбитра.
    /// Players[0] — игрок, чей ввод получен («я»), Players[1] — противник; Current — чей сейчас ход.
    /// Неизвестные карты (рука противника, колоды, добор) учитываются только счётчиками.
    /// </summary>
    public sealed class GameState
    {
        public const int MaxHand = 8;
        public const int MaxBoard = 6;
        public const int MaxMana = 12;
        public const int InitialHealth = 30;
        public const int PlayerTurnLimit = 50;

        public readonly PlayerState[] Players = { new PlayerState(), new PlayerState() };
        public int Current;
        public int Winner = -1;
        /// <summary>Счётчик ходов арбитра: чётный у первого игрока, нечётный у второго; номер хода игрока = Turn / 2 (лимит 50).</summary>
        public int Turn;

        public PlayerState Me => Players[Current];
        public PlayerState Opp => Players[1 - Current];
        public bool IsOver => Winner >= 0;

        /// <summary>Значение Turn для своего battleTurn-го хода боя (с нуля) первым или вторым игроком.</summary>
        public static int RefereeTurn(int myBattleTurn, bool secondPlayer) => 2 * myBattleTurn + (secondPlayer ? 1 : 0);

        /// <summary>
        /// Второй игрок получает чётные instanceId (арбитр раздаёт id = 2*i + player + 1).
        /// Если своих карт нет — смотрим на карты противника; если карт нет вовсе — считаем первым.
        /// </summary>
        public static bool IsSecondPlayer(TurnInput input)
        {
            foreach (var c in input.Cards)
                if (c.Location != Location.OpponentBoard) return c.InstanceId % 2 == 0;
            foreach (var c in input.Cards)
                return c.InstanceId % 2 != 0;
            return false;
        }

        public static GameState FromInput(TurnInput input) => FromInput(input, 0);

        /// <summary>Состояние на начало моего хода по вводу арбитра. turn — счётчик арбитра (см. RefereeTurn).</summary>
        public static GameState FromInput(TurnInput input, int turn)
        {
            var s = new GameState();
            var me = s.Players[0];
            var opp = s.Players[1];

            me.Health = input.Me.Health;
            me.MaxMana = input.Me.Mana;      // арбитр присылает maxMana; в начале хода currentMana == maxMana
            me.Mana = input.Me.Mana;
            me.DeckSize = input.Me.DeckSize;
            me.NextRune = input.Me.Rune;
            me.DrawShown = input.Me.Draw;    // сколько карт я добрал в начале этого хода
            me.NextTurnDraw = 1;

            opp.Health = input.Opponent.Health;
            opp.MaxMana = input.Opponent.Mana;
            opp.Mana = 0;
            opp.DeckSize = input.Opponent.DeckSize;
            opp.NextRune = input.Opponent.Rune;
            opp.DrawShown = input.Opponent.Draw;
            opp.NextTurnDraw = input.Opponent.Draw;   // столько противник доберёт (плюс руны, которые я пробью)
            opp.HandCount = input.OpponentHandSize;

            foreach (var c in input.Cards)
            {
                switch (c.Location)
                {
                    case Location.MyHand: me.AddHandCard(c); break;
                    case Location.MyBoard: me.AddCreature(Creature.FromInput(c, true)); break;
                    case Location.OpponentBoard: opp.AddCreature(Creature.FromInput(c, false)); break;
                }
            }

            s.Current = 0;
            s.Turn = turn;
            s.CheckWinCondition();
            return s;
        }

        public void CopyFrom(GameState o)
        {
            Players[0].CopyFrom(o.Players[0]);
            Players[1].CopyFrom(o.Players[1]);
            Current = o.Current;
            Winner = o.Winner;
            Turn = o.Turn;
        }

        public GameState Clone()
        {
            var s = new GameState();
            s.CopyFrom(this);
            return s;
        }

        // ---------------------------------------------------------------- легальные ходы

        /// <summary>Все легальные действия текущего игрока в порядке арбитра: SUMMON, ATTACK, USE, PASS.</summary>
        public void LegalActions(List<GameAction> into)
        {
            into.Clear();
            var me = Me;
            var opp = Opp;

            if (me.BoardCount < MaxBoard)
            {
                for (int i = 0; i < me.HandKnown; i++)
                {
                    if (me.Hand[i].Type != CardType.Creature || me.Hand[i].Cost > me.Mana) continue;
                    into.Add(GameAction.Summon(me.Hand[i].InstanceId));
                }
            }

            bool guards = opp.HasGuard();
            for (int i = 0; i < me.BoardCount; i++)
            {
                if (!me.Board[i].CanAttack) continue;
                int id = me.Board[i].InstanceId;
                if (guards)
                {
                    for (int j = 0; j < opp.BoardCount; j++)
                        if (opp.Board[j].Has(Abilities.Guard)) into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
                }
                else
                {
                    into.Add(GameAction.Attack(id, GameAction.Face));
                    for (int j = 0; j < opp.BoardCount; j++)
                        into.Add(GameAction.Attack(id, opp.Board[j].InstanceId));
                }
            }

            for (int i = 0; i < me.HandKnown; i++)
            {
                Card c = me.Hand[i];
                if (c.Type == CardType.Creature || c.Cost > me.Mana) continue;
                if (c.Type == CardType.GreenItem)
                {
                    for (int j = 0; j < me.BoardCount; j++)
                        into.Add(GameAction.Use(c.InstanceId, me.Board[j].InstanceId));
                }
                else
                {
                    for (int j = 0; j < opp.BoardCount; j++)
                        into.Add(GameAction.Use(c.InstanceId, opp.Board[j].InstanceId));
                    if (c.Type == CardType.BlueItem) into.Add(GameAction.Use(c.InstanceId, GameAction.Face));
                }
            }

            into.Add(GameAction.Pass);
        }

        /// <summary>Легально ли действие для текущего игрока (те же правила, что и LegalActions).</summary>
        public bool IsLegal(GameAction a)
        {
            var me = Me;
            var opp = Opp;
            switch (a.Type)
            {
                case ActionType.Pass:
                    return true;

                case ActionType.Summon:
                {
                    if (me.BoardCount >= MaxBoard) return false;
                    int i = me.FindHand(a.Id);
                    return i >= 0 && me.Hand[i].Type == CardType.Creature && me.Hand[i].Cost <= me.Mana;
                }

                case ActionType.Attack:
                {
                    int ai = me.FindCreature(a.Id);
                    if (ai < 0 || !me.Board[ai].CanAttack) return false;
                    bool guards = opp.HasGuard();
                    if (a.Target == GameAction.Face) return !guards;
                    int di = opp.FindCreature(a.Target);
                    if (di < 0) return false;
                    return !guards || opp.Board[di].Has(Abilities.Guard);
                }

                case ActionType.Use:
                {
                    int i = me.FindHand(a.Id);
                    if (i < 0) return false;
                    Card c = me.Hand[i];
                    if (c.Type == CardType.Creature || c.Cost > me.Mana) return false;
                    if (c.Type == CardType.GreenItem) return a.Target != GameAction.Face && me.FindCreature(a.Target) >= 0;
                    if (a.Target == GameAction.Face) return c.Type == CardType.BlueItem;
                    return opp.FindCreature(a.Target) >= 0;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- применение действий

        /// <summary>Как арбитр: нелегальное действие пропускается (false), PASS — пустое действие.</summary>
        public bool TryApply(GameAction a)
        {
            if (!IsLegal(a)) return false;
            Apply(a);
            return true;
        }

        /// <summary>
        /// Ход целиком, как его исполняет арбитр: действия по порядку, нелегальные и PASS пропускаются,
        /// после конца партии ничего не применяется. Возвращает число пропущенных нелегальных действий.
        /// </summary>
        public int ApplySequence(IList<GameAction> actions)
        {
            int illegal = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                if (IsOver) break;
                if (actions[i].IsPass) continue;
                if (!TryApply(actions[i])) illegal++;
            }
            return illegal;
        }

        /// <summary>Применить действие, предполагая его легальность (GameState.AdvanceState(action) арбитра).</summary>
        public void Apply(GameAction a)
        {
            var me = Me;
            var opp = Opp;
            switch (a.Type)
            {
                case ActionType.Pass:
                    return;

                case ActionType.Summon:
                {
                    int hi = me.FindHand(a.Id);
                    if (hi < 0) throw Illegal(a, "card not in hand");
                    Card c = me.Hand[hi];
                    me.RemoveHand(hi);
                    me.Mana -= c.Cost;
                    me.AddCreature(Creature.Summon(c));
                    me.ModifyHealth(c.MyHealthChange);
                    opp.ModifyHealth(c.OpponentHealthChange);
                    me.NextTurnDraw += c.CardDraw;
                    break;
                }

                case ActionType.Attack:
                {
                    int ai = me.FindCreature(a.Id);
                    if (ai < 0) throw Illegal(a, "attacker not on board");
                    Creature att = me.Board[ai];
                    if (a.Target == GameAction.Face)
                    {
                        att.CanAttack = false;
                        att.HasAttacked = true;
                        att.ClearSummonEffects();
                        me.Board[ai] = att;
                        me.ModifyHealth(att.Has(Abilities.Drain) ? att.Attack : 0);
                        opp.ModifyHealth(-att.Attack);
                    }
                    else
                    {
                        int di = opp.FindCreature(a.Target);
                        if (di < 0) throw Illegal(a, "defender not on board");
                        Creature def = opp.Board[di];
                        bool attDied, defDied;
                        int healthGain, healthTaken;
                        ResolveAttack(ref att, ref def, out attDied, out defDied, out healthGain, out healthTaken);
                        if (defDied) opp.RemoveCreature(di); else opp.Board[di] = def;
                        if (attDied) me.RemoveCreature(ai); else me.Board[ai] = att;
                        me.ModifyHealth(healthGain);
                        opp.ModifyHealth(healthTaken);
                    }
                    break;
                }

                case ActionType.Use:
                {
                    int hi = me.FindHand(a.Id);
                    if (hi < 0) throw Illegal(a, "item not in hand");
                    Card item = me.Hand[hi];
                    me.RemoveHand(hi);
                    me.Mana -= item.Cost;

                    if (item.Type == CardType.GreenItem)
                    {
                        int ti = me.FindCreature(a.Target);
                        if (ti < 0) throw Illegal(a, "target not on my board");
                        Creature t = me.Board[ti];
                        bool died = ResolveUse(item, ref t);
                        // Арбитр считает, что зелёный предмет не убивает: при defense <= 0 существо остаётся как было.
                        if (!died) me.Board[ti] = t;
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.OpponentHealthChange);
                    }
                    else if (a.Target == GameAction.Face)
                    {
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.Defense + item.OpponentHealthChange);
                    }
                    else
                    {
                        int ti = opp.FindCreature(a.Target);
                        if (ti < 0) throw Illegal(a, "target not on opponent board");
                        Creature t = opp.Board[ti];
                        bool died = ResolveUse(item, ref t);
                        if (died) opp.RemoveCreature(ti); else opp.Board[ti] = t;
                        me.ModifyHealth(item.MyHealthChange);
                        opp.ModifyHealth(item.OpponentHealthChange);
                    }
                    me.NextTurnDraw += item.CardDraw;
                    break;
                }
            }
            CheckWinCondition();
        }

        private static InvalidOperationException Illegal(GameAction a, string why) =>
            new InvalidOperationException("illegal action " + a + ": " + why);

        /// <summary>GameState.ResolveAttack(attacker, defender) арбитра. att/def — существа «до», на выходе — «после».</summary>
        public static void ResolveAttack(ref Creature att, ref Creature def,
                                         out bool attackerDied, out bool defenderDied, out int healthGain, out int healthTaken)
        {
            Creature a0 = att;
            Creature d0 = def;

            att.CanAttack = false;
            att.HasAttacked = true;
            att.ClearSummonEffects();
            def.ClearSummonEffects();

            // Ward снимается первым же входящим ударом с уроном > 0
            if (d0.Has(Abilities.Ward)) SetWard(ref def, a0.Attack == 0);
            if (a0.Has(Abilities.Ward)) SetWard(ref att, d0.Attack == 0);

            int damageGiven = d0.Has(Abilities.Ward) ? 0 : a0.Attack;
            int damageTaken = a0.Has(Abilities.Ward) ? 0 : d0.Attack;
            healthGain = 0;
            healthTaken = 0;

            // атакующий бьёт
            defenderDied = damageGiven >= d0.Defense;
            if (a0.Has(Abilities.Breakthrough) && defenderDied) healthTaken = d0.Defense - damageGiven;
            if (a0.Has(Abilities.Lethal) && damageGiven > 0) defenderDied = true;
            if (a0.Has(Abilities.Drain) && damageGiven > 0) healthGain = a0.Attack;
            if (!defenderDied) def.Defense -= damageGiven;

            // защитник отвечает (его Breakthrough/Drain не работают)
            attackerDied = damageTaken >= a0.Defense;
            if (d0.Has(Abilities.Lethal) && damageTaken > 0) attackerDied = true;
            if (!attackerDied) att.Defense -= damageTaken;
        }

        /// <summary>GameState.ResolveUse(item, target) арбитра. Возвращает true, если существо погибло (defense <= 0).</summary>
        public static bool ResolveUse(Card item, ref Creature t)
        {
            t.ClearSummonEffects();
            if (item.Type == CardType.GreenItem)
            {
                t.Abilities |= item.Abilities;
                if ((item.Abilities & Abilities.Charge) != 0) t.CanAttack = !t.HasAttacked;
            }
            else
            {
                t.Abilities &= ~item.Abilities;
            }

            t.Attack = Math.Max(0, t.Attack + item.Attack);

            if (t.Has(Abilities.Ward) && item.Defense < 0)
                t.Abilities &= ~Abilities.Ward;      // Ward поглощает урон предмета целиком
            else
                t.Defense += item.Defense;

            return t.Defense <= 0;
        }

        private static void SetWard(ref Creature c, bool on)
        {
            if (on) c.Abilities |= Abilities.Ward; else c.Abilities &= ~Abilities.Ward;
        }

        public void CheckWinCondition()
        {
            if (Opp.Health <= 0) Winner = Current;           // сначала честная победа
            else if (Me.Health <= 0) Winner = 1 - Current;   // потом самоубийство
        }

        // ---------------------------------------------------------------- смена хода

        /// <summary>
        /// GameState.AdvanceState() арбитра: конец хода текущего игрока и начало хода следующего —
        /// мана (+1 до 12, бонус второго игрока), готовность существ, добор с рунами.
        /// </summary>
        public void EndTurn()
        {
            CheckWinCondition();

            var prev = Me;
            for (int i = 0; i < prev.BoardCount; i++)
            {
                prev.Board[i].CanAttack = false;
                prev.Board[i].HasAttacked = false;
            }
            prev.DrawShown = prev.NextTurnDraw;

            Current = 1 - Current;
            Turn++;
            var p = Me;

            if (p.MaxMana < MaxMana + (p.BonusManaTurns > 0 ? 1 : 0))
                p.MaxMana++;

            // бонус второго игрока пропадает после первого хода, в котором он потратил всю ману
            if (p.BonusManaTurns > 0 && p.Mana == 0)
            {
                p.BonusManaTurns--;
                if (p.BonusManaTurns == 0) p.MaxMana--;
            }

            p.Mana = p.MaxMana;

            for (int i = 0; i < p.BoardCount; i++)
                p.Board[i].CanAttack = true;

            p.DrawCards(p.NextTurnDraw, Turn / 2);
            p.DrawShown = p.NextTurnDraw;
            p.NextTurnDraw = 1;
            CheckWinCondition();
        }

        // ---------------------------------------------------------------- вывод

        /// <summary>
        /// Состояние в формате ввода арбитра с точки зрения текущего игрока (без списка действий противника:
        /// строка "oppHand 0"). Карты: известная рука, мой стол, стол противника — как у арбитра.
        /// </summary>
        public string[] ToInputLines()
        {
            var me = Me;
            var opp = Opp;
            var lines = new List<string>(4 + me.HandKnown + me.BoardCount + opp.BoardCount);
            lines.Add(me.ToInputLine());
            lines.Add(opp.ToInputLine());
            lines.Add(opp.HandCount + " 0");
            lines.Add((me.HandKnown + me.BoardCount + opp.BoardCount).ToString());
            for (int i = 0; i < me.HandKnown; i++) lines.Add(me.Hand[i].ToInputLine(Location.MyHand));
            for (int i = 0; i < me.BoardCount; i++) lines.Add(me.Board[i].ToInputLine(false));
            for (int i = 0; i < opp.BoardCount; i++) lines.Add(opp.Board[i].ToInputLine(true));
            return lines.ToArray();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("turn ").Append(Turn).Append(" current P").Append(Current);
            if (IsOver) sb.Append(" winner P").Append(Winner);
            sb.AppendLine();
            for (int p = 0; p < 2; p++)
            {
                var pl = Players[p];
                sb.Append("P").Append(p).Append(": ").Append(pl).AppendLine();
                for (int i = 0; i < pl.HandKnown; i++) sb.Append("   hand ").Append(pl.Hand[i]).AppendLine();
                for (int i = 0; i < pl.BoardCount; i++) sb.Append("   board ").Append(pl.Board[i]).AppendLine();
            }
            return sb.ToString();
        }
    }
}
