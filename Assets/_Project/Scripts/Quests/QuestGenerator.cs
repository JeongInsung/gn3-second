using System;
using System.Collections.Generic;
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
            RegionInfo.GetEncounter(region, tier, rng, out string enemyName, out string locationName);
            var waves = RollWaves(region, tier, enemyName, minCount, maxCount, rng);
            int durationDays = RollDurationDays(tier, rng);

            int count = waves[waves.Count - 1].Count;
            string title = locationName != null
                ? $"[{RegionInfo.DisplayName(region)}] {locationName} — {enemyName} 토벌"
                : waves.Count == 1
                    ? $"[{RegionInfo.DisplayName(region)}] {enemyName} {count}마리 처치"
                    : $"[{RegionInfo.DisplayName(region)}] {enemyName} 토벌";

            return new Quest(title, region, locationName, enemyName, count, difficulty, durationDays, waves);
        }

        /// <summary>
        /// 라운드 구성. 마지막 라운드가 퀘스트의 대표 적(계층 그대로)이고, 앞 라운드는 그보다 낮거나 같은 계층의 다른 무리.
        /// 약탈 세력 1~2라운드, 지형 동물 2~3라운드, 보스 3라운드(약탈 세력 → 지형 동물 → 보스 단독).
        /// 한 라운드짜리는 예전처럼 minCount~maxCount마리, 여러 라운드면 라운드마다 2~3마리.
        /// </summary>
        private static List<QuestWave> RollWaves(Region region, int tier, string mainEnemy, int minCount, int maxCount, Random rng)
        {
            int waveCount = tier switch
            {
                0 => rng.Next(1, 3),
                1 => rng.Next(2, 4),
                _ => 3,
            };

            var waves = new List<QuestWave>(waveCount);
            for (int i = 0; i < waveCount; i++)
            {
                bool last = i == waveCount - 1;
                int waveTier = last ? tier : tier == 2 ? Math.Min(i, 1) : rng.Next(0, tier + 1);
                string name = last ? mainEnemy : RegionInfo.GetMonsterName(region, waveTier, rng);
                int count = waveTier == 2 ? 1 // 보스는 단독 등장
                    : waveCount == 1 ? rng.Next(minCount, maxCount + 1)
                    : rng.Next(2, Math.Max(2, Math.Min(maxCount, 3)) + 1);
                waves.Add(new QuestWave(name, count, waveTier));
            }
            return waves;
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
