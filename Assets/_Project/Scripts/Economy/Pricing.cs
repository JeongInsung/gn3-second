using GN3.Mercenaries;
using GN3.Quests;

namespace GN3.Economy
{
    /// <summary>고용비·퀘스트 보상 공식. 숫자를 한곳에 모아 밸런스를 맞추기 쉽게 한다.</summary>
    public static class Pricing
    {
        /// <summary>
        /// 지금 강함(등급 전투력 × 0.8)에 재능 배율(F ×0.7 ~ S ×2.3)과 나이 배율(잘 자라는 10대 ×1.2 ~ 40대 이상 ×0.75)을 곱하고 레어 패시브 +60G.
        /// D 재능 20대 전사(전투력 106) 약 85G. 주급은 이 값의 20%라 강해질수록 오른다.
        /// </summary>
        public static int HireCost(Mercenary mercenary) =>
            UnityEngine.Mathf.RoundToInt(mercenary.GradePower * 0.8f * GradeTable.PriceMultiplier(mercenary.Talent) * GrowthTuning.PriceMultiplier(mercenary.Age))
            + (mercenary.HasRarePassive ? 60 : 0);

        /// <summary>쉬운 의뢰 약 130G, 보스 토벌 약 260G.</summary>
        public static int QuestReward(Quest quest) =>
            60 * quest.Difficulty + 20 * quest.EnemyCount + 15 * quest.DurationDays;
    }
}
