using GN3.Mercenaries;
using GN3.Quests;

namespace GN3.Economy
{
    /// <summary>고용비·퀘스트 보상 공식. 숫자를 한곳에 모아 밸런스를 맞추기 쉽게 한다.</summary>
    public static class Pricing
    {
        /// <summary>Lv1 약 90G, Lv3 약 170G, 레어 패시브는 +60G.</summary>
        public static int HireCost(Mercenary mercenary) =>
            50 + 40 * mercenary.Level + (mercenary.HasRarePassive ? 60 : 0);

        /// <summary>쉬운 의뢰 약 130G, 보스 토벌 약 260G.</summary>
        public static int QuestReward(Quest quest) =>
            60 * quest.Difficulty + 20 * quest.EnemyCount + 15 * quest.DurationDays;
    }
}
