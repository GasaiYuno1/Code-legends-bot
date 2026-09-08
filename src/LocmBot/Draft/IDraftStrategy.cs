using System.Collections.Generic;

namespace Locm
{
    public interface IDraftStrategy
    {
        /// <summary>Возвращает индекс (0..2) выбранной карты из предложенной тройки.</summary>
        int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked);
    }

    /// <summary>Заглушка этапа 0: всегда первая карта.</summary>
    public sealed class FirstCardDraft : IDraftStrategy
    {
        public int Pick(TurnInput input, IReadOnlyList<Card> alreadyPicked) => 0;
    }
}
