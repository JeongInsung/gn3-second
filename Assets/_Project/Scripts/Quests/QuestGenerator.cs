using System;
using GN3.World;

namespace GN3.Quests
{
    public static class QuestGenerator
    {
        /// <summary>tierOverride를 주면(계층 0~2 고정) 난이도는 그 계층에서 역산한다(난이도 1~3이 계층 0~2에 정확히
        /// 1:1 대응). 생략하면 기존처럼 minDifficulty~maxDifficulty에서 난이도를 굴려 계층을 정한다(QuestBoard가
        /// 이미 깬 계층 비율을 낮추려고 계층을 먼저 가중 추첨할 때 tierOverride를 쓴다).</summary>
        public static Quest Generate(Region region, int minCount, int maxCount, Random rng,
            int? tierOverride = null, int minDifficulty = 1, int maxDifficulty = 3)
        {
            // 난이도 1=인간형 약탈 세력, 2=지형 상징 동물, 3 이상=지역 보스급
            int tier = tierOverride ?? Math.Clamp(rng.Next(minDifficulty, maxDifficulty + 1) - 1, 0, 2);
            int difficulty = tier + 1;
            int count = tier == 2 ? 1 : rng.Next(minCount, maxCount + 1); // 보스는 단독 등장

            RegionInfo.GetEncounter(region, tier, rng, out string enemyName, out string locationName);
            int durationDays = RollDurationDays(tier, rng);

            string title = locationName != null
                ? $"[{RegionInfo.DisplayName(region)}] {locationName} — {enemyName} 토벌"
                : $"[{RegionInfo.DisplayName(region)}] {enemyName} {count}마리 처치";

            return new Quest(title, region, locationName, enemyName, count, difficulty, durationDays);
        }

        private static int RollDurationDays(int tier, Random rng)
        {
            (int min, int max) = tier switch
            {
                0 => (1, 2),
                1 => (2, 3),
                _ => (3, 5),
            };
            return rng.Next(min, max + 1);
        }
    }
}
