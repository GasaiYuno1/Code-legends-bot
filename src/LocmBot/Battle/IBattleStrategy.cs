namespace Locm
{
    public interface IBattleStrategy
    {
        /// <summary>Возвращает строку действий хода в формате арбитра ("SUMMON 5;ATTACK 5 -1").</summary>
        string PlayTurn(TurnInput input, TurnClock clock);

        /// <summary>Прогрев (JIT) в свободное время первого хода драфта; должен уложиться в часы.</summary>
        void WarmUp(TurnClock clock);

        /// <summary>Ход драфта (тройка общая для обоих игроков) — данные для модели колоды противника.</summary>
        void ObserveDraft(TurnInput input);
    }

    /// <summary>Заглушка этапа 0: ничего не делает.</summary>
    public sealed class PassBattle : IBattleStrategy
    {
        public string PlayTurn(TurnInput input, TurnClock clock) => "PASS";
        public void WarmUp(TurnClock clock) { }
        public void ObserveDraft(TurnInput input) { }
    }
}
