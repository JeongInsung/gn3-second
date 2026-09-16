using System;
using GN3.World;

namespace GN3.Quests
{
    public static class QuestGenerator
    {
        public static Quest Generate(Region region, int minCount, int maxCount, int minDifficulty, int maxDifficulty, Random rng)
        {
            int difficulty = rng.Next(minDifficulty, maxDifficulty + 1);

            // 난이도 1=인간형 약탈 세력, 2=지형 상징 동물, 3 이상=지역 보스급
            int tier = Math.Clamp(difficulty - 1, 0, 2);
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
